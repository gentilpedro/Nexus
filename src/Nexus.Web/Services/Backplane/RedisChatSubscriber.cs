using Microsoft.EntityFrameworkCore;
using Nexus.Infrastructure.Data;
using StackExchange.Redis;

namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Listens for chat messages posted on other instances and hands them to this instance's
/// <see cref="WorkspaceChatBroadcaster"/>, so circuits connected here see them live.
/// </summary>
/// <remarks>
/// The receiving half of the backplane. Runs as a hosted service because the subscription has to
/// outlive any request or circuit: an instance must keep receiving messages for workspaces whose
/// users are connected to it, regardless of what it is otherwise doing.
/// </remarks>
public sealed class RedisChatSubscriber(
    IConnectionMultiplexer redis,
    NexusInstance instance,
    WorkspaceChatBroadcaster broadcaster,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<RedisChatSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();

        // SubscribeAsync's queue overload, not the callback overload: the queue processes frames
        // one at a time in arrival order. With the callback form, two messages posted in quick
        // succession run their handlers concurrently on the thread pool, and the second one can
        // finish its database load first — so the chat would occasionally render messages out of
        // order on remote instances only, which is the kind of bug that never reproduces locally.
        var queue = await subscriber.SubscribeAsync(RedisChatMessagePublisher.Channel);

        queue.OnMessage(async channelMessage =>
        {
            try
            {
                await HandleAsync(channelMessage.Message, stoppingToken);
            }
            catch (Exception ex)
            {
                // One bad frame must not tear down the subscription: OnMessage stops delivering
                // to this queue if the handler throws, and the instance would silently stop
                // receiving every future message while looking perfectly healthy.
                logger.LogError(ex, "Failed to handle a chat backplane message.");
            }
        });

        logger.LogInformation(
            "Chat backplane subscribed on {Channel} as instance {InstanceId}.",
            RedisChatMessagePublisher.Channel,
            instance.Id);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            await subscriber.UnsubscribeAsync(RedisChatMessagePublisher.Channel);
        }
    }

    private async Task HandleAsync(RedisValue payload, CancellationToken cancellationToken)
    {
        if (!ChatBackplaneEnvelope.TryParse(payload, out var envelope))
        {
            logger.LogWarning("Discarded an unparseable chat backplane frame.");
            return;
        }

        // Our own publish, echoed back by Redis to every subscriber including us. The message was
        // already raised locally the moment it was sent; raising it again would duplicate it on
        // the sender's screen.
        if (envelope.InstanceId == instance.Id)
        {
            return;
        }

        // Reloaded with exactly the includes WorkspaceChat.razor renders — see the remarks on
        // ChatBackplaneEnvelope for why the entity is not sent over the wire.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var message = await db.ChatMessages
            .Include(m => m.User)
            .Include(m => m.ReferencedDocPage)
            .Include(m => m.Attachments)
            .Include(m => m.Mentions).ThenInclude(x => x.User)
            .FirstOrDefaultAsync(m => m.Id == envelope.MessageId, cancellationToken);

        if (message is null)
        {
            // Deleted between publish and receive, or published by an instance pointing at a
            // different database. Either way there is nothing to render.
            logger.LogWarning(
                "Chat message {MessageId} arrived on the backplane but no longer exists.",
                envelope.MessageId);
            return;
        }

        broadcaster.RaiseFromRemote(message);
    }
}
