namespace Nexus.Domain.Entities;

public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Color { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<WorkspaceMember> Members { get; set; } = new List<WorkspaceMember>();
    public ICollection<Space> Spaces { get; set; } = new List<Space>();

    // Null only for workspaces created before plans existed and not yet backfilled; readers treat
    // a missing subscription as Starter.
    public WorkspaceSubscription? Subscription { get; set; }
}
