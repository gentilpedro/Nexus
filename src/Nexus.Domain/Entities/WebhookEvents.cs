namespace Nexus.Domain.Entities;

// Eventos que um webhook de saída pode assinar (#75). Persistido como inteiro (flags); não
// reordenar nem reaproveitar valores. "ping" não está aqui: todo webhook recebe o teste.
[Flags]
public enum WebhookEvents
{
    None = 0,
    TaskCreated = 1,
    TaskUpdated = 2,
    TaskStatusChanged = 4,
    TaskAssigneeChanged = 8,
    CommentCreated = 16,
    All = TaskCreated | TaskUpdated | TaskStatusChanged | TaskAssigneeChanged | CommentCreated,
}

public static class WebhookEventNames
{
    public const string Ping = "ping";
    public const string TaskCreated = "task.created";
    public const string TaskUpdated = "task.updated";
    public const string TaskStatusChanged = "task.status_changed";
    public const string TaskAssigneeChanged = "task.assignee_changed";
    public const string CommentCreated = "comment.created";

    // O nome que vai no cabeçalho X-Nexus-Event e no campo "type" do corpo.
    public static string Of(WebhookEvents single) => single switch
    {
        WebhookEvents.TaskCreated => TaskCreated,
        WebhookEvents.TaskUpdated => TaskUpdated,
        WebhookEvents.TaskStatusChanged => TaskStatusChanged,
        WebhookEvents.TaskAssigneeChanged => TaskAssigneeChanged,
        WebhookEvents.CommentCreated => CommentCreated,
        _ => throw new ArgumentOutOfRangeException(nameof(single), single, "Não é um evento único."),
    };
}
