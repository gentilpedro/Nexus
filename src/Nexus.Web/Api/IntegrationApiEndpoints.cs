using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Api;

// Respostas JSON da API de integração. Separadas das entidades para a API ter um contrato
// estável: mudar uma coluna no banco não muda o que o ERP recebe.
public sealed record WorkspaceResponse(Guid Id, string Name, string Slug, bool IsArchived);

public sealed record SpaceResponse(Guid Id, string Name, IReadOnlyList<ListResponse> Lists);

public sealed record ListResponse(Guid Id, string Name, IReadOnlyList<StatusResponse> Statuses);

public sealed record StatusResponse(Guid Id, string Name, StatusCategory Category, bool IsDefault);

public sealed record MemberResponse(string Id, string Name, string? Email, WorkspaceRole Role);

public sealed record PersonResponse(string Id, string Name, string? Email);

public sealed record TaskListRef(Guid Id, string Name);

public sealed record TaskResponse(
    Guid Id,
    string Code,
    int Number,
    string Title,
    string? Description,
    WorkItemType Type,
    WorkItemPriority Priority,
    StatusResponse Status,
    TaskListRef List,
    PersonResponse? Assignee,
    DateTime? StartDateUtc,
    DateTime? DueDateUtc,
    string? ExternalReference,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string? Url);

public sealed record TaskPageResponse(IReadOnlyList<TaskResponse> Items, int Page, int PageSize, int Total);

/// <param name="Task">A tarefa que já existe com a mesma referência externa.</param>
public sealed record TaskConflictResponse(string Message, TaskResponse Task);

/// <summary>Filtros de GET /api/v1/tasks, lidos da query string. Todos opcionais.</summary>
public sealed class TaskListQuery
{
    public Guid? ListId { get; init; }
    public Guid? StatusId { get; init; }
    public string? AssigneeId { get; init; }
    public DateTime? UpdatedSince { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

// Rotas de /api/v1 (#66). Toda rota exige o esquema ApiKey; o workspace vem do claim da chave.
public static class IntegrationApiEndpoints
{
    public const string RoutePrefix = "/api/v1";
    public const string RateLimitPolicy = "api";

    public static void AddIntegrationApiRateLimit(this RateLimiterOptions options)
    {
        // Por chave, não por IP: um ERP inteiro costuma sair por um único endereço. O limitador
        // roda antes da autenticação do endpoint, então a partição usa o hash do cabeçalho (nunca
        // o valor em si, que ficaria em memória) e cai para o IP quando não há cabeçalho.
        options.AddPolicy(RateLimitPolicy, httpContext =>
        {
            var header = httpContext.Request.Headers.Authorization.ToString();
            var partition = header.Length > 0
                ? "key:" + WorkspaceApiKeyService.Hash(header)
                : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        });
    }

    public static IEndpointRouteBuilder MapIntegrationApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(RoutePrefix)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName })
            .RequireRateLimiting(RateLimitPolicy)
            .DisableAntiforgery()
            .AddEndpointFilter(RejectArchivedWorkspaceAsync);

        api.MapGet("/workspace", GetWorkspaceAsync)
            .WithTags("Workspace")
            .WithSummary("Workspace da chave")
            .WithDescription("Identifica o workspace ao qual a chave de API dá acesso.");

        api.MapGet("/lists", GetListsAsync)
            .WithTags("Workspace")
            .WithSummary("Spaces, listas e status")
            .WithDescription("Ids necessários para criar tarefas: a lista de destino e, se quiser escolher, o status inicial.");

        api.MapGet("/members", GetMembersAsync)
            .WithTags("Workspace")
            .WithSummary("Membros do workspace")
            .WithDescription("Ids aceitos em assigneeId ao criar ou atualizar uma tarefa.");

        api.MapGet("/tasks", ListTasksAsync)
            .WithTags("Tarefas")
            .WithSummary("Lista tarefas")
            .WithDescription("Paginada (pageSize até 100). updatedSince traz só o que mudou desde a data informada, para sincronizações incrementais.");

        api.MapGet("/tasks/{idOrCode}", GetTaskAsync)
            .WithTags("Tarefas")
            .WithSummary("Busca uma tarefa")
            .WithDescription("Pelo id ou pelo código mostrado na tela (TASK-12).");

        api.MapPost("/tasks", CreateTaskAsync)
            .WithTags("Tarefas")
            .WithSummary("Cria uma tarefa")
            .WithDescription("Informe externalReference com o id da tarefa no sistema de origem: uma segunda chamada com a mesma referência responde 409 com a tarefa já criada, em vez de duplicar.");

        api.MapPatch("/tasks/{idOrCode}", UpdateTaskAsync)
            .WithTags("Tarefas")
            .WithSummary("Atualiza uma tarefa")
            .WithDescription("Atualização parcial: campo ausente não muda. String vazia limpa description, assigneeId ou externalReference. Mudança de status respeita o fluxo de trabalho da lista.");

        return app;
    }

    private static async ValueTask<object?> RejectArchivedWorkspaceAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.User.HasClaim(ApiKeyClaims.WorkspaceArchived, "true"))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Workspace arquivado",
                detail: "Desarquive o workspace nas configurações para voltar a usar a API.");
        }

        return await next(context);
    }

    private static async Task<Results<Ok<WorkspaceResponse>, NotFound>> GetWorkspaceAsync(
        ClaimsPrincipal user, IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct)
    {
        var workspaceId = user.GetWorkspaceId();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var workspace = await db.Workspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => new WorkspaceResponse(w.Id, w.Name, w.Slug, w.IsArchived))
            .FirstOrDefaultAsync(ct);
        return workspace is null ? TypedResults.NotFound() : TypedResults.Ok(workspace);
    }

    private static async Task<Ok<IReadOnlyList<SpaceResponse>>> GetListsAsync(
        ClaimsPrincipal user, IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct)
    {
        var workspaceId = user.GetWorkspaceId();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var spaces = await db.Spaces.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Include(s => s.Lists).ThenInclude(l => l.Statuses)
            .ToListAsync(ct);

        IReadOnlyList<SpaceResponse> response = spaces
            .Select(s => new SpaceResponse(s.Id, s.Name, s.Lists
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
                .Select(l => new ListResponse(l.Id, l.Name, l.Statuses
                    .OrderBy(st => st.SortOrder)
                    .Select(ToResponse)
                    .ToList()))
                .ToList()))
            .ToList();
        return TypedResults.Ok(response);
    }

    private static async Task<Ok<IReadOnlyList<MemberResponse>>> GetMembersAsync(
        ClaimsPrincipal user, IDbContextFactory<AppDbContext> dbFactory, CancellationToken ct)
    {
        var workspaceId = user.GetWorkspaceId();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var members = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.User.DisplayName).ThenBy(m => m.User.Email)
            .Select(m => new { m.UserId, m.User.DisplayName, m.User.Email, m.Role })
            .ToListAsync(ct);

        IReadOnlyList<MemberResponse> response = members
            .Select(m => new MemberResponse(m.UserId, DisplayNameOf(m.DisplayName, m.Email), m.Email, m.Role))
            .ToList();
        return TypedResults.Ok(response);
    }

    private static async Task<Ok<TaskPageResponse>> ListTasksAsync(
        [AsParameters] TaskListQuery query, ClaimsPrincipal user, WorkItemService tasks, HttpContext http, CancellationToken ct)
    {
        var page = await tasks.ListAsync(user.GetWorkspaceId(),
            new WorkItemListFilter(query.ListId, query.StatusId, query.AssigneeId, query.UpdatedSince, query.Page ?? 1, query.PageSize ?? 50), ct);
        var baseUrl = BaseUrl(http);
        return TypedResults.Ok(new TaskPageResponse(
            page.Items.Select(i => ToResponse(i, baseUrl)).ToList(), page.Page, page.PageSize, page.Total));
    }

    private static async Task<Results<Ok<TaskResponse>, ProblemHttpResult>> GetTaskAsync(
        string idOrCode, ClaimsPrincipal user, WorkItemService tasks, HttpContext http, CancellationToken ct)
    {
        var item = await tasks.GetAsync(user.GetWorkspaceId(), idOrCode, ct);
        return item is null
            ? TaskNotFound(idOrCode)
            : TypedResults.Ok(ToResponse(item, BaseUrl(http)));
    }

    private static async Task<Results<Created<TaskResponse>, ValidationProblem, Conflict<TaskConflictResponse>>> CreateTaskAsync(
        CreateWorkItemRequest body, ClaimsPrincipal user, WorkItemService tasks, HttpContext http, CancellationToken ct)
    {
        var outcome = await tasks.CreateAsync(user.GetWorkspaceId(), body, ActorOf(user), ct);
        var baseUrl = BaseUrl(http);
        return outcome.Status switch
        {
            WorkItemOutcomeStatus.Ok => TypedResults.Created($"{RoutePrefix}/tasks/{outcome.Item!.Id}", ToResponse(outcome.Item, baseUrl)),
            WorkItemOutcomeStatus.Conflict => TypedResults.Conflict(new TaskConflictResponse(
                "Já existe uma tarefa com essa externalReference neste workspace.", ToResponse(outcome.Item!, baseUrl))),
            _ => TypedResults.ValidationProblem(outcome.Errors!),
        };
    }

    // ProblemHttpResult cobre o 404 (tarefa inexistente) e o 409 sem corpo de tarefa (alterada por
    // outra pessoa); o 409 com a tarefa existente tem o próprio tipo.
    private static async Task<Results<Ok<TaskResponse>, ValidationProblem, Conflict<TaskConflictResponse>, ProblemHttpResult>> UpdateTaskAsync(
        string idOrCode, UpdateWorkItemRequest body, ClaimsPrincipal user, WorkItemService tasks, HttpContext http, CancellationToken ct)
    {
        var outcome = await tasks.UpdateAsync(user.GetWorkspaceId(), idOrCode, body, ActorOf(user), ct);
        var baseUrl = BaseUrl(http);
        return outcome.Status switch
        {
            WorkItemOutcomeStatus.Ok => TypedResults.Ok(ToResponse(outcome.Item!, baseUrl)),
            WorkItemOutcomeStatus.NotFound => TaskNotFound(idOrCode),
            WorkItemOutcomeStatus.Conflict when outcome.Item is not null => TypedResults.Conflict(new TaskConflictResponse(
                "Já existe outra tarefa com essa externalReference neste workspace.", ToResponse(outcome.Item, baseUrl))),
            WorkItemOutcomeStatus.Conflict => TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Tarefa alterada por outra pessoa", detail: outcome.Errors![""][0]),
            _ => TypedResults.ValidationProblem(outcome.Errors!),
        };
    }

    // Tarefas criadas pela chave ficam registradas em nome de quem gerou a chave (quando essa conta
    // ainda existe), e a notificação de atribuição diz o nome da integração.
    private static WorkItemActor ActorOf(ClaimsPrincipal user) =>
        new(user.GetKeyCreatorUserId(), $"Integração \"{user.GetKeyName()}\"");

    private static ProblemHttpResult TaskNotFound(string idOrCode) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Tarefa não encontrada",
        detail: $"Nenhuma tarefa \"{idOrCode}\" neste workspace. Use o id ou o código (TASK-12).");

    private static string BaseUrl(HttpContext http)
    {
        var configured = http.RequestServices.GetRequiredService<IConfiguration>()["PUBLIC_BASE_URL"];
        return string.IsNullOrWhiteSpace(configured)
            ? $"{http.Request.Scheme}://{http.Request.Host}"
            : configured.TrimEnd('/');
    }

    private static string DisplayNameOf(string? displayName, string? email) =>
        string.IsNullOrWhiteSpace(displayName) ? (email ?? "") : displayName;

    private static StatusResponse ToResponse(TaskStatusDefinition status) =>
        new(status.Id, status.Name, status.Category, status.IsDefault);

    private static TaskResponse ToResponse(WorkItem item, string baseUrl) => new(
        item.Id,
        item.Code,
        item.Number,
        item.Title,
        item.Description,
        item.Type,
        item.Priority,
        ToResponse(item.Status),
        new TaskListRef(item.TaskListId, item.TaskList.Name),
        item.Assignee is null ? null : new PersonResponse(item.Assignee.Id, DisplayNameOf(item.Assignee.DisplayName, item.Assignee.Email), item.Assignee.Email),
        item.StartDateUtc,
        item.DueDateUtc,
        item.ExternalReference,
        item.CreatedAtUtc,
        item.UpdatedAtUtc,
        $"{baseUrl}/lists/{item.TaskListId}/list");
}
