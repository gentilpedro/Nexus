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

    public async Task<List<(string UserId, string DisplayName)>> GetWorkspaceMemberOptionsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId)
            .Select(m => new ValueTuple<string, string>(m.UserId, m.User.DisplayName != "" ? m.User.DisplayName : m.User.Email!))
            .ToListAsync(ct);
    }
}
