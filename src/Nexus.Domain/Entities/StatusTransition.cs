namespace Nexus.Domain.Entities;

// An allowed From -> To status change for a list. If a list has zero rows here, every
// transition is allowed (unrestricted default); once any row exists for a list, only the
// listed pairs are allowed. From/To always belong to the same TaskList.
public class StatusTransition
{
    public Guid Id { get; set; }

    public Guid FromStatusId { get; set; }
    public TaskStatusDefinition FromStatus { get; set; } = null!;

    public Guid ToStatusId { get; set; }
    public TaskStatusDefinition ToStatus { get; set; } = null!;
}
