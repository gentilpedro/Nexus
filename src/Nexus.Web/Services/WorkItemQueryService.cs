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
    //
    // listId is the TaskListId the caller already authorized the current user against (the
    // page resolves and checks it in OnParametersSetAsync). Both workItemId and newStatusId
    // are scoped to it below — this is the only thing standing between a legitimate drag on
    // your own board and a crafted DotNet.invokeMethodAsync call moving a WorkItem that
    // belongs to a workspace you're not even a member of.
    public async Task<bool> UpdateWorkItemStatusAsync(Guid listId, Guid workItemId, Guid newStatusId, int newSortOrder, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var workItem = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId && w.TaskListId == listId, ct);
        if (workItem is null)
        {
            return false;
        }

        var statusBelongsToList = await db.TaskStatusDefinitions.AnyAsync(s => s.Id == newStatusId && s.TaskListId == listId, ct);
        if (!statusBelongsToList)
        {
            return false;
        }

        if (workItem.StatusId != newStatusId
            && !await IsStatusTransitionAllowedAsync(db, workItem.TaskListId, workItem.StatusId, newStatusId, ct))
        {
            return false;
        }

        workItem.StatusId = newStatusId;
        workItem.SortOrder = newSortOrder;
        workItem.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else changed this WorkItem between our read and this save — treat it the
            // same as any other "not allowed" outcome; the caller reloads and shows a message.
            return false;
        }

        return true;
    }

    // Single source of truth for the workflow-rule check, shared by the drag-and-drop path
    // above and TaskDetailPanel.razor's edit-panel save — the two used to compute "does this
    // list restrict transitions?" differently (list-wide vs. per-current-status), which let a
    // status with zero outgoing rules skip validation entirely through the edit panel even
    // when the list had rules configured for its other statuses.
    public async Task<bool> IsStatusTransitionAllowedAsync(AppDbContext db, Guid taskListId, Guid fromStatusId, Guid toStatusId, CancellationToken ct = default)
    {
        if (fromStatusId == toStatusId)
        {
            return true;
        }

        var hasRules = await db.StatusTransitions.AnyAsync(t => t.FromStatus.TaskListId == taskListId, ct);
        if (!hasRules)
        {
            return true;
        }

        return await db.StatusTransitions.AnyAsync(t => t.FromStatusId == fromStatusId && t.ToStatusId == toStatusId, ct);
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
            .Select(m => new ValueTuple<string, string>(m.UserId, m.User.DisplayName != "" ? m.User.DisplayName : (m.User.Email ?? "")))
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
            .Include(s => s.WorkItems).ThenInclude(w => w.WorkItemLabels).ThenInclude(l => l.Label)
            .Include(s => s.WorkItems).ThenInclude(w => w.Comments)
            .OrderBy(s => s.StartDateUtc)
            .ToListAsync(ct);
    }

    public async Task<List<Sprint>> GetCompletedSprintsForListAsync(Guid taskListId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Sprints
            .Where(s => s.TaskListId == taskListId && s.Status == SprintStatus.Completed)
            .Include(s => s.WorkItems)
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
            .Include(w => w.WorkItemLabels).ThenInclude(l => l.Label)
            .Include(w => w.Comments)
            .OrderBy(w => w.SortOrder)
            .ToListAsync(ct);
    }

    // listId-scoped for the same IDOR reason as UpdateWorkItemStatusAsync above — both the
    // dragged item and the target sprint must belong to the list the caller already
    // authorized the user against.
    public async Task<bool> UpdateWorkItemSprintAsync(Guid listId, Guid workItemId, Guid? sprintId, int newSortOrder, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var workItem = await db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId && w.TaskListId == listId, ct);
        if (workItem is null)
        {
            return false;
        }

        if (sprintId is not null)
        {
            var sprintBelongsToList = await db.Sprints.AnyAsync(s => s.Id == sprintId && s.TaskListId == listId, ct);
            if (!sprintBelongsToList)
            {
                return false;
            }
        }

        workItem.SprintId = sprintId;
        workItem.SortOrder = newSortOrder;
        workItem.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        return true;
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
    /// sprint in the same list is already Active, the sprint doesn't belong to listId
    /// (IDOR guard — see UpdateWorkItemStatusAsync), or a concurrent StartSprintAsync call
    /// won the race (see the unique filtered index on Sprints in SprintConfiguration —
    /// the AnyAsync check below is only a fast pre-check, not the actual guarantee).
    /// </summary>
    public async Task<bool> StartSprintAsync(Guid listId, Guid sprintId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == sprintId && s.TaskListId == listId, ct);
        if (sprint is null)
        {
            return false;
        }

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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with another StartSprintAsync call for the same list — the unique
            // filtered index on Sprints (SprintConfiguration) rejected our update between our
            // AnyAsync check and this SaveChangesAsync. Re-verify before assuming that's what
            // happened (same pattern as WorkspaceInviteService's double-accept race): a real
            // infra failure (dropped connection, timeout) shouldn't be silently reinterpreted
            // as "sprint already active".
            var anotherSprintWon = await db.Sprints.AnyAsync(
                s => s.TaskListId == sprint.TaskListId && s.Status == SprintStatus.Active && s.Id != sprintId, ct);
            if (!anotherSprintWon)
            {
                throw;
            }
            return false;
        }

        return true;
    }

    // Returns any not-Done items to the backlog (SprintId = null) instead of leaving them
    // stranded in a Completed sprint forever — GetSprintsForListAsync/GetBacklogItemsAsync both
    // stop returning a Completed sprint's items, so without this they'd disappear from every
    // sprint-planning surface with no way to reassign them. SortOrder is recomputed relative to
    // the backlog's current max so returned items don't collide with existing backlog ordering.
    public async Task CompleteSprintAsync(Guid listId, Guid sprintId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var maxBacklogSortOrder = await db.WorkItems
            .Where(w => w.TaskListId == listId && w.SprintId == null)
            .Select(w => (int?)w.SortOrder)
            .MaxAsync(ct) ?? -1;

        var unfinished = await db.WorkItems
            .Where(w => w.SprintId == sprintId && w.Status.Category != StatusCategory.Done)
            .OrderBy(w => w.SortOrder)
            .ToListAsync(ct);

        for (var i = 0; i < unfinished.Count; i++)
        {
            unfinished[i].SprintId = null;
            unfinished[i].SortOrder = maxBacklogSortOrder + 1 + i;
        }

        await db.Sprints.Where(s => s.Id == sprintId && s.TaskListId == listId && s.Status == SprintStatus.Active).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, SprintStatus.Completed)
            .SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow), ct);

        await db.SaveChangesAsync(ct);
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
