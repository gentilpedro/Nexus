namespace Nexus.Domain.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }

    // Denormalized from the conversation: the attachment download endpoint and the doc-page
    // cleanup only need the workspace, and both predate conversations.
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public Guid ConversationId { get; set; }
    public ChatConversation Conversation { get; set; } = null!;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string Content { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }

    public Guid? ReferencedDocPageId { get; set; }
    public DocPage? ReferencedDocPage { get; set; }

    public ICollection<ChatMessageAttachment> Attachments { get; set; } = new List<ChatMessageAttachment>();
    public ICollection<ChatMessageMention> Mentions { get; set; } = new List<ChatMessageMention>();
}
