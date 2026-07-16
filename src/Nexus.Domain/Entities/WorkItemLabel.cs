namespace Nexus.Domain.Entities;

// Explicit many-to-many join between WorkItem and Label — same shape as CustomFieldValue
// (own Id, not a composite key), for consistency with the rest of the project.
public class WorkItemLabel
{
    public Guid Id { get; set; }

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public Guid LabelId { get; set; }
    public Label Label { get; set; } = null!;
}
