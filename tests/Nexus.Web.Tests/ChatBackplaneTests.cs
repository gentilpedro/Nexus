using Nexus.Domain.Entities;
using Nexus.Web.Services;
using Nexus.Web.Services.Backplane;

namespace Nexus.Web.Tests;

/// <summary>
/// Tests for the multi-instance chat fan-out: the envelope that crosses the wire and the
/// broadcaster's two entry points.
/// </summary>
/// <remarks>
/// Deliberately no Redis here. The pieces that talk to Redis are thin wrappers over the client,
/// and a test that needs a live server to run is a test that gets skipped in CI. What is worth
/// pinning is the logic that decides <em>what</em> is sent and <em>whether</em> it is sent again
/// — which is exactly what these cover.
/// </remarks>
public class ChatBackplaneTests
{
    /// <summary>Records what was published without touching a network.</summary>
    private sealed class RecordingPublisher : IChatMessagePublisher
    {
        public List<(Guid WorkspaceId, Guid MessageId)> Published { get; } = [];

        public Task PublishAsync(Guid workspaceId, Guid messageId, CancellationToken cancellationToken = default)
        {
            Published.Add((workspaceId, messageId));
            return Task.CompletedTask;
        }
    }

    private static ChatMessage AMessage() => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        Content = "olá",
    };

    [Fact]
    public void EnvelopeRoundTripsThroughTheWireFormat()
    {
        var original = new ChatBackplaneEnvelope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(ChatBackplaneEnvelope.TryParse(original.ToString(), out var parsed));
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid|also-not|nope")]
    [InlineData("8a1f…|missing-fields")]
    public void EnvelopeRejectsMalformedPayloadsInsteadOfThrowing(string? payload)
    {
        // A frame published by something else on the same Redis instance, or by an older version
        // during a rolling deploy, must be dropped quietly — never take the subscriber down.
        Assert.False(ChatBackplaneEnvelope.TryParse(payload, out _));
    }

    [Fact]
    public void EnvelopeRejectsAWrongFieldCountEvenWhenEveryFieldIsAGuid()
    {
        var twoGuids = $"{Guid.NewGuid():N}|{Guid.NewGuid():N}";

        Assert.False(ChatBackplaneEnvelope.TryParse(twoGuids, out _));
    }

    [Fact]
    public async Task BroadcastDeliversLocallyAndAnnouncesToOtherInstances()
    {
        var publisher = new RecordingPublisher();
        var broadcaster = new WorkspaceChatBroadcaster(publisher);
        var message = AMessage();

        ChatMessage? received = null;
        broadcaster.MessagePosted += m => received = m;

        await broadcaster.BroadcastAsync(message, TestContext.Current.CancellationToken);

        Assert.Same(message, received);
        Assert.Equal([(message.WorkspaceId, message.Id)], publisher.Published);
    }

    [Fact]
    public async Task BroadcastStillDeliversLocallyWhenThePublisherIsANoOp()
    {
        // The single-instance deployment: no Redis configured, so nothing is announced, but the
        // chat must behave exactly as it did before the backplane existed.
        var broadcaster = new WorkspaceChatBroadcaster(new NullChatMessagePublisher());
        var message = AMessage();

        ChatMessage? received = null;
        broadcaster.MessagePosted += m => received = m;

        await broadcaster.BroadcastAsync(message, TestContext.Current.CancellationToken);

        Assert.Same(message, received);
    }

    [Fact]
    public void RaiseFromRemoteDeliversLocallyWithoutPublishingBack()
    {
        // The loop guard. A message that arrived over the backplane must not be published onto
        // it again: instance A would hear B's echo, republish it, B would hear A's, and the two
        // would keep handing the same message back and forth forever.
        var publisher = new RecordingPublisher();
        var broadcaster = new WorkspaceChatBroadcaster(publisher);
        var message = AMessage();

        ChatMessage? received = null;
        broadcaster.MessagePosted += m => received = m;

        broadcaster.RaiseFromRemote(message);

        Assert.Same(message, received);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task BroadcastIsSafeWithNoSubscribers()
    {
        // An instance where nobody has the chat page open still has to publish, so that the
        // instances that do have readers deliver the message.
        var publisher = new RecordingPublisher();
        var broadcaster = new WorkspaceChatBroadcaster(publisher);

        await broadcaster.BroadcastAsync(AMessage(), TestContext.Current.CancellationToken);

        Assert.Single(publisher.Published);
    }
}
