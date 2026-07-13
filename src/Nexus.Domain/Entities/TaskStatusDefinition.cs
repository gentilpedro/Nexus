namespace Nexus.Domain.Entities;

public class TaskStatusDefinition
{
    public Guid Id { get; set; }

    public Guid TaskListId { get; set; }
    public TaskList TaskList { get; set; } = null!;

    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public StatusCategory Category { get; set; }
    public bool IsDefault { get; set; }
}
