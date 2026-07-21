namespace Nexus.Domain.Entities;

public class CustomFieldDefinition
{
    public Guid Id { get; set; }

    public Guid TaskListId { get; set; }
    public TaskList TaskList { get; set; } = null!;

    public string Name { get; set; } = "";
    public CustomFieldType Type { get; set; }
    public bool Required { get; set; }
    public int SortOrder { get; set; }

    public ICollection<CustomFieldOption> Options { get; set; } = new List<CustomFieldOption>();
}
