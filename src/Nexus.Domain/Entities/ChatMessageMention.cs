namespace Nexus.Domain.Entities;

public class ChatMessageMention
{
    public Guid Id { get; set; }

    public Guid ChatMessageId { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;

    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
}
