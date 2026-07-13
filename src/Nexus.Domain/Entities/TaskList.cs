namespace Nexus.Domain.Entities;

public class TaskList
{
    public Guid Id { get; set; }

    public Guid SpaceId { get; set; }
    public Space Space { get; set; } = null!;

    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    public ICollection<TaskStatusDefinition> Statuses { get; set; } = new List<TaskStatusDefinition>();
    public ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();
}
