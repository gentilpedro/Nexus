namespace Nexus.Domain.Entities;

public class Sprint
{
    public Guid Id { get; set; }

    public Guid TaskListId { get; set; }
    public TaskList TaskList { get; set; } = null!;

    public string Name { get; set; } = "";
    public string? Goal { get; set; }
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public SprintStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();
    public ICollection<SprintBurndownSnapshot> Snapshots { get; set; } = new List<SprintBurndownSnapshot>();
}
