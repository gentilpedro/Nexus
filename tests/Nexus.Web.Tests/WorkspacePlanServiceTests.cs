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
