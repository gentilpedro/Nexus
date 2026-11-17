using System.ComponentModel;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Api;
using Nexus.Web.Services;

namespace Nexus.Web.Mcp;

// Ferramentas do servidor MCP (#67): o que um assistente de IA (Claude, ChatGPT, Copilot) consegue
// fazer num workspace. Mesma chave de API, mesmas regras e mesmo serviço da API de integração:
// o assistente nunca faz nada que a API não deixaria. Cada chamada chega com a ClaimsPrincipal
// da chave; o workspace vem dela, nunca de um argumento do modelo.
[McpServerToolType]
public sealed class NexusTools(
    IDbContextFactory<AppDbContext> dbFactory,
    WorkItemService tasks,
    IConfiguration configuration)
{
    private string? BaseUrl => configuration["PUBLIC_BASE_URL"];

    [McpServerTool(Name = "get_workspace"), Description("Identifica o workspace ao qual a chave de API dá acesso: nome, slug e se está arquivado.")]
    public async Task<WorkspaceResponse> GetWorkspace(ClaimsPrincipal user, CancellationToken ct)
    {
        var workspaceId = WorkspaceOf(user);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var workspace = await db.Workspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => new WorkspaceResponse(w.Id, w.Name, w.Slug, w.IsArchived))
            .FirstOrDefaultAsync(ct);
        return workspace ?? throw new McpException("Workspace da chave não encontrado.");
    }

    [McpServerTool(Name = "list_lists"), Description("Lista os spaces, as listas de tarefas e os status de cada lista. Use para descobrir o id da lista (taskListId) e dos status antes de criar ou mover tarefas.")]
    public async Task<IReadOnlyList<SpaceResponse>> ListLists(ClaimsPrincipal user, CancellationToken ct)
    {
        var workspaceId = WorkspaceOf(user);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var spaces = await db.Spaces.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Include(s => s.Lists).ThenInclude(l => l.Statuses)
            .ToListAsync(ct);
        return spaces
            .Select(s => new SpaceResponse(s.Id, s.Name, s.Lists
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
                .Select(l => new ListResponse(l.Id, l.Name, l.Statuses.OrderBy(st => st.SortOrder).Select(ApiMapping.ToResponse).ToList()))
                .ToList()))
            .ToList();
    }

    [McpServerTool(Name = "list_members"), Description("Lista os membros do workspace, com id, nome, e-mail e papel. O id é o que vai em assigneeId.")]
    public async Task<IReadOnlyList<MemberResponse>> ListMembers(ClaimsPrincipal user, CancellationToken ct)
    {
        var workspaceId = WorkspaceOf(user);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var members = await db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.User.DisplayName).ThenBy(m => m.User.Email)
            .Select(m => new { m.UserId, m.User.DisplayName, m.User.Email, m.Role })
            .ToListAsync(ct);
        return members.Select(m => new MemberResponse(m.UserId, ApiMapping.DisplayNameOf(m.DisplayName, m.Email), m.Email, m.Role)).ToList();
    }

    [McpServerTool(Name = "list_tasks"), Description("Lista tarefas do workspace, paginadas (até 100 por página), com filtros opcionais por lista, status, responsável e data da última alteração (updatedSince, ISO 8601).")]
    public async Task<TaskPageResponse> ListTasks(
        ClaimsPrincipal user,
        [Description("Id da lista; omita para todas as listas.")] Guid? taskListId = null,
        [Description("Id do status; omita para todos.")] Guid? statusId = null,
        [Description("Id do responsável; omita para todos.")] string? assigneeId = null,
        [Description("Só tarefas criadas ou alteradas a partir deste instante (ISO 8601).")] DateTime? updatedSince = null,
        [Description("Página, a partir de 1.")] int page = 1,
        [Description("Itens por página, até 100.")] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await tasks.ListAsync(WorkspaceOf(user), new WorkItemListFilter(taskListId, statusId, assigneeId, updatedSince, page, pageSize), ct);
        return new TaskPageResponse(result.Items.Select(i => ApiMapping.ToResponse(i, BaseUrl)).ToList(), result.Page, result.PageSize, result.Total);
    }

    [McpServerTool(Name = "get_task"), Description("Busca uma tarefa pelo id ou pelo código mostrado na tela (TASK-12).")]
    public async Task<TaskResponse> GetTask(
        ClaimsPrincipal user,
        [Description("Id (GUID) ou código, como TASK-12.")] string idOrCode,
        CancellationToken ct)
    {
        var item = await tasks.GetAsync(WorkspaceOf(user), idOrCode, ct);
        return item is null
            ? throw new McpException($"Nenhuma tarefa \"{idOrCode}\" neste workspace.")
            : ApiMapping.ToResponse(item, BaseUrl);
    }

    [McpServerTool(Name = "create_task"), Description("Cria uma tarefa numa lista do workspace. Exige chave de leitura e escrita. Informe externalReference para a mesma chamada repetida não duplicar: se já existir, devolve a tarefa existente.")]
    public async Task<TaskResponse> CreateTask(
        ClaimsPrincipal user,
        [Description("Id da lista (veja list_lists).")] Guid taskListId,
        [Description("Título, até 500 caracteres.")] string title,
        [Description("Descrição, opcional.")] string? description = null,
        [Description("Task, Bug, Story ou Epic. Padrão: Task.")] WorkItemType? type = null,
        [Description("Lowest, Low, Medium, High ou Highest. Padrão: Medium.")] WorkItemPriority? priority = null,
        [Description("Id do status inicial; omita para o padrão da lista.")] Guid? statusId = null,
        [Description("Id do responsável (veja list_members).")] string? assigneeId = null,
        [Description("Data de início (ISO 8601).")] DateTime? startDateUtc = null,
        [Description("Prazo (ISO 8601).")] DateTime? dueDateUtc = null,
        [Description("Id da tarefa no sistema de origem, até 100 caracteres.")] string? externalReference = null,
        CancellationToken ct = default)
    {
        RequireWrite(user);
        var outcome = await tasks.CreateAsync(WorkspaceOf(user),
            new CreateWorkItemRequest(taskListId, title, description, type, priority, statusId, assigneeId, startDateUtc, dueDateUtc, externalReference),
            Actor(user), ct);
        return outcome.Status switch
        {
            WorkItemOutcomeStatus.Ok => ApiMapping.ToResponse(outcome.Item!, BaseUrl),
            WorkItemOutcomeStatus.Conflict => ApiMapping.ToResponse(outcome.Item!, BaseUrl),
            _ => throw new McpException(Errors(outcome)),
        };
    }

    [McpServerTool(Name = "update_task"), Description("Atualiza campos de uma tarefa (id ou código TASK-12). Exige chave de leitura e escrita. Só o que for informado muda; string vazia limpa description, assigneeId ou externalReference. Mudança de status respeita o fluxo da lista.")]
    public async Task<TaskResponse> UpdateTask(
        ClaimsPrincipal user,
        [Description("Id (GUID) ou código, como TASK-12.")] string idOrCode,
        [Description("Novo título.")] string? title = null,
        [Description("Nova descrição; vazio limpa.")] string? description = null,
        [Description("Task, Bug, Story ou Epic.")] WorkItemType? type = null,
        [Description("Lowest, Low, Medium, High ou Highest.")] WorkItemPriority? priority = null,
        [Description("Id do novo status (da mesma lista).")] Guid? statusId = null,
        [Description("Id do novo responsável; vazio tira o responsável.")] string? assigneeId = null,
        [Description("Data de início (ISO 8601).")] DateTime? startDateUtc = null,
        [Description("Prazo (ISO 8601).")] DateTime? dueDateUtc = null,
        [Description("Referência externa; vazio limpa.")] string? externalReference = null,
        CancellationToken ct = default)
    {
        RequireWrite(user);
        var outcome = await tasks.UpdateAsync(WorkspaceOf(user), idOrCode,
            new UpdateWorkItemRequest(title, description, type, priority, statusId, assigneeId, startDateUtc, dueDateUtc, externalReference),
            Actor(user), ct);
        return outcome.Status switch
        {
            WorkItemOutcomeStatus.Ok => ApiMapping.ToResponse(outcome.Item!, BaseUrl),
            WorkItemOutcomeStatus.NotFound => throw new McpException($"Nenhuma tarefa \"{idOrCode}\" neste workspace."),
            WorkItemOutcomeStatus.Conflict when outcome.Item is not null => throw new McpException($"Já existe outra tarefa com essa externalReference: {outcome.Item.Code}."),
            _ => throw new McpException(Errors(outcome)),
        };
    }

    // As mesmas recusas dos endpoints de /api/v1 (plano sem API, workspace arquivado), como erro
    // de ferramenta legível para o modelo, em vez de 403.
    private static Guid WorkspaceOf(ClaimsPrincipal user)
    {
        if (!user.HasApiAccess())
        {
            throw new McpException("A API de integração não faz parte do plano deste workspace. Disponível nos planos Pro e Business.");
        }

        if (user.HasClaim(ApiKeyClaims.WorkspaceArchived, "true"))
        {
            throw new McpException("Este workspace está arquivado. Desarquive nas configurações para voltar a usar a API.");
        }

        return user.GetWorkspaceId();
    }

    private static void RequireWrite(ClaimsPrincipal user)
    {
        if (!user.CanWrite())
        {
            throw new McpException("Esta chave é só de leitura. Gere uma chave de leitura e escrita em Configurações do workspace, Integrações.");
        }
    }

    private static WorkItemActor Actor(ClaimsPrincipal user) =>
        new(user.GetKeyCreatorUserId(), $"Assistente via \"{user.GetKeyName()}\"");

    private static string Errors(WorkItemOutcome outcome) =>
        outcome.Errors is null
            ? "A tarefa não pôde ser gravada."
            : string.Join(" ", outcome.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}"));
}
