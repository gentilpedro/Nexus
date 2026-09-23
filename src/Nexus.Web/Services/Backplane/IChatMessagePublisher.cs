namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Carries a "a message was posted" signal to the other instances of the app.
/// </summary>
/// <remarks>
/// Separate from the subscriber half on purpose. <see cref="WorkspaceChatBroadcaster"/> needs to
/// publish, and the subscriber needs the broadcaster to hand messages back to — registering one
/// type for both halves would be a dependency cycle in the container.
/// </remarks>
public interface IChatMessagePublisher
{
    /// <summary>
    /// Announces a persisted chat message to the other instances.
    /// </summary>
    /// <remarks>
    /// Never throws: chat delivery within this instance already succeeded by the time this is
    /// called, and losing the cross-instance fan-out is strictly less bad than failing the send
    /// the user just made. Implementations log and swallow.
    /// </remarks>
    Task PublishAsync(Guid workspaceId, Guid messageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The single-instance deployment: there is nobody else to tell.
/// </summary>
/// <remarks>
/// Registered whenever no Redis connection string is configured, which keeps running without
/// Redis a first-class supported mode rather than a degraded one. The current production
/// deployment (single IIS site on shared hosting) uses exactly this.
/// </remarks>
public sealed class NullChatMessagePublisher : IChatMessagePublisher
{
    public Task PublishAsync(Guid workspaceId, Guid messageId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
