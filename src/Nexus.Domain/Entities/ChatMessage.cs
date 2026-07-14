namespace Nexus.Domain.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string Content { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
