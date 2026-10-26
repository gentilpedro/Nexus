using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers <see cref="WorkspacePlanService"/> (#49) against the real <c>AppDbContext</c> model.
/// </summary>
public class WorkspacePlanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WorkspaceWithoutSubscription_IsTreatedAsStarter()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await AddWorkspaceAsync(factory, subscription: null);

        var info = await new WorkspacePlanService(factory, new FakeTimeProvider(Now))
            .GetAsync(workspaceId, TestContext.Current.CancellationToken);

        Assert.Equal(WorkspacePlan.Starter, info.EffectivePlan);
        Assert.Equal(PlanCapabilities.StarterMaxMembers, info.Capabilities.MaxMembers);
        Assert.Null(info.Subscription);
    }

    [Fact]
    public async Task ActiveProSubscription_UnlocksProCapabilities()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await AddWorkspaceAsync(factory, id => new WorkspaceSubscription
        {
            WorkspaceId = id,
            Plan = WorkspacePlan.Pro,
            Status = SubscriptionStatus.Active,
            Seats = 8,
            CurrentPeriodEndUtc = Now.UtcDateTime.AddDays(20)
        });

        var info = await new WorkspacePlanService(factory, new FakeTimeProvider(Now))
            .GetAsync(workspaceId, TestContext.Current.CancellationToken);

        Assert.Equal(WorkspacePlan.Pro, info.EffectivePlan);
        Assert.True(info.Capabilities.IncludesSprintsGanttAndBurndown);
        Assert.Equal(8, info.Subscription!.Seats);
    }

    [Fact]
    public async Task CanceledPro_FallsBackToStarter_WhenTheClockPassesThePeriodEnd()
    {
        var factory = TestDb.CreateFactory();
        var periodEnd = Now.UtcDateTime.AddDays(2);
        var workspaceId = await AddWorkspaceAsync(factory, id => new WorkspaceSubscription
        {
            WorkspaceId = id,
            Plan = WorkspacePlan.Pro,
            Status = SubscriptionStatus.Canceled,
            CurrentPeriodEndUtc = periodEnd
        });
        var clock = new FakeTimeProvider(Now);
        var service = new WorkspacePlanService(factory, clock);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(WorkspacePlan.Pro, (await service.GetAsync(workspaceId, ct)).EffectivePlan);

        clock.Advance(TimeSpan.FromDays(3));

        Assert.Equal(WorkspacePlan.Starter, (await service.GetAsync(workspaceId, ct)).EffectivePlan);
    }

    [Fact]
    public async Task OneWorkspacesPlan_DoesNotLeakIntoAnother()
    {
        var factory = TestDb.CreateFactory();
        var pro = await AddWorkspaceAsync(factory, id => new WorkspaceSubscription
        {
            WorkspaceId = id,
            Plan = WorkspacePlan.Pro,
            Status = SubscriptionStatus.Active
        });
        var starter = await AddWorkspaceAsync(factory, id => WorkspaceSubscription.StartStarter(id, Now.UtcDateTime));
        var service = new WorkspacePlanService(factory, new FakeTimeProvider(Now));
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(WorkspacePlan.Pro, (await service.GetAsync(pro, ct)).EffectivePlan);
        Assert.Equal(WorkspacePlan.Starter, (await service.GetAsync(starter, ct)).EffectivePlan);
    }

    [Theory]
    [InlineData("dono@nexus.dev")]
    [InlineData("  DONO@Nexus.dev ")]
    public async Task WorkspaceOwnedByAnUnlimitedAccount_GetsEverything_RegardlessOfSubscription(string configured)
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await AddWorkspaceAsync(factory, id => WorkspaceSubscription.StartStarter(id, Now.UtcDateTime));
        await AddMemberAsync(factory, workspaceId, "dono@nexus.dev", WorkspaceRole.Owner);

        var info = await Service(factory, configured).GetAsync(workspaceId, TestContext.Current.CancellationToken);

        Assert.True(info.OwnedByUnlimitedAccount);
        Assert.Equal(WorkspacePlan.Business, info.EffectivePlan);
        Assert.Null(info.Capabilities.MaxMembers);
        Assert.True(info.Capabilities.IncludesSprintsGanttAndBurndown);
    }

    // Being a plain member of someone else's workspace must not upgrade that workspace — the
    // exemption follows what the account owns, not every place it has been invited to.
    [Fact]
    public async Task UnlimitedAccountAsPlainMember_DoesNotUpgradeTheWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await AddWorkspaceAsync(factory, id => WorkspaceSubscription.StartStarter(id, Now.UtcDateTime));
        await AddMemberAsync(factory, workspaceId, "outro@nexus.dev", WorkspaceRole.Owner);
        await AddMemberAsync(factory, workspaceId, "dono@nexus.dev", WorkspaceRole.Member);

        var info = await Service(factory, "dono@nexus.dev").GetAsync(workspaceId, TestContext.Current.CancellationToken);

        Assert.False(info.OwnedByUnlimitedAccount);
        Assert.Equal(WorkspacePlan.Starter, info.EffectivePlan);
    }

    [Fact]
    public async Task UserHasUnlimitedAccount_OnlyForConfiguredLogins()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await AddWorkspaceAsync(factory, subscription: null);
        var unlimited = await AddMemberAsync(factory, workspaceId, "dono@nexus.dev", WorkspaceRole.Owner);
        var regular = await AddMemberAsync(factory, workspaceId, "outro@nexus.dev", WorkspaceRole.Admin);
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await Service(factory, "dono@nexus.dev").UserHasUnlimitedAccountAsync(unlimited, ct));
        Assert.False(await Service(factory, "dono@nexus.dev").UserHasUnlimitedAccountAsync(regular, ct));
        Assert.False(await new WorkspacePlanService(factory).UserHasUnlimitedAccountAsync(unlimited, ct));
    }

    private static WorkspacePlanService Service(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> factory, string unlimitedEmail) =>
        new(factory, new FakeTimeProvider(Now),
            Microsoft.Extensions.Options.Options.Create(new PlanOptions { UnlimitedAccounts = [unlimitedEmail] }));

    private static async Task<string> AddMemberAsync(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> factory,
        Guid workspaceId, string email, WorkspaceRole role)
    {
        var userId = Guid.NewGuid().ToString();
        await using var db = factory.CreateDbContext();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            DisplayName = email,
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return userId;
    }

    private static async Task<Guid> AddWorkspaceAsync(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> factory,
        Func<Guid, WorkspaceSubscription>? subscription)
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace
        {
            Id = id,
            Name = "Agência Norte",
            Slug = $"agencia-{id:N}",
            Subscription = subscription?.Invoke(id)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }
}
