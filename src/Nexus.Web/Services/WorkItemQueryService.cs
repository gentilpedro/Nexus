using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Web.Services;

// Shared by ListView and BoardView so both pages always render the same
// underlying data — the only thing that differs between them is presentation.
// Uses IDbContextFactory (not an injected AppDbContext) so each call gets a
// short-lived context, rather than accumulating tracked entities for the
// lifetime of the Blazor Server circuit.
public class WorkItemQueryService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<Guid?> GetWorkspaceIdForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TaskLists
            .Where(l => l.Id == taskListId)
            .Select(l => (Guid?)l.Space.WorkspaceId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<WorkItem>> GetWorkItemsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkItems
            .Where(w => w.TaskListId == taskListId)
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .Include(w => w.WorkItemLabels).ThenInclude(l => l.Label)
            .OrderBy(w => w.SortOrder)
            .ToListAsync(ct);
    }

    public async Task<List<Label>> GetLabelsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Labels
            .Where(l => l.TaskListId == taskListId)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);
    }

    public async Task<List<TaskStatusDefinition>> GetStatusesForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TaskStatusDefinitions
            .Where(s => s.TaskListId == taskListId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);
    }

    // Returns false (without changing anything) if the list has configured StatusTransitions
    // and the from -> to change isn't one of them. A list with zero configured transitions is
    // unrestricted — every change is allowed, matching the app's behavior before this feature.
    public async Task<bool> UpdateWorkItemStatusAsync(Guid workItemId, Guid newStatusId, int newSortOrder, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var workItem = await db.WorkItems.FirstAsync(w => w.Id == workItemId, ct);

        if (workItem.StatusId != newStatusId)
        {
            var hasRules = await db.StatusTransitions.AnyAsync(t => t.FromStatus.TaskListId == workItem.TaskListId, ct);
            if (hasRules)
            {
                var allowed = await db.StatusTransitions.AnyAsync(
                    t => t.FromStatusId == workItem.StatusId && t.ToStatusId == newStatusId, ct);
                if (!allowed)
                {
                    return false;
                }
            }
        }

        workItem.StatusId = newStatusId;
        workItem.SortOrder = newSortOrder;
        workItem.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<StatusTransition>> GetStatusTransitionsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.StatusTransitions
            .Where(t => t.FromStatus.TaskListId == taskListId)
            .Include(t => t.FromStatus)
            .Include(t => t.ToStatus)
            .ToListAsync(ct);
    }

    public async Task<List<WorkItem>> GetAssignedWorkItemsAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkItems
            .Where(w => w.AssigneeId == userId && w.Status.Category != StatusCategory.Done)
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .Include(w => w.TaskList).ThenInclude(l => l.Space).ThenInclude(s => s.Workspace)
            .OrderBy(w => w.DueDateUtc == null)
            .ThenBy(w => w.DueDateUtc)
            .ThenByDescending(w => w.Priority)
            .ToListAsync(ct);
    }

    public async Task<List<WorkItem>> GetRecentlyCompletedByUserAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkItems
            .Where(w => w.AssigneeId == userId && w.Status.Category == StatusCategory.Done)
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .Include(w => w.TaskList).ThenInclude(l => l.Space).ThenInclude(s => s.Workspace)
            .OrderByDescending(w => w.UpdatedAtUtc)
            .Take(30)
            .ToListAsync(ct);
    }

    public async Task<List<WorkItem>> GetDelegatedByUserAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkItems
            .Where(w => w.CreatedByUserId == userId && w.AssigneeId != null && w.AssigneeId != userId)
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .Include(w => w.TaskList).ThenInclude(l => l.Space).ThenInclude(s => s.Workspace)
            .OrderBy(w => w.DueDateUtc == null)
            .ThenBy(w => w.DueDateUtc)
            .ThenBy(w => w.Title)
            .ToListAsync(ct);
    }

    public async Task<List<CustomFieldDefinition>> GetCustomFieldDefinitionsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CustomFieldDefinitions
            .Where(f => f.TaskListId == taskListId)
            .Include(f => f.Options.OrderBy(o => o.SortOrder))
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);
    }

    public async Task<List<(string UserId, string DisplayName)>> GetWorkspaceMemberOptionsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId)
            .Select(m => new ValueTuple<string, string>(m.UserId, m.User.DisplayName != "" ? m.User.DisplayName : m.User.Email!))
            .ToListAsync(ct);
    }

    // --- Sprints / backlog ---

    public async Task<List<Sprint>> GetSprintsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Sprints
            .Where(s => s.TaskListId == taskListId && s.Status != SprintStatus.Completed)
            .Include(s => s.WorkItems).ThenInclude(w => w.Status)
            .Include(s => s.WorkItems).ThenInclude(w => w.Assignee)
            .OrderBy(s => s.StartDateUtc)
            .ToListAsync(ct);
    }

    public async Task<List<Sprint>> GetCompletedSprintsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Sprints
            .Where(s => s.TaskListId == taskListId && s.Status == SprintStatus.Completed)
            .OrderByDescending(s => s.CompletedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<WorkItem>> GetBacklogItemsAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkItems
            .Where(w => w.TaskListId == taskListId && w.SprintId == null)
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .OrderBy(w => w.SortOrder)
            .ToListAsync(ct);
    }

    public async Task UpdateWorkItemSprintAsync(Guid workItemId, Guid? sprintId, int newSortOrder, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var workItem = await db.WorkItems.FirstAsync(w => w.Id == workItemId, ct);
        workItem.SprintId = sprintId;
        workItem.SortOrder = newSortOrder;
        workItem.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<WorkItem>> GetEpicsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkItems
            .Where(w => w.TaskListId == taskListId && w.Type == WorkItemType.Epic)
            .OrderBy(w => w.Title)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Starts a sprint, enforcing at most one Active sprint per list, and writes the
    /// Day-0 burndown snapshot. Returns false (without changing anything) if another
    /// sprint in the same list is already Active.
    /// </summary>
    public async Task<bool> StartSprintAsync(Guid sprintId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sprint = await db.Sprints.FirstAsync(s => s.Id == sprintId, ct);

        var hasActiveSprint = await db.Sprints.AnyAsync(
            s => s.TaskListId == sprint.TaskListId && s.Status == SprintStatus.Active && s.Id != sprintId, ct);
        if (hasActiveSprint)
        {
            return false;
        }

        sprint.Status = SprintStatus.Active;
        sprint.StartedAtUtc = DateTime.UtcNow;

        var totalCount = await db.WorkItems.CountAsync(w => w.SprintId == sprintId, ct);
        var remainingCount = await db.WorkItems.CountAsync(
            w => w.SprintId == sprintId && w.Status.Category != StatusCategory.Done, ct);

        await UpsertSnapshotAsync(db, sprintId, DateTime.UtcNow.Date, totalCount, remainingCount, ct);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task CompleteSprintAsync(Guid sprintId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Sprints.Where(s => s.Id == sprintId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, SprintStatus.Completed)
            .SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow), ct);
    }

    private static async Task UpsertSnapshotAsync(AppDbContext db, Guid sprintId, DateTime snapshotDateUtc, int totalCount, int remainingCount, CancellationToken ct)
    {
        var existing = await db.SprintBurndownSnapshots
            .FirstOrDefaultAsync(sn => sn.SprintId == sprintId && sn.SnapshotDateUtc == snapshotDateUtc, ct);

        if (existing is null)
        {
            db.SprintBurndownSnapshots.Add(new SprintBurndownSnapshot
            {
                Id = Guid.NewGuid(),
                SprintId = sprintId,
                SnapshotDateUtc = snapshotDateUtc,
                TotalCount = totalCount,
                RemainingCount = remainingCount
            });
        }
        else
        {
            existing.TotalCount = totalCount;
            existing.RemainingCount = remainingCount;
        }
    }

    // Cross-workspace search. The membership check inline in the query (rather than a
    // separate authorization call) is what guarantees a task from a workspace the user
    // doesn't belong to can never leak into results, regardless of which filters are set.
    public async Task<List<WorkItem>> SearchWorkItemsAsync(string userId, WorkItemSearchFilters filters, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = db.WorkItems
            .Where(w => w.TaskList.Space.Workspace.Members.Any(m => m.UserId == userId));

        if (filters.WorkspaceId is not null)
        {
            query = query.Where(w => w.TaskList.Space.WorkspaceId == filters.WorkspaceId);
        }

        if (!string.IsNullOrWhiteSpace(filters.Text))
        {
            query = query.Where(w => w.Title.Contains(filters.Text));
        }

        if (filters.StatusCategory is not null)
        {
            query = query.Where(w => w.Status.Category == filters.StatusCategory);
        }

        if (filters.Priority is not null)
        {
            query = query.Where(w => w.Priority == filters.Priority);
        }

        if (filters.Type is not null)
        {
            query = query.Where(w => w.Type == filters.Type);
        }

        if (filters.AssigneeMode == AssigneeFilterMode.Me)
        {
            query = query.Where(w => w.AssigneeId == userId);
        }
        else if (filters.AssigneeMode == AssigneeFilterMode.Unassigned)
        {
            query = query.Where(w => w.AssigneeId == null);
        }

        var today = DateTime.UtcNow.Date;
        if (filters.DueMode == DueFilterMode.Overdue)
        {
            query = query.Where(w => w.DueDateUtc != null && w.DueDateUtc.Value.Date < today);
        }
        else if (filters.DueMode == DueFilterMode.ThisWeek)
        {
            var weekAhead = today.AddDays(7);
            query = query.Where(w => w.DueDateUtc != null && w.DueDateUtc.Value.Date >= today && w.DueDateUtc.Value.Date <= weekAhead);
        }
        else if (filters.DueMode == DueFilterMode.NoDueDate)
        {
            query = query.Where(w => w.DueDateUtc == null);
        }

        return await query
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .Include(w => w.TaskList).ThenInclude(l => l.Space).ThenInclude(s => s.Workspace)
            .OrderBy(w => w.DueDateUtc == null)
            .ThenBy(w => w.DueDateUtc)
            .ThenBy(w => w.Title)
            .Take(200)
            .ToListAsync(ct);
    }
}

public class WorkItemSearchFilters
{
    public Guid? WorkspaceId { get; set; }
    public string? Text { get; set; }
    public StatusCategory? StatusCategory { get; set; }
    public WorkItemPriority? Priority { get; set; }
    public WorkItemType? Type { get; set; }
    public AssigneeFilterMode AssigneeMode { get; set; } = AssigneeFilterMode.Any;
    public DueFilterMode DueMode { get; set; } = DueFilterMode.Any;
}

public enum AssigneeFilterMode { Any, Me, Unassigned }

public enum DueFilterMode { Any, Overdue, ThisWeek, NoDueDate }
