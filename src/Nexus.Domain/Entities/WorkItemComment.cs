namespace Nexus.Domain.Entities;

public class WorkItemComment
{
    public Guid Id { get; set; }

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string Content { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
