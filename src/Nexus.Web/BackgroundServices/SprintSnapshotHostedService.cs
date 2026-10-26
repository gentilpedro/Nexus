using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Nexus.Web.Services;

namespace Nexus.Web.BackgroundServices;

/// <summary>
/// Writes a daily burndown snapshot for every Active sprint. Runs a pass immediately on
/// startup (so restarts/deploys don't lose a day) and then re-checks hourly; each write is
/// idempotent per (SprintId, SnapshotDateUtc), so more frequent checks are harmless.
/// </summary>
public class SprintSnapshotHostedService(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<SprintSnapshotHostedService> logger,
    WorkspacePlanService? plans = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TakeSnapshotsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TakeSnapshotsAsync(stoppingToken);
        }
    }

    internal async Task TakeSnapshotsAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var today = DateTime.UtcNow.Date;

            var activeSprints = await db.Sprints
                .Where(s => s.Status == SprintStatus.Active)
                .Select(s => new { s.Id, s.TaskList.Space.WorkspaceId })
                .ToListAsync(ct);

            // Burndown is a Pro feature (#53): no new snapshots for a workspace without it. The
            // ones already taken stay, so the chart comes back intact after an upgrade.
            var allowed = new Dictionary<Guid, bool>();
            foreach (var workspaceId in activeSprints.Select(s => s.WorkspaceId).Distinct())
            {
                allowed[workspaceId] = plans is null || await plans.HasProFeaturesAsync(workspaceId, ct);
            }

            foreach (var sprintId in activeSprints.Where(s => allowed[s.WorkspaceId]).Select(s => s.Id))
            {
                var totalCount = await db.WorkItems.CountAsync(w => w.SprintId == sprintId, ct);
                var remainingCount = await db.WorkItems.CountAsync(
                    w => w.SprintId == sprintId && w.Status.Category != StatusCategory.Done, ct);

                var existing = await db.SprintBurndownSnapshots
                    .FirstOrDefaultAsync(sn => sn.SprintId == sprintId && sn.SnapshotDateUtc == today, ct);

                if (existing is null)
                {
                    db.SprintBurndownSnapshots.Add(new SprintBurndownSnapshot
                    {
                        Id = Guid.NewGuid(),
                        SprintId = sprintId,
                        SnapshotDateUtc = today,
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

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missed snapshot pass shouldn't crash the app — the next hourly tick retries.
            logger.LogError(ex, "Failed to write sprint burndown snapshots.");
        }
    }
}
