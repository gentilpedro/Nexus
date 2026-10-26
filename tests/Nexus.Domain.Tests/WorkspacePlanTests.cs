using Nexus.Domain.Entities;

namespace Nexus.Domain.Tests;

/// <summary>
/// Covers the plan model (#49): what each plan allows and which plan is in effect for a
/// subscription at a given moment. The limits and Pro-only features built on top of it (#52, #53)
/// all read these answers, so a regression here silently gives away — or takes away — a paid plan.
/// </summary>
public class WorkspacePlanTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    // Both enums are stored as integers; reordering them would reinterpret every existing row.
    [Fact]
    public void PlanAndStatusOrdinals_AreStable()
    {
        Assert.Equal([WorkspacePlan.Starter, WorkspacePlan.Pro, WorkspacePlan.Business], Enum.GetValues<WorkspacePlan>());
        Assert.Equal(0, (int)WorkspacePlan.Starter);
        Assert.Equal(
            [SubscriptionStatus.Active, SubscriptionStatus.PastDue, SubscriptionStatus.Canceled],
            Enum.GetValues<SubscriptionStatus>());
    }

    [Fact]
    public void Starter_IsLimitedToOneWorkspaceAndFiveMembers_WithoutProFeatures()
    {
        var starter = PlanCapabilities.For(WorkspacePlan.Starter);

        Assert.Equal(5, starter.MaxMembers);
        Assert.Equal(1, starter.MaxOwnedWorkspaces);
        Assert.False(starter.IncludesSprintsGanttAndBurndown);
        Assert.False(starter.IncludesPrioritySupport);
    }

    [Theory]
    [InlineData(WorkspacePlan.Pro)]
    [InlineData(WorkspacePlan.Business)]
    public void PaidPlans_AreUnlimited_AndIncludeProFeatures(WorkspacePlan plan)
    {
        var caps = PlanCapabilities.For(plan);

        Assert.Null(caps.MaxMembers);
        Assert.Null(caps.MaxOwnedWorkspaces);
        Assert.True(caps.IncludesSprintsGanttAndBurndown);
        Assert.True(caps.IncludesPrioritySupport);
    }

    [Theory]
    [InlineData(WorkspacePlan.Starter, 5, true)]
    [InlineData(WorkspacePlan.Starter, 6, false)]
    [InlineData(WorkspacePlan.Pro, 500, true)]
    public void AllowsMembers_RespectsThePlanLimit(WorkspacePlan plan, int members, bool allowed)
    {
        Assert.Equal(allowed, PlanCapabilities.For(plan).AllowsMembers(members));
    }

    [Fact]
    public void StartStarter_CreatesAnActiveStarterWithTheOwnerAsOnlySeat()
    {
        var workspaceId = Guid.NewGuid();

        var sub = WorkspaceSubscription.StartStarter(workspaceId, Now);

        Assert.Equal(workspaceId, sub.WorkspaceId);
        Assert.Equal(WorkspacePlan.Starter, sub.Plan);
        Assert.Equal(SubscriptionStatus.Active, sub.Status);
        Assert.Equal(1, sub.Seats);
        Assert.Null(sub.CurrentPeriodEndUtc);
        Assert.Equal(WorkspacePlan.Starter, sub.EffectivePlan(Now));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    public void ActiveOrPastDue_KeepThePaidPlan(SubscriptionStatus status)
    {
        var sub = Paid(WorkspacePlan.Pro, status, periodEnd: Now.AddDays(-3));

        Assert.Equal(WorkspacePlan.Pro, sub.EffectivePlan(Now));
    }

    [Fact]
    public void Canceled_KeepsThePaidPlanUntilThePeriodEnds()
    {
        var sub = Paid(WorkspacePlan.Pro, SubscriptionStatus.Canceled, periodEnd: Now.AddDays(10));

        Assert.Equal(WorkspacePlan.Pro, sub.EffectivePlan(Now));
        Assert.Equal(WorkspacePlan.Pro, sub.EffectivePlan(Now.AddDays(10).AddTicks(-1)));
    }

    [Fact]
    public void Canceled_FallsBackToStarterOnceThePeriodEnds()
    {
        var sub = Paid(WorkspacePlan.Business, SubscriptionStatus.Canceled, periodEnd: Now);

        Assert.Equal(WorkspacePlan.Starter, sub.EffectivePlan(Now));
    }

    [Fact]
    public void Canceled_WithoutAPaidPeriod_IsStarter()
    {
        var sub = Paid(WorkspacePlan.Pro, SubscriptionStatus.Canceled, periodEnd: null);

        Assert.Equal(WorkspacePlan.Starter, sub.EffectivePlan(Now));
    }

    private static WorkspaceSubscription Paid(WorkspacePlan plan, SubscriptionStatus status, DateTime? periodEnd) => new()
    {
        WorkspaceId = Guid.NewGuid(),
        Plan = plan,
        Status = status,
        Seats = 3,
        CurrentPeriodStartUtc = periodEnd?.AddMonths(-1),
        CurrentPeriodEndUtc = periodEnd
    };
}
