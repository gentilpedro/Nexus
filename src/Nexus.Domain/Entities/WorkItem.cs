namespace Nexus.Domain.Entities;

public class WorkItem
{
    public Guid Id { get; set; }

    public Guid TaskListId { get; set; }
    public TaskList TaskList { get; set; } = null!;

    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public WorkItemType Type { get; set; }

    public Guid StatusId { get; set; }
    public TaskStatusDefinition Status { get; set; } = null!;

    public WorkItemPriority Priority { get; set; }

    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }

    public DateTime? DueDateUtc { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Extension point: enables future Epic grouping without a schema change.
    public Guid? ParentEpicId { get; set; }
    public WorkItem? ParentEpic { get; set; }
}
