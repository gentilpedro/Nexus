using Nexus.Domain.Entities;

namespace Nexus.Web.Api;

// Respostas JSON da API de integração (#66) e corpo dos webhooks de saída (#75). Separadas das
// entidades para o contrato com quem integra ficar estável: mudar uma coluna no banco não muda o
// que o ERP recebe. A mesma TaskResponse vai na resposta da API e dentro do evento do webhook.
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

public static class ApiMapping
{
    public static string DisplayNameOf(string? displayName, string? email) =>
        string.IsNullOrWhiteSpace(displayName) ? (email ?? "") : displayName;

    public static StatusResponse ToResponse(TaskStatusDefinition status) =>
        new(status.Id, status.Name, status.Category, status.IsDefault);

    public static PersonResponse ToResponse(ApplicationUser user) =>
        new(user.Id, DisplayNameOf(user.DisplayName, user.Email), user.Email);

    // Exige Status, Assignee e TaskList carregados.
    public static TaskResponse ToResponse(WorkItem item, string? baseUrl) => new(
        item.Id,
        item.Code,
        item.Number,
        item.Title,
        item.Description,
        item.Type,
        item.Priority,
        ToResponse(item.Status),
        new TaskListRef(item.TaskListId, item.TaskList.Name),
        item.Assignee is null ? null : ToResponse(item.Assignee),
        item.StartDateUtc,
        item.DueDateUtc,
        item.ExternalReference,
        item.CreatedAtUtc,
        item.UpdatedAtUtc,
        TaskUrl(baseUrl, item.TaskListId));

    public static string? TaskUrl(string? baseUrl, Guid taskListId) =>
        string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/lists/{taskListId}/list";
}
