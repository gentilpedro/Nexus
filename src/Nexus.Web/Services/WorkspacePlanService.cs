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

    /// <summary>
    /// Whether the Starter limits (members per workspace, workspaces per owner) are applied. Off
    /// during the launch period, while the public page promises Nexus is free and unlimited; turn
    /// it on together with the paid plans.
    /// </summary>
    public bool EnforceLimits { get; set; }
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
    private readonly bool enforceLimits = options?.Value.EnforceLimits ?? false;

    // Compared against Identity's NormalizedEmail, which is upper-cased invariant.
    private readonly HashSet<string> unlimitedAccounts = (options?.Value.UnlimitedAccounts ?? [])
        .Where(e => !string.IsNullOrWhiteSpace(e))
        .Select(e => e.Trim().ToUpperInvariant())
        .ToHashSet();

    public async Task<WorkspacePlanInfo> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await GetAsync(db, workspaceId, cancellationToken);
    }

    /// <summary>
    /// Same as <see cref="GetAsync(Guid, CancellationToken)"/>, on the caller's context — so a
    /// write path can read the plan inside its own transaction.
    /// </summary>
    public async Task<WorkspacePlanInfo> GetAsync(AppDbContext db, Guid workspaceId, CancellationToken cancellationToken = default)
    {
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
        return await IsUnlimitedUserAsync(db, userId, cancellationToken);
    }

    /// <summary>
    /// Whether one more person fits in the workspace's plan. With <paramref name="countPendingInvites"/>,
    /// invites still waiting for an answer take a seat too — otherwise a Starter admin could send
    /// twenty invites and the plan would only notice when they were accepted.
    /// </summary>
    /// <remarks>
    /// Callers that add a member must hold <see cref="LockWorkspaceAsync"/> first, in the same
    /// transaction, or two concurrent accepts can both see four members and both add a fifth.
    /// </remarks>
    public async Task<bool> CanAddMemberAsync(AppDbContext db, Guid workspaceId, bool countPendingInvites, CancellationToken cancellationToken = default)
    {
        if (!enforceLimits)
        {
            return true;
        }

        var plan = await GetAsync(db, workspaceId, cancellationToken);
        if (plan.Capabilities.MaxMembers is not { } max)
        {
            return true;
        }

        var seats = await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId, cancellationToken);
        if (countPendingInvites)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            seats += await db.WorkspaceInvites.CountAsync(
                i => i.WorkspaceId == workspaceId && i.AcceptedAtUtc == null && i.ExpiresAtUtc > now, cancellationToken);
        }

        return seats < max;
    }

    /// <summary>
    /// Whether the user may create one more workspace. A new workspace starts on Starter, and an
    /// account may own <see cref="PlanCapabilities.StarterMaxOwnedWorkspaces"/> active Starter
    /// workspace; paid and archived ones do not count.
    /// </summary>
    public async Task<bool> CanCreateWorkspaceAsync(AppDbContext db, string userId, CancellationToken cancellationToken = default)
    {
        if (!enforceLimits || await IsUnlimitedUserAsync(db, userId, cancellationToken))
        {
            return true;
        }

        var owned = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.Role == WorkspaceRole.Owner && !m.Workspace.IsArchived)
            .Select(m => m.Workspace.Subscription)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var ownedStarter = owned.Count(s => (s?.EffectivePlan(now) ?? WorkspacePlan.Starter) == WorkspacePlan.Starter);

        return ownedStarter < PlanCapabilities.StarterMaxOwnedWorkspaces;
    }

    /// <summary>
    /// Takes a row lock on the workspace for the rest of the caller's transaction, serializing
    /// everything that changes its member count. A no-op on providers without row locks (the
    /// in-memory database the unit tests use).
    /// </summary>
    public static async Task LockWorkspaceAsync(AppDbContext db, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Workspaces" WHERE "Id" = {workspaceId} FOR UPDATE""", cancellationToken);
        }
    }

    private Task<bool> IsUnlimitedUserAsync(AppDbContext db, string userId, CancellationToken cancellationToken) =>
        unlimitedAccounts.Count == 0
            ? Task.FromResult(false)
            : db.Users.AnyAsync(u => u.Id == userId && unlimitedAccounts.Contains(u.NormalizedEmail!), cancellationToken);
}
