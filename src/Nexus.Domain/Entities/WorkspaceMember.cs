namespace Nexus.Domain.Entities;

public class WorkspaceMember
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    public WorkspaceRole Role { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}
