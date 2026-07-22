namespace Nexus.Domain.Entities;

public class Notification
{
    public const int MaxMessageLength = 500;

    public Guid Id { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    public NotificationType Type { get; set; }

    // Precomputed at creation time (e.g. "Pedro atribuiu 'Corrigir bug' a você.") — avoids
    // needing a display-time template/i18n layer for something this simple.
    public string Message { get; set; } = "";

    // Message is built by interpolating user-controlled strings (task titles, display names)
    // that individually can already be as long as MaxMessageLength, so the composed string can
    // overflow the column — always run it through this before assigning Message.
    public static string TruncateMessage(string message) =>
        message.Length <= MaxMessageLength ? message : string.Concat(message.AsSpan(0, MaxMessageLength - 1), "…");

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    // Set for ChatMessage notifications so Notifications.razor can link back to the chat —
    // chat messages have no WorkItem to hang the link off of.
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
