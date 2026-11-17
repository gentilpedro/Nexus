using System.Linq.Expressions;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

/// <summary>Corpo de criação de tarefa. Também é o JSON do POST /api/v1/tasks.</summary>
public sealed record CreateWorkItemRequest(
    Guid TaskListId,
    string? Title,
    string? Description = null,
    WorkItemType? Type = null,
    WorkItemPriority? Priority = null,
    Guid? StatusId = null,
    string? AssigneeId = null,
    DateTime? StartDateUtc = null,
    DateTime? DueDateUtc = null,
    string? ExternalReference = null);

/// <summary>
/// Corpo de atualização parcial. Campo ausente ou null não muda nada; string vazia limpa a
/// descrição, o responsável ou a referência externa.
/// </summary>
public sealed record UpdateWorkItemRequest(
    string? Title = null,
    string? Description = null,
    WorkItemType? Type = null,
    WorkItemPriority? Priority = null,
    Guid? StatusId = null,
    string? AssigneeId = null,
    DateTime? StartDateUtc = null,
    DateTime? DueDateUtc = null,
    string? ExternalReference = null);

/// <param name="UserId">Conta registrada como criadora da tarefa; null quando não há uma.</param>
/// <param name="DisplayName">Como o ator aparece na notificação de atribuição.</param>
public sealed record WorkItemActor(string? UserId, string DisplayName);

public sealed record WorkItemListFilter(
    Guid? TaskListId = null,
    Guid? StatusId = null,
    string? AssigneeId = null,
    DateTime? UpdatedSince = null,
    int Page = 1,
    int PageSize = 50);

public sealed record WorkItemListPage(IReadOnlyList<WorkItem> Items, int Page, int PageSize, int Total);

public enum WorkItemOutcomeStatus
{
    Ok,
    Invalid,
    NotFound,

    // Com Item: já existe tarefa com a mesma referência externa. Sem Item: outra pessoa alterou a
    // tarefa durante a atualização.
    Conflict,
}

public sealed record WorkItemOutcome(
    WorkItemOutcomeStatus Status,
    WorkItem? Item = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);

// Criação, leitura e atualização de tarefas para a API de integração (#66). Concentra as regras
// que a tela de tarefa aplica (lista do workspace, status da lista, responsável membro, datas em
// ordem, transição de status permitida) para um ERP não conseguir gravar o que a tela recusaria.
public class WorkItemService(
    IDbContextFactory<AppDbContext> dbFactory,
    WorkItemQueryService queries,
    Mailer? mailer = null,
    IConfiguration? configuration = null)
{
    public const int MaxPageSize = 100;
    public const int MaxTitleLength = 500;

    public async Task<WorkItemOutcome> CreateAsync(Guid workspaceId, CreateWorkItemRequest request, WorkItemActor actor, CancellationToken ct = default)
    {
        var errors = new ErrorBag();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var listBelongs = await db.TaskLists.AnyAsync(l => l.Id == request.TaskListId && l.Space.WorkspaceId == workspaceId, ct);
        if (!listBelongs)
        {
            errors.Add("taskListId", "Lista não encontrada neste workspace. Consulte GET /api/v1/lists.");
        }

        var title = ValidateTitle(request.Title, errors);
        var description = ValidateDescription(request.Description, errors);
        var externalReference = ValidateExternalReference(request.ExternalReference, errors);
        var assigneeId = await ResolveAssigneeAsync(db, workspaceId, request.AssigneeId, errors, ct);
        var (start, due) = ValidateDates(request.StartDateUtc, request.DueDateUtc, errors);

        Guid statusId = default;
        if (listBelongs)
        {
            statusId = await ResolveStatusAsync(db, request.TaskListId, request.StatusId, errors, ct);
        }

        if (errors.Any)
        {
            return Invalid(errors);
        }

        if (externalReference is not null)
        {
            var existing = await FindByExternalReferenceAsync(db, workspaceId, externalReference, exceptId: null, ct);
            if (existing is not null)
            {
                return new WorkItemOutcome(WorkItemOutcomeStatus.Conflict, existing);
            }
        }

        var item = new WorkItem
        {
            Id = Guid.NewGuid(),
            Number = await WorkItemNumbers.NextAsync(db, workspaceId, ct),
            TaskListId = request.TaskListId,
            Title = title!,
            Description = description,
            Type = request.Type ?? WorkItemType.Task,
            Priority = request.Priority ?? WorkItemPriority.Medium,
            StatusId = statusId,
            AssigneeId = assigneeId,
            StartDateUtc = start,
            DueDateUtc = due,
            ExternalReference = externalReference,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = actor.UserId,
        };
        db.WorkItems.Add(item);

        if (item.AssigneeId is not null && item.AssigneeId != actor.UserId)
        {
            await NotifyAssigneeAsync(db, item, actor, ct);
        }

        await db.SaveChangesAsync(ct);
        return new WorkItemOutcome(WorkItemOutcomeStatus.Ok, await LoadAsync(db, item.Id, ct));
    }

    public async Task<WorkItemOutcome> UpdateAsync(Guid workspaceId, string idOrCode, UpdateWorkItemRequest request, WorkItemActor actor, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.WorkItems
            .Where(w => w.TaskList.Space.WorkspaceId == workspaceId)
            .Where(Match(idOrCode))
            .FirstOrDefaultAsync(ct);
        if (item is null)
        {
            return new WorkItemOutcome(WorkItemOutcomeStatus.NotFound);
        }

        var errors = new ErrorBag();
        var title = request.Title is null ? item.Title : ValidateTitle(request.Title, errors);
        var description = request.Description is null ? item.Description : ValidateDescription(request.Description, errors);
        var externalReference = request.ExternalReference is null ? item.ExternalReference : ValidateExternalReference(request.ExternalReference, errors);
        var assigneeId = request.AssigneeId is null ? item.AssigneeId : await ResolveAssigneeAsync(db, workspaceId, request.AssigneeId, errors, ct);
        var (start, due) = ValidateDates(request.StartDateUtc ?? item.StartDateUtc, request.DueDateUtc ?? item.DueDateUtc, errors);

        var statusId = item.StatusId;
        if (request.StatusId is { } requestedStatus && requestedStatus != item.StatusId)
        {
            var belongs = await db.TaskStatusDefinitions.AnyAsync(s => s.Id == requestedStatus && s.TaskListId == item.TaskListId, ct);
            if (!belongs)
            {
                errors.Add("statusId", "Status não encontrado na lista desta tarefa. Consulte GET /api/v1/lists.");
            }
            else if (!await queries.IsStatusTransitionAllowedAsync(db, item.TaskListId, item.StatusId, requestedStatus, ct))
            {
                errors.Add("statusId", "Essa mudança de status não é permitida pelo fluxo de trabalho da lista.");
            }
            else
            {
                statusId = requestedStatus;
            }
        }

        if (errors.Any)
        {
            return Invalid(errors);
        }

        if (externalReference is not null && externalReference != item.ExternalReference)
        {
            var existing = await FindByExternalReferenceAsync(db, workspaceId, externalReference, exceptId: item.Id, ct);
            if (existing is not null)
            {
                return new WorkItemOutcome(WorkItemOutcomeStatus.Conflict, existing);
            }
        }

        var previousAssigneeId = item.AssigneeId;
        item.Title = title!;
        item.Description = description;
        item.Type = request.Type ?? item.Type;
        item.Priority = request.Priority ?? item.Priority;
        item.StatusId = statusId;
        item.AssigneeId = assigneeId;
        item.StartDateUtc = start;
        item.DueDateUtc = due;
        item.ExternalReference = externalReference;
        item.UpdatedAtUtc = DateTime.UtcNow;

        if (item.AssigneeId is not null && item.AssigneeId != previousAssigneeId && item.AssigneeId != actor.UserId)
        {
            await NotifyAssigneeAsync(db, item, actor, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new WorkItemOutcome(WorkItemOutcomeStatus.Conflict, Errors: new Dictionary<string, string[]>
            {
                [""] = ["Essa tarefa foi alterada por outra pessoa durante a atualização. Leia de novo e tente outra vez."],
            });
        }

        return new WorkItemOutcome(WorkItemOutcomeStatus.Ok, await LoadAsync(db, item.Id, ct));
    }

    public async Task<WorkItem?> GetAsync(Guid workspaceId, string idOrCode, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Query(db, workspaceId).Where(Match(idOrCode)).FirstOrDefaultAsync(ct);
    }

    public async Task<WorkItemListPage> ListAsync(Guid workspaceId, WorkItemListFilter filter, CancellationToken ct = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = Query(db, workspaceId);

        if (filter.TaskListId is { } listId)
        {
            query = query.Where(w => w.TaskListId == listId);
        }

        if (filter.StatusId is { } statusId)
        {
            query = query.Where(w => w.StatusId == statusId);
        }

        if (!string.IsNullOrWhiteSpace(filter.AssigneeId))
        {
            query = query.Where(w => w.AssigneeId == filter.AssigneeId);
        }

        if (filter.UpdatedSince is { } since)
        {
            var sinceUtc = ToUtc(since)!.Value;
            query = query.Where(w => (w.UpdatedAtUtc ?? w.CreatedAtUtc) >= sinceUtc);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(w => w.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new WorkItemListPage(items, page, pageSize, total);
    }

    private static IQueryable<WorkItem> Query(AppDbContext db, Guid workspaceId) => db.WorkItems
        .AsNoTracking()
        .Where(w => w.TaskList.Space.WorkspaceId == workspaceId)
        .Include(w => w.Status)
        .Include(w => w.Assignee)
        .Include(w => w.TaskList);

    private static Task<WorkItem> LoadAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        db.WorkItems
            .AsNoTracking()
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .Include(w => w.TaskList)
            .FirstAsync(w => w.Id == id, ct);

    // Aceita o id (GUID) ou o código mostrado às pessoas ("TASK-12", ou só "12").
    private static Expression<Func<WorkItem, bool>> Match(string idOrCode)
    {
        idOrCode = idOrCode.Trim();
        if (Guid.TryParse(idOrCode, out var id))
        {
            return w => w.Id == id;
        }

        var digits = idOrCode.StartsWith("TASK-", StringComparison.OrdinalIgnoreCase) ? idOrCode[5..] : idOrCode;
        if (int.TryParse(digits, out var number) && number > 0)
        {
            return w => w.Number == number;
        }

        return _ => false;
    }

    private static Task<WorkItem?> FindByExternalReferenceAsync(AppDbContext db, Guid workspaceId, string externalReference, Guid? exceptId, CancellationToken ct) =>
        Query(db, workspaceId)
            .Where(w => w.ExternalReference == externalReference)
            .Where(w => exceptId == null || w.Id != exceptId)
            .FirstOrDefaultAsync(ct);

    private static string? ValidateTitle(string? value, ErrorBag errors)
    {
        var title = value?.Trim() ?? "";
        if (title.Length == 0)
        {
            errors.Add("title", "Informe o título.");
            return null;
        }

        if (title.Length > MaxTitleLength)
        {
            errors.Add("title", $"O título pode ter no máximo {MaxTitleLength} caracteres.");
            return null;
        }

        return title;
    }

    private static string? ValidateDescription(string? value, ErrorBag errors)
    {
        var description = value?.Trim();
        if (string.IsNullOrEmpty(description))
        {
            return null;
        }

        if (description.Length > WorkItem.MaxDescriptionLength)
        {
            errors.Add("description", $"A descrição pode ter no máximo {WorkItem.MaxDescriptionLength} caracteres.");
            return null;
        }

        return description;
    }

    private static string? ValidateExternalReference(string? value, ErrorBag errors)
    {
        var reference = value?.Trim();
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        if (reference.Length > WorkItem.MaxExternalReferenceLength)
        {
            errors.Add("externalReference", $"A referência externa pode ter no máximo {WorkItem.MaxExternalReferenceLength} caracteres.");
            return null;
        }

        return reference;
    }

    private static async Task<string?> ResolveAssigneeAsync(AppDbContext db, Guid workspaceId, string? value, ErrorBag errors, CancellationToken ct)
    {
        var assigneeId = value?.Trim();
        if (string.IsNullOrEmpty(assigneeId))
        {
            return null;
        }

        var isMember = await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == assigneeId, ct);
        if (!isMember)
        {
            errors.Add("assigneeId", "O responsável precisa ser membro do workspace. Consulte GET /api/v1/members.");
            return null;
        }

        return assigneeId;
    }

    private static async Task<Guid> ResolveStatusAsync(AppDbContext db, Guid taskListId, Guid? requested, ErrorBag errors, CancellationToken ct)
    {
        if (requested is { } statusId)
        {
            var belongs = await db.TaskStatusDefinitions.AnyAsync(s => s.Id == statusId && s.TaskListId == taskListId, ct);
            if (!belongs)
            {
                errors.Add("statusId", "Status não encontrado nesta lista. Consulte GET /api/v1/lists.");
            }

            return statusId;
        }

        // Mesmo padrão da tela: o status marcado como inicial, senão o primeiro da lista.
        var fallback = await db.TaskStatusDefinitions
            .Where(s => s.TaskListId == taskListId)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.SortOrder)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (fallback is null)
        {
            errors.Add("statusId", "A lista não tem nenhum status configurado.");
            return default;
        }

        return fallback.Value;
    }

    private static (DateTime? Start, DateTime? Due) ValidateDates(DateTime? start, DateTime? due, ErrorBag errors)
    {
        var startUtc = ToUtc(start);
        var dueUtc = ToUtc(due);
        if (startUtc is not null && dueUtc is not null && startUtc > dueUtc)
        {
            errors.Add("startDateUtc", "A data de início precisa ser antes do prazo.");
        }

        return (startUtc, dueUtc);
    }

    // Npgsql exige Kind=Utc em timestamptz. Uma data sem fuso ("2026-11-20") conta como UTC, como
    // a tela de tarefa já faz.
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };

    // A mesma notificação (e o mesmo e-mail) que a tela de tarefa manda ao atribuir.
    private async Task NotifyAssigneeAsync(AppDbContext db, WorkItem item, WorkItemActor actor, CancellationToken ct)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = item.AssigneeId!,
            Type = NotificationType.TaskAssigned,
            Message = Notification.TruncateMessage($"{actor.DisplayName} atribuiu {item.Code} \"{item.Title}\" a você."),
            WorkItemId = item.Id,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow,
        });

        if (mailer is null)
        {
            return;
        }

        var assigneeEmail = await db.Users.Where(u => u.Id == item.AssigneeId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(assigneeEmail))
        {
            return;
        }

        // Nome do ator e título vêm de fora: codificados antes de entrar no HTML do e-mail.
        var safeActor = WebUtility.HtmlEncode(actor.DisplayName);
        var safeTitle = WebUtility.HtmlEncode(item.Title);
        var link = TaskUrl(item.TaskListId);
        var open = link is null ? "" : $"<p><a href='{link}'>Clique aqui para abrir</a>.</p>";
        await mailer.SendAsync(
            assigneeEmail,
            $"{safeActor} atribuiu uma tarefa a você no Nexus",
            $"<p>{safeActor} atribuiu a tarefa <strong>{item.Code} {safeTitle}</strong> a você.</p>{open}");
    }

    // Fora de um circuito Blazor não há NavigationManager; o endereço público vem da configuração.
    private string? TaskUrl(Guid taskListId)
    {
        var baseUrl = configuration?["PUBLIC_BASE_URL"];
        return string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/lists/{taskListId}/list";
    }

    private static WorkItemOutcome Invalid(ErrorBag errors) =>
        new(WorkItemOutcomeStatus.Invalid, Errors: errors.ToDictionary());

    private sealed class ErrorBag
    {
        private readonly Dictionary<string, List<string>> errors = [];

        public bool Any => errors.Count > 0;

        public void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        public Dictionary<string, string[]> ToDictionary() => errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}
