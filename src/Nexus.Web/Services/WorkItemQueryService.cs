using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Web.Services;

// Shared by ListView and BoardView so both pages always render the same
// underlying data — the only thing that differs between them is presentation.
public class WorkItemQueryService(AppDbContext db)
{
    public Task<Guid?> GetWorkspaceIdForListAsync(Guid taskListId, CancellationToken ct = default) =>
        db.TaskLists
            .Where(l => l.Id == taskListId)
            .Select(l => (Guid?)l.Space.WorkspaceId)
            .FirstOrDefaultAsync(ct);

    public Task<List<WorkItem>> GetWorkItemsForListAsync(Guid taskListId, CancellationToken ct = default) =>
        db.WorkItems
            .Where(w => w.TaskListId == taskListId)
            .Include(w => w.Status)
            .Include(w => w.Assignee)
            .OrderBy(w => w.SortOrder)
            .ToListAsync(ct);

    public Task<List<TaskStatusDefinition>> GetStatusesForListAsync(Guid taskListId, CancellationToken ct = default) =>
        db.TaskStatusDefinitions
            .Where(s => s.TaskListId == taskListId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

    public async Task UpdateWorkItemStatusAsync(Guid workItemId, Guid newStatusId, int newSortOrder, CancellationToken ct = default)
    {
        var workItem = await db.WorkItems.FirstAsync(w => w.Id == workItemId, ct);
        workItem.StatusId = newStatusId;
        workItem.SortOrder = newSortOrder;
        workItem.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<(string UserId, string DisplayName)>> GetWorkspaceMemberOptionsAsync(Guid workspaceId, CancellationToken ct = default) =>
        db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId)
            .Select(m => new ValueTuple<string, string>(m.UserId, m.User.DisplayName != "" ? m.User.DisplayName : m.User.Email!))
            .ToListAsync(ct);
}
