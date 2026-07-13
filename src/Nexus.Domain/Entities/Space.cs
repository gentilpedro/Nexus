namespace Nexus.Domain.Entities;

public class Space
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }

    public ICollection<TaskList> Lists { get; set; } = new List<TaskList>();
}
