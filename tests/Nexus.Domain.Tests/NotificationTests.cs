using Nexus.Domain.Entities;

namespace Nexus.Domain.Tests;

/// <summary>
/// Notification.Message is mapped with HasMaxLength(MaxMessageLength) and is composed from
/// user-controlled values (workspace names, task titles) that can each already be that long on
/// their own. If TruncateMessage ever stops enforcing the limit, the failure is a database write
/// exception in the middle of sending a chat message — i.e. a user-visible outage triggered by
/// nothing more than a long workspace name.
/// </summary>
public class NotificationTests
{
    [Fact]
    public void ShortMessage_IsUnchanged()
    {
        const string message = "Alguém mencionou você no chat.";

        Assert.Equal(message, Notification.TruncateMessage(message));
    }

    [Fact]
    public void MessageExactlyAtTheLimit_IsUnchanged()
    {
        var message = new string('a', Notification.MaxMessageLength);

        Assert.Equal(message, Notification.TruncateMessage(message));
    }

    [Fact]
    public void OverlongMessage_IsTruncatedToFitTheColumn()
    {
        var message = new string('a', Notification.MaxMessageLength + 500);

        var truncated = Notification.TruncateMessage(message);

        Assert.Equal(Notification.MaxMessageLength, truncated.Length);
        Assert.EndsWith("…", truncated, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyMessage_IsHandled()
    {
        Assert.Equal("", Notification.TruncateMessage(""));
    }

    /// <summary>
    /// The realistic trigger: a workspace name at the entity's own maximum length composed into
    /// the notification template.
    /// </summary>
    [Fact]
    public void ComposedMessageFromMaxLengthInputs_StillFits()
    {
        var workspaceName = new string('W', 200);
        var senderName = new string('S', 200);
        var composed = Notification.TruncateMessage(
            $"{senderName} enviou uma mensagem no chat de \"{workspaceName}\".");

        Assert.True(composed.Length <= Notification.MaxMessageLength);
    }
}
