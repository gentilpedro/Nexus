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
            .OrderBy(w => w.SortOrder)
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

    public async Task UpdateWorkItemStatusAsync(Guid workItemId, Guid newStatusId, int newSortOrder, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var workItem = await db.WorkItems.FirstAsync(w => w.Id == workItemId, ct);
        workItem.StatusId = newStatusId;
        workItem.SortOrder = newSortOrder;
        workItem.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
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
}
