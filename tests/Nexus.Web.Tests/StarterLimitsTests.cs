using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the Starter limits (#52) at the <see cref="WorkspacePlanService"/> level: how many
/// workspaces an account may own and how many seats a workspace has, including the launch-period
/// switch and the operator's unlimited account.
/// </summary>
public class StarterLimitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static WorkspacePlanService Service(IDbContextFactory<AppDbContext> factory, bool enforce = true, string? unlimited = null) =>
        new(factory, new FakeTimeProvider(Now), Options.Create(new PlanOptions
        {
            EnforceLimits = enforce,
            UnlimitedAccounts = unlimited is null ? [] : [unlimited]
        }));

    [Fact]
    public async Task AccountWithOneActiveStarterWorkspace_CannotCreateAnother()
    {
        var factory = TestDb.CreateFactory();
        var user = await SeedUserAsync(factory, "dono@example.com");
        await SeedOwnedWorkspaceAsync(factory, user, WorkspacePlan.Starter);

        await using var db = factory.CreateDbContext();
        Assert.False(await Service(factory).CanCreateWorkspaceAsync(db, user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AccountWithoutWorkspaces_CanCreateItsFirst()
    {
        var factory = TestDb.CreateFactory();
        var user = await SeedUserAsync(factory, "novo@example.com");

        await using var db = factory.CreateDbContext();
        Assert.True(await Service(factory).CanCreateWorkspaceAsync(db, user, TestContext.Current.CancellationToken));
    }

    // Archiving is the way out for a Starter account that wants a fresh workspace, and paid
    // workspaces are not what the Starter allowance is about.
    [Theory]
    [InlineData(WorkspacePlan.Starter, true)]
    [InlineData(WorkspacePlan.Pro, false)]
    public async Task ArchivedOrPaidWorkspaces_DoNotUseTheStarterAllowance(WorkspacePlan plan, bool archived)
    {
        var factory = TestDb.CreateFactory();
        var user = await SeedUserAsync(factory, "dono@example.com");
        await SeedOwnedWorkspaceAsync(factory, user, plan, archived);

        await using var db = factory.CreateDbContext();
        Assert.True(await Service(factory).CanCreateWorkspaceAsync(db, user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnlimitedAccount_CreatesAsManyWorkspacesAsItWants()
    {
        var factory = TestDb.CreateFactory();
        var user = await SeedUserAsync(factory, "operador@example.com");
        await SeedOwnedWorkspaceAsync(factory, user, WorkspacePlan.Starter);
        await SeedOwnedWorkspaceAsync(factory, user, WorkspacePlan.Starter);

        await using var db = factory.CreateDbContext();
        Assert.True(await Service(factory, unlimited: "operador@example.com").CanCreateWorkspaceAsync(db, user, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LimitsOff_AllowEverything()
    {
        var factory = TestDb.CreateFactory();
        var user = await SeedUserAsync(factory, "dono@example.com");
        var workspaceId = await SeedOwnedWorkspaceAsync(factory, user, WorkspacePlan.Starter);
        await SeedMembersAsync(factory, workspaceId, 7);
        var service = Service(factory, enforce: false);
        var ct = TestContext.Current.CancellationToken;

        await using var db = factory.CreateDbContext();
        Assert.True(await service.CanCreateWorkspaceAsync(db, user, ct));
        Assert.True(await service.CanAddMemberAsync(db, workspaceId, countPendingInvites: true, ct));
    }

    [Fact]
    public async Task ExpiredInvites_DoNotHoldASeat()
    {
        var factory = TestDb.CreateFactory();
        var user = await SeedUserAsync(factory, "dono@example.com");
        var workspaceId = await SeedOwnedWorkspaceAsync(factory, user, WorkspacePlan.Starter);
        await SeedMembersAsync(factory, workspaceId, 3);
        await SeedInviteAsync(factory, workspaceId, expiresAt: Now.UtcDateTime.AddDays(3));
        var ct = TestContext.Current.CancellationToken;

        await using (var db = factory.CreateDbContext())
        {
            // Owner + 3 members + 1 pending = 5: full.
            Assert.False(await Service(factory).CanAddMemberAsync(db, workspaceId, countPendingInvites: true, ct));
            // Accepting counts members only: 4 of 5.
            Assert.True(await Service(factory).CanAddMemberAsync(db, workspaceId, countPendingInvites: false, ct));
        }

        await using (var db = factory.CreateDbContext())
        {
            var invite = await db.WorkspaceInvites.SingleAsync(ct);
            invite.ExpiresAtUtc = Now.UtcDateTime.AddMinutes(-1);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = factory.CreateDbContext())
        {
            Assert.True(await Service(factory).CanAddMemberAsync(db, workspaceId, countPendingInvites: true, ct));
        }
    }

    [Fact]
    public async Task WorkspaceOwnedByUnlimitedAccount_HasNoSeatLimit()
    {
        var factory = TestDb.CreateFactory();
        var user = await SeedUserAsync(factory, "operador@example.com");
        var workspaceId = await SeedOwnedWorkspaceAsync(factory, user, WorkspacePlan.Starter);
        await SeedMembersAsync(factory, workspaceId, 9);

        await using var db = factory.CreateDbContext();
        Assert.True(await Service(factory, unlimited: "operador@example.com")
            .CanAddMemberAsync(db, workspaceId, countPendingInvites: true, TestContext.Current.CancellationToken));
    }

    private static async Task<string> SeedUserAsync(IDbContextFactory<AppDbContext> factory, string email)
    {
        var id = Guid.NewGuid().ToString();
        await using var db = factory.CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = id, Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task<Guid> SeedOwnedWorkspaceAsync(IDbContextFactory<AppDbContext> factory, string ownerId, WorkspacePlan plan, bool archived = false)
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace
        {
            Id = id,
            Name = "W",
            Slug = $"w-{id:N}",
            IsArchived = archived,
            Subscription = new WorkspaceSubscription { WorkspaceId = id, Plan = plan, Status = SubscriptionStatus.Active }
        });
        db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = id, UserId = ownerId, Role = WorkspaceRole.Owner });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task SeedMembersAsync(IDbContextFactory<AppDbContext> factory, Guid workspaceId, int count)
    {
        await using var db = factory.CreateDbContext();
        for (var i = 0; i < count; i++)
        {
            db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = $"m{i}-{Guid.NewGuid():N}", Role = WorkspaceRole.Member });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SeedInviteAsync(IDbContextFactory<AppDbContext> factory, Guid workspaceId, DateTime expiresAt)
    {
        await using var db = factory.CreateDbContext();
        db.WorkspaceInvites.Add(new WorkspaceInvite
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Email = "pendente@example.com",
            Token = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = Now.UtcDateTime,
            ExpiresAtUtc = expiresAt
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
