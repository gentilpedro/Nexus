namespace Nexus.Domain.Entities;

public class Notification
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    public NotificationType Type { get; set; }

    // Precomputed at creation time (e.g. "Pedro atribuiu 'Corrigir bug' a você.") — avoids
    // needing a display-time template/i18n layer for something this simple.
    public string Message { get; set; } = "";

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    // Set for ChatMessage notifications so Notifications.razor can link back to the chat —
    // chat messages have no WorkItem to hang the link off of.
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
