using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Api;

namespace Nexus.Web.Services.Webhooks;

public static class WebhookPayloads
{
    // Mesmas convenções da API: camelCase e enums como texto, para o ERP ler os dois do mesmo jeito.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}

/// <summary>Como a tarefa estava antes da mudança, para o evento dizer o que mudou.</summary>
public sealed record WorkItemSnapshot(
    string Title,
    string? Description,
    WorkItemType Type,
    WorkItemPriority Priority,
    Guid StatusId,
    string? AssigneeId,
    DateTime? StartDateUtc,
    DateTime? DueDateUtc,
    string? ExternalReference)
{
    public static WorkItemSnapshot Of(WorkItem item) => new(
        item.Title, item.Description, item.Type, item.Priority, item.StatusId, item.AssigneeId,
        item.StartDateUtc, item.DueDateUtc, item.ExternalReference);
}

/// <param name="Id">Id da conta; null para uma ação sem pessoa (integração sem criador, job).</param>
/// <param name="Name">Vazio para o publisher procurar o nome pela conta.</param>
public sealed record EventActor(string? Id, string Name = "");

// O único ponto que transforma "a tarefa mudou" em eventos de webhook (#75). Tela, API, quadro e
// MCP chamam aqui depois de salvar, e é aqui que se decide quais eventos saem: status e
// responsável têm evento próprio; o resto é task.updated com os campos que mudaram.
public class WorkItemEventPublisher(WebhookService webhooks, IConfiguration? configuration = null)
{
    public async Task TaskCreatedAsync(AppDbContext db, Guid workItemId, EventActor? actor, CancellationToken ct = default)
    {
        var item = await LoadAsync(db, workItemId, ct);
        if (item is null)
        {
            return;
        }

        var workspaceId = item.TaskList.Space.WorkspaceId;
        if (!await webhooks.AnySubscriberAsync(db, workspaceId, WebhookEvents.TaskCreated, ct))
        {
            return;
        }

        await EnqueueAsync(db, workspaceId, WebhookEvents.TaskCreated, item, await ActorAsync(db, actor, ct), previous: null, comment: null, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task TaskChangedAsync(AppDbContext db, Guid workItemId, WorkItemSnapshot previous, EventActor? actor, CancellationToken ct = default)
    {
        var item = await LoadAsync(db, workItemId, ct);
        if (item is null)
        {
            return;
        }

        var workspaceId = item.TaskList.Space.WorkspaceId;
        var statusChanged = previous.StatusId != item.StatusId;
        var assigneeChanged = previous.AssigneeId != item.AssigneeId;
        var otherChanges = ChangedFields(previous, item);
        if (!statusChanged && !assigneeChanged && otherChanges.Count == 0)
        {
            return;
        }

        var wanted = (statusChanged ? WebhookEvents.TaskStatusChanged : WebhookEvents.None)
                     | (assigneeChanged ? WebhookEvents.TaskAssigneeChanged : WebhookEvents.None)
                     | (otherChanges.Count > 0 ? WebhookEvents.TaskUpdated : WebhookEvents.None);
        var anySubscriber = false;
        foreach (var single in Singles(wanted))
        {
            anySubscriber |= await webhooks.AnySubscriberAsync(db, workspaceId, single, ct);
        }

        if (!anySubscriber)
        {
            return;
        }

        var resolvedActor = await ActorAsync(db, actor, ct);
        if (statusChanged)
        {
            var previousStatus = await db.TaskStatusDefinitions.AsNoTracking()
                .Where(s => s.Id == previous.StatusId)
                .Select(s => new { s.Id, s.Name, s.Category })
                .FirstOrDefaultAsync(ct);
            await EnqueueAsync(db, workspaceId, WebhookEvents.TaskStatusChanged, item, resolvedActor,
                previous: new Dictionary<string, object?> { ["status"] = previousStatus }, comment: null, ct);
        }

        if (assigneeChanged)
        {
            var previousAssignee = previous.AssigneeId is null ? null : await db.Users.AsNoTracking()
                .Where(u => u.Id == previous.AssigneeId)
                .Select(u => new { u.Id, Name = ApiMapping.DisplayNameOf(u.DisplayName, u.Email), u.Email })
                .FirstOrDefaultAsync(ct);
            await EnqueueAsync(db, workspaceId, WebhookEvents.TaskAssigneeChanged, item, resolvedActor,
                previous: new Dictionary<string, object?> { ["assignee"] = previousAssignee }, comment: null, ct);
        }

        if (otherChanges.Count > 0)
        {
            await EnqueueAsync(db, workspaceId, WebhookEvents.TaskUpdated, item, resolvedActor, previous: otherChanges, comment: null, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task CommentCreatedAsync(AppDbContext db, Guid commentId, EventActor? actor, CancellationToken ct = default)
    {
        var comment = await db.WorkItemComments.AsNoTracking()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == commentId, ct);
        if (comment is null)
        {
            return;
        }

        var item = await LoadAsync(db, comment.WorkItemId, ct);
        if (item is null)
        {
            return;
        }

        var workspaceId = item.TaskList.Space.WorkspaceId;
        if (!await webhooks.AnySubscriberAsync(db, workspaceId, WebhookEvents.CommentCreated, ct))
        {
            return;
        }

        var commentBody = new
        {
            id = comment.Id,
            content = comment.Content,
            author = comment.User is null ? null : ApiMapping.ToResponse(comment.User),
            createdAtUtc = comment.CreatedAtUtc,
        };
        await EnqueueAsync(db, workspaceId, WebhookEvents.CommentCreated, item, await ActorAsync(db, actor, ct), previous: null, comment: commentBody, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnqueueAsync(AppDbContext db, Guid workspaceId, WebhookEvents single, WorkItem item, PersonResponse? actor, object? previous, object? comment, CancellationToken ct)
    {
        var eventId = Guid.NewGuid();
        var payload = WebhookPayloads.Serialize(new
        {
            id = eventId,
            type = WebhookEventNames.Of(single),
            occurredAtUtc = DateTime.UtcNow,
            workspaceId,
            actor,
            task = ApiMapping.ToResponse(item, configuration?["PUBLIC_BASE_URL"]),
            previous,
            comment,
        });
        await webhooks.EnqueueAsync(db, workspaceId, single, eventId, payload, ct);
    }

    private static Dictionary<string, object?> ChangedFields(WorkItemSnapshot previous, WorkItem item)
    {
        var changed = new Dictionary<string, object?>();
        if (previous.Title != item.Title) changed["title"] = previous.Title;
        if (previous.Description != item.Description) changed["description"] = previous.Description;
        if (previous.Type != item.Type) changed["type"] = previous.Type;
        if (previous.Priority != item.Priority) changed["priority"] = previous.Priority;
        if (previous.StartDateUtc != item.StartDateUtc) changed["startDateUtc"] = previous.StartDateUtc;
        if (previous.DueDateUtc != item.DueDateUtc) changed["dueDateUtc"] = previous.DueDateUtc;
        if (previous.ExternalReference != item.ExternalReference) changed["externalReference"] = previous.ExternalReference;
        return changed;
    }

    private static IEnumerable<WebhookEvents> Singles(WebhookEvents flags)
    {
        foreach (var single in new[] { WebhookEvents.TaskCreated, WebhookEvents.TaskUpdated, WebhookEvents.TaskStatusChanged, WebhookEvents.TaskAssigneeChanged, WebhookEvents.CommentCreated })
        {
            if ((flags & single) == single)
            {
                yield return single;
            }
        }
    }

    private static async Task<PersonResponse?> ActorAsync(AppDbContext db, EventActor? actor, CancellationToken ct)
    {
        if (actor is null)
        {
            return null;
        }

        if (actor.Id is null)
        {
            return new PersonResponse("", actor.Name, null);
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor.Id)
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is null)
        {
            return new PersonResponse(actor.Id, actor.Name, null);
        }

        return new PersonResponse(user.Id, actor.Name.Length > 0 ? actor.Name : ApiMapping.DisplayNameOf(user.DisplayName, user.Email), user.Email);
    }

    // Instância própria, sem rastreamento: o chamador pode ter a tarefa rastreada no mesmo contexto.
    private static Task<WorkItem?> LoadAsync(AppDbContext db, Guid workItemId, CancellationToken ct) =>
        db.WorkItems.AsNoTracking()
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .Include(w => w.TaskList).ThenInclude(l => l.Space)
            .FirstOrDefaultAsync(w => w.Id == workItemId, ct);
}
