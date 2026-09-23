using Nexus.Domain.Entities;
using Nexus.Web.Services.Backplane;

namespace Nexus.Web.Services;

/// <summary>
/// Singleton (not Scoped like NavigationContextService) — every user's chat page
/// subscribes to the same event, so a message posted by one circuit reaches every other
/// connected circuit without a page reload. No separate SignalR hub needed since Blazor
/// Server already keeps a persistent circuit per user; this just fans a C# event out to
/// whichever WorkspaceChat.razor instances are currently alive.
/// </summary>
/// <remarks>
/// <para>
/// A C# event only reaches circuits held by <em>this</em> process. With more than one instance
/// behind a load balancer, two users in the same workspace can be connected to different
/// servers, and neither would see the other's messages until a page reload. So the local event
/// is paired with an <see cref="IChatMessagePublisher"/> that tells the other instances, which
/// raise their own local event in turn (see <see cref="RedisChatSubscriber"/>).
/// </para>
/// <para>
/// The local event is raised first and unconditionally. Cross-instance delivery is best-effort
/// on top of it: a Redis outage degrades the app to single-instance behaviour instead of
/// breaking chat for everyone, and a single-instance deployment gets the no-op publisher and the
/// exact behaviour this class had before.
/// </para>
/// </remarks>
public class WorkspaceChatBroadcaster(IChatMessagePublisher publisher)
{
    public event Action<ChatMessage>? MessagePosted;

    /// <summary>
    /// Delivers a just-posted message to the circuits on this instance, then announces it to the
    /// other instances.
    /// </summary>
    /// <remarks>
    /// The message must already be persisted: the other instances receive only its id and load
    /// it from the database. Calling this before <c>SaveChangesAsync</c> would have them look up
    /// a row that does not exist yet.
    /// </remarks>
    public async Task BroadcastAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        MessagePosted?.Invoke(message);
        await publisher.PublishAsync(message.WorkspaceId, message.Id, cancellationToken);
    }

    /// <summary>
    /// Raises the local event for a message that was posted on another instance and arrived over
    /// the backplane.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="BroadcastAsync"/> so that a message coming off the backplane is
    /// never published back onto it — that would be an endless round trip between instances.
    /// </remarks>
    public void RaiseFromRemote(ChatMessage message) => MessagePosted?.Invoke(message);
}
