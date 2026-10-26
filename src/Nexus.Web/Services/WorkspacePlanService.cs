using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

/// <summary>
/// Bound from the "Plans" configuration section.
/// </summary>
public sealed class PlanOptions
{
    /// <summary>
    /// Login e-mails of accounts that are never limited by a plan — the operator's own account in
    /// production. Workspaces they own behave as Business no matter what the subscription says.
    /// </summary>
    public List<string> UnlimitedAccounts { get; set; } = [];
}

/// <summary>
/// The plan currently in effect for a workspace, plus what that plan allows.
/// </summary>
/// <param name="Subscription">Null when the workspace has no subscription row yet.</param>
/// <param name="OwnedByUnlimitedAccount">True when the plan comes from <see cref="PlanOptions.UnlimitedAccounts"/>, not from billing.</param>
public sealed record WorkspacePlanInfo(
    WorkspacePlan EffectivePlan,
    PlanCapabilities Capabilities,
    WorkspaceSubscription? Subscription,
    bool OwnedByUnlimitedAccount = false);

/// <summary>
/// Single answer to "what can this workspace do?". Limits (#52) and Pro-only features (#53) ask
/// this service instead of reading the subscription and comparing plan names on their own, so
/// the rules live in <see cref="PlanCapabilities"/> and nowhere else.
/// </summary>
public class WorkspacePlanService(
    IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider? timeProvider = null,
    IOptions<PlanOptions>? options = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // Compared against Identity's NormalizedEmail, which is upper-cased invariant.
    private readonly HashSet<string> unlimitedAccounts = (options?.Value.UnlimitedAccounts ?? [])
        .Where(e => !string.IsNullOrWhiteSpace(e))
        .Select(e => e.Trim().ToUpperInvariant())
        .ToHashSet();

    public async Task<WorkspacePlanInfo> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var subscription = await db.WorkspaceSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, cancellationToken);

        if (unlimitedAccounts.Count > 0 && await db.WorkspaceMembers
                .AnyAsync(m => m.WorkspaceId == workspaceId
                               && m.Role == WorkspaceRole.Owner
                               && unlimitedAccounts.Contains(m.User.NormalizedEmail!), cancellationToken))
        {
            return new WorkspacePlanInfo(WorkspacePlan.Business, PlanCapabilities.For(WorkspacePlan.Business),
                subscription, OwnedByUnlimitedAccount: true);
        }

        // A workspace without a row is one the backfill has not reached (or a test fixture):
        // treating it as Starter fails closed — never grants a paid feature by accident.
        var plan = subscription?.EffectivePlan(clock.GetUtcNow().UtcDateTime) ?? WorkspacePlan.Starter;

        return new WorkspacePlanInfo(plan, PlanCapabilities.For(plan), subscription);
    }

    /// <summary>
    /// Whether the account is exempt from every plan limit — for checks that are about the user,
    /// not a single workspace, such as how many workspaces they may own (#52).
    /// </summary>
    public async Task<bool> UserHasUnlimitedAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (unlimitedAccounts.Count == 0)
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.Users.AnyAsync(u => u.Id == userId && unlimitedAccounts.Contains(u.NormalizedEmail!), cancellationToken);
    }
}
