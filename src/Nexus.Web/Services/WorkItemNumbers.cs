using Microsoft.EntityFrameworkCore;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

// Hands out the per-workspace task number (TASK-1, TASK-2…). The task code used to be the first
// six hex digits of the GUID, which repeated (issue #63); a counter on the workspace row cannot.
public static class WorkItemNumbers
{
    public static async Task<int> NextAsync(AppDbContext db, Guid workspaceId, CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
        {
            // One atomic statement: the row lock serializes concurrent creations in the same
            // workspace, so two tasks never read the same counter value. It commits on its own,
            // so a save that fails afterwards leaves a gap in the sequence, never a duplicate.
            var next = await db.Database.SqlQuery<int>(
                    $"""UPDATE "Workspaces" SET "LastWorkItemNumber" = "LastWorkItemNumber" + 1 WHERE "Id" = {workspaceId} RETURNING "LastWorkItemNumber" AS "Value" """)
                .ToListAsync(ct);
            return next.Count == 1 ? next[0] : throw new InvalidOperationException($"Workspace {workspaceId} not found.");
        }

        // In-memory provider (tests): no SQL and no concurrency. The caller's SaveChanges persists
        // the bumped counter together with the new task.
        var workspace = await db.Workspaces.FirstAsync(w => w.Id == workspaceId, ct);
        return ++workspace.LastWorkItemNumber;
    }
}
