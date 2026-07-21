namespace Nexus.Web.Services;

/// <summary>
/// Scoped per-circuit (same pattern as NavigationContextService) — lets Notifications.razor
/// push a fresh unread count straight to MainLayout's topbar badge right after marking
/// notifications read, instead of leaving the badge stale until the next
/// NavigationManager.LocationChanged recomputes it.
/// </summary>
public class NotificationBadgeService
{
    public int UnreadCount { get; private set; }

    public event Action? Changed;

    public void SetUnreadCount(int count)
    {
        UnreadCount = count;
        Changed?.Invoke();
    }
}
