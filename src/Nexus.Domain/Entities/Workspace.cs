namespace Nexus.Domain.Entities;

public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Color { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<WorkspaceMember> Members { get; set; } = new List<WorkspaceMember>();
    public ICollection<Space> Spaces { get; set; } = new List<Space>();
}
