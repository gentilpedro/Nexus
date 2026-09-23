using System.Diagnostics.CodeAnalysis;

namespace Nexus.Web.Services.Backplane;

/// <summary>
/// What travels between instances when a chat message is posted: who sent it and which message
/// it was — never the message content itself.
/// </summary>
/// <remarks>
/// <para>
/// Only identifiers cross the wire. <see cref="Nexus.Domain.Entities.ChatMessage"/> is an EF
/// entity with navigation properties (author, attachments, mentions, referenced doc page) that
/// the chat page needs loaded; serializing that graph would mean either shipping a partially
/// populated object — which renders as a message with no author — or keeping a DTO in sync with
/// every future change to the entity. The receiving instance reloads from the database instead,
/// with exactly the includes the page uses. The database is shared anyway, so it is the one
/// source of truth both instances already agree on.
/// </para>
/// <para>
/// <see cref="InstanceId"/> exists to suppress the echo: Redis delivers a published message to
/// every subscriber of the channel, the publisher included. Without discarding our own, the
/// sender's browser would render the message twice — once from the local event raised at send
/// time, once when it came back around.
/// </para>
/// </remarks>
/// <param name="InstanceId">Identifies the process that published this message.</param>
/// <param name="WorkspaceId">Workspace the message belongs to, so subscribers can filter early.</param>
/// <param name="MessageId">Primary key of the persisted chat message.</param>
public readonly record struct ChatBackplaneEnvelope(Guid InstanceId, Guid WorkspaceId, Guid MessageId)
{
    private const char Separator = '|';

    /// <summary>Wire format: three GUIDs separated by <c>|</c>.</summary>
    /// <remarks>
    /// A fixed three-field layout rather than JSON: the payload is closed by design (only ever
    /// identifiers — see the type remarks), so a serializer would add a dependency and a parsing
    /// surface without buying any flexibility. It also keeps the value small enough that the
    /// whole envelope fits comfortably in a single Redis pub/sub frame.
    /// </remarks>
    public override string ToString() =>
        $"{InstanceId:N}{Separator}{WorkspaceId:N}{Separator}{MessageId:N}";

    /// <summary>
    /// Parses the wire format, rejecting anything malformed.
    /// </summary>
    /// <remarks>
    /// Returns false instead of throwing because the input is whatever happened to be published
    /// on the channel. A different application sharing the Redis instance, or a rolling deploy
    /// where an older version still speaks a previous format, must not be able to bring down the
    /// subscriber loop — the frame is dropped and the next one is processed normally.
    /// </remarks>
    public static bool TryParse(string? value, [NotNullWhen(true)] out ChatBackplaneEnvelope envelope)
    {
        envelope = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(Separator);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!Guid.TryParse(parts[0], out var instanceId)
            || !Guid.TryParse(parts[1], out var workspaceId)
            || !Guid.TryParse(parts[2], out var messageId))
        {
            return false;
        }

        envelope = new ChatBackplaneEnvelope(instanceId, workspaceId, messageId);
        return true;
    }
}
