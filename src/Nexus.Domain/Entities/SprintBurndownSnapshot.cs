namespace Nexus.Domain.Entities;

public class SprintBurndownSnapshot
{
    public Guid Id { get; set; }

    public Guid SprintId { get; set; }
    public Sprint Sprint { get; set; } = null!;

    public DateTime SnapshotDateUtc { get; set; }
    public int TotalCount { get; set; }
    public int RemainingCount { get; set; }
}
