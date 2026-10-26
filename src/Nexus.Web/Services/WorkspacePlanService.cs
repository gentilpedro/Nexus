using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

/// <summary>
/// The plan currently in effect for a workspace, plus what that plan allows.
/// </summary>
/// <param name="Subscription">Null when the workspace has no subscription row yet.</param>
public sealed record WorkspacePlanInfo(
    WorkspacePlan EffectivePlan,
    PlanCapabilities Capabilities,
    WorkspaceSubscription? Subscription);

/// <summary>
/// Single answer to "what can this workspace do?". Limits (#52) and Pro-only features (#53) ask
/// this service instead of reading the subscription and comparing plan names on their own, so
/// the rules live in <see cref="PlanCapabilities"/> and nowhere else.
/// </summary>
public class WorkspacePlanService(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<WorkspacePlanInfo> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var subscription = await db.WorkspaceSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, cancellationToken);

        // A workspace without a row is one the backfill has not reached (or a test fixture):
        // treating it as Starter fails closed — never grants a paid feature by accident.
        var plan = subscription?.EffectivePlan(clock.GetUtcNow().UtcDateTime) ?? WorkspacePlan.Starter;

        return new WorkspacePlanInfo(plan, PlanCapabilities.For(plan), subscription);
    }
}
