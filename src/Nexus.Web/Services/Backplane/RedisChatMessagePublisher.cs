using StackExchange.Redis;

namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Publishes the "message posted" signal on a Redis pub/sub channel so the other instances can
/// deliver it to their own connected circuits.
/// </summary>
public sealed class RedisChatMessagePublisher(
    IConnectionMultiplexer redis,
    NexusInstance instance,
    ILogger<RedisChatMessagePublisher> logger) : IChatMessagePublisher
{
    /// <summary>Channel every instance publishes to and subscribes on.</summary>
    /// <remarks>
    /// One channel for all workspaces, with the filtering done by the receiver. Per-workspace
    /// channels would mean subscribing and unsubscribing as users navigate — churn on the Redis
    /// connection proportional to navigation, to save delivering a 108-byte frame that the
    /// receiver discards. The envelope carries the workspace id precisely so that discard is a
    /// comparison, not a database round trip.
    /// </remarks>
    public static readonly RedisChannel Channel =
        RedisChannel.Literal("nexus:chat:posted");

    public async Task PublishAsync(
        Guid workspaceId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var envelope = new ChatBackplaneEnvelope(instance.Id, workspaceId, messageId);

        try
        {
            await redis.GetSubscriber().PublishAsync(Channel, envelope.ToString());
        }
        catch (Exception ex)
        {
            // Deliberately swallowed. The message is already committed to the database and has
            // already been rendered for everyone on this instance; Redis being unreachable must
            // degrade the app to single-instance behaviour, not fail a send the user completed.
            // Users on other instances will see the message on their next page load.
            logger.LogWarning(
                ex,
                "Could not publish chat message {MessageId} to the backplane; other instances will not see it live.",
                messageId);
        }
    }
}
