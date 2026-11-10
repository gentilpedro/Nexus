namespace Nexus.Domain.Entities;

// For Direct and Group conversations this row is what grants access. For the Workspace
// conversation access comes from WorkspaceMember instead, and a row here only exists to hold
// LastReadAtUtc — it is created the first time the person opens that conversation.
public class ChatConversationMember
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;

    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    public DateTime JoinedAtUtc { get; set; }

    // Null means never opened: every message in the conversation counts as unread.
    public DateTime? LastReadAtUtc { get; set; }
}
