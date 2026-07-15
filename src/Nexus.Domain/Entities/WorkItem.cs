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

    public DateTime? StartDateUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Epic grouping: assigns this item under an Epic-typed WorkItem in the same list.
    public Guid? ParentEpicId { get; set; }
    public WorkItem? ParentEpic { get; set; }

    // Sprint assignment: null means the item sits in the list's backlog.
    public Guid? SprintId { get; set; }
    public Sprint? Sprint { get; set; }

    // Who created this item — set once at creation, never overwritten on edit.
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public ICollection<CustomFieldValue> CustomFieldValues { get; set; } = new List<CustomFieldValue>();
    public ICollection<WorkItemAttachment> Attachments { get; set; } = new List<WorkItemAttachment>();
}
