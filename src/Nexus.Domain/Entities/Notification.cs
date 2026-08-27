namespace Nexus.Domain.Entities;

public class Notification
{
    public const int MaxMessageLength = 500;
    public const int MaxLinkUrlLength = 512;

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

    // Where clicking the notification should take the user, when that target isn't a WorkItem or
    // a workspace chat — currently workspace invites, which point at /convite/{token} so the
    // person can review and accept instead of being dropped into a workspace they haven't joined.
    // Always an app-relative path built by us, never user input.
    public string? LinkUrl { get; set; }

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    // Set for ChatMessage notifications so Notifications.razor can link back to the chat —
    // chat messages have no WorkItem to hang the link off of.
    public Guid? WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
