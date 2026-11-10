namespace Nexus.Domain.Entities;

public class ChatConversation
{
    public const int MaxNameLength = 80;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public ChatConversationType Type { get; set; }

    // Only groups have a name of their own. The workspace conversation is shown under the
    // workspace's name, a direct conversation under the other person's.
    public string? Name { get; set; }

    // Sorted pair of the two participants' ids, so the "do these two already have a direct
    // conversation?" check is a unique-index lookup instead of a two-level membership query, and
    // two people opening one with each other at the same moment can't end up with two.
    public string? DirectKey { get; set; }

    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<ChatConversationMember> Members { get; set; } = new List<ChatConversationMember>();

    public static string BuildDirectKey(string userIdA, string userIdB) =>
        string.CompareOrdinal(userIdA, userIdB) <= 0 ? $"{userIdA}:{userIdB}" : $"{userIdB}:{userIdA}";
}
