namespace Nexus.Domain.Entities;

// What each plan allows, in one place, so pages and services ask "can this workspace do X?"
// instead of comparing plan names themselves. Null limits mean unlimited.
public sealed record PlanCapabilities(
    WorkspacePlan Plan,
    int? MaxMembers,
    int? MaxOwnedWorkspaces,
    bool IncludesSprintsGanttAndBurndown,
    bool IncludesPrioritySupport)
{
    public const int StarterMaxMembers = 5;
    public const int StarterMaxOwnedWorkspaces = 1;

    public static PlanCapabilities For(WorkspacePlan plan) => plan switch
    {
        WorkspacePlan.Starter => new(plan, StarterMaxMembers, StarterMaxOwnedWorkspaces,
            IncludesSprintsGanttAndBurndown: false, IncludesPrioritySupport: false),
        WorkspacePlan.Pro or WorkspacePlan.Business => new(plan, MaxMembers: null, MaxOwnedWorkspaces: null,
            IncludesSprintsGanttAndBurndown: true, IncludesPrioritySupport: true),
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null)
    };

    public bool AllowsMembers(int memberCount) => MaxMembers is not { } max || memberCount <= max;
}
