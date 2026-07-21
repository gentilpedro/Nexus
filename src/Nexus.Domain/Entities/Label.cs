namespace Nexus.Domain.Entities;

public class Label
{
    public Guid Id { get; set; }

    public Guid TaskListId { get; set; }
    public TaskList TaskList { get; set; } = null!;

    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Color { get; set; } = "";
    public int SortOrder { get; set; }
}
