using Nexus.Domain.Entities;

namespace Nexus.Web.Services;

/// <summary>
/// Singleton (not Scoped like NavigationContextService) — every user's chat page
/// subscribes to the same event, so a message posted by one circuit reaches every other
/// connected circuit without a page reload. No separate SignalR hub needed since Blazor
/// Server already keeps a persistent circuit per user; this just fans a C# event out to
/// whichever WorkspaceChat.razor instances are currently alive.
/// </summary>
public class WorkspaceChatBroadcaster
{
    public event Action<ChatMessage>? MessagePosted;

    public void Broadcast(ChatMessage message) => MessagePosted?.Invoke(message);
}
