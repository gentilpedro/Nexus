using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.BackgroundServices;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the Pro-only features (#53): sprints, Gantt and burndown. Pages ask
/// <see cref="WorkspacePlanService.HasProFeaturesAsync"/>; the sprint operations and the daily
/// burndown snapshot check it again on their own, so a Starter workspace cannot plan a sprint
/// even from a page that was opened before a downgrade.
/// </summary>
public class ProFeaturesTests
{
    private static WorkspacePlanService Plans(IDbContextFactory<AppDbContext> factory, bool enforce = true, string? unlimited = null) =>
        new(factory, options: Options.Create(new PlanOptions
        {
            EnforceLimits = enforce,
            UnlimitedAccounts = unlimited is null ? [] : [unlimited]
        }));

    [Theory]
    [InlineData(WorkspacePlan.Starter, true, false)]
    [InlineData(WorkspacePlan.Pro, true, true)]
    [InlineData(WorkspacePlan.Business, true, true)]
    [InlineData(WorkspacePlan.Starter, false, true)] // launch period: switch off, everything allowed
    public async Task HasProFeatures_FollowsThePlan_AndTheSwitch(WorkspacePlan plan, bool enforce, bool expected)
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedListAsync(factory, plan);

        Assert.Equal(expected, await Plans(factory, enforce).HasProFeaturesAsync(seed.WorkspaceId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StarterWorkspaceOwnedByUnlimitedAccount_HasProFeatures()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedListAsync(factory, WorkspacePlan.Starter, ownerEmail: "operador@example.com");

        Assert.True(await Plans(factory, unlimited: "operador@example.com").HasProFeaturesAsync(seed.WorkspaceId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Starter_CannotStartASprint_ButPro_Can()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = TestDb.CreateFactory();
        var starter = await SeedListAsync(factory, WorkspacePlan.Starter);
        var pro = await SeedListAsync(factory, WorkspacePlan.Pro);
        var queries = new WorkItemQueryService(factory, Plans(factory));

        Assert.False(await queries.StartSprintAsync(starter.ListId, starter.SprintId, ct));
        Assert.True(await queries.StartSprintAsync(pro.ListId, pro.SprintId, ct));

        await using var db = factory.CreateDbContext();
        Assert.Equal(SprintStatus.Planned, (await db.Sprints.SingleAsync(s => s.Id == starter.SprintId, ct)).Status);
    }

    // Moving work out of a sprint must keep working after a downgrade, or the items would be
    // stuck in a sprint the workspace can no longer see.
    [Fact]
    public async Task Starter_CannotPlanWorkIntoASprint_ButCanTakeItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = TestDb.CreateFactory();
        var seed = await SeedListAsync(factory, WorkspacePlan.Starter);
        var queries = new WorkItemQueryService(factory, Plans(factory));

        Assert.False(await queries.UpdateWorkItemSprintAsync(seed.ListId, seed.BacklogItemId, seed.SprintId, 0, ct));
        Assert.True(await queries.UpdateWorkItemSprintAsync(seed.ListId, seed.SprintItemId, null, 0, ct));
    }

    [Fact]
    public async Task BurndownSnapshot_IsOnlyTakenForWorkspacesWithProFeatures()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = TestDb.CreateFactory();
        var starter = await SeedListAsync(factory, WorkspacePlan.Starter, sprintStatus: SprintStatus.Active);
        var pro = await SeedListAsync(factory, WorkspacePlan.Pro, sprintStatus: SprintStatus.Active);

        await new SprintSnapshotHostedService(factory, NullLogger<SprintSnapshotHostedService>.Instance, Plans(factory))
            .TakeSnapshotsAsync(ct);

        await using var db = factory.CreateDbContext();
        Assert.False(await db.SprintBurndownSnapshots.AnyAsync(s => s.SprintId == starter.SprintId, ct));
        Assert.True(await db.SprintBurndownSnapshots.AnyAsync(s => s.SprintId == pro.SprintId, ct));
    }

    private sealed record Seed(Guid WorkspaceId, Guid ListId, Guid SprintId, Guid SprintItemId, Guid BacklogItemId);

    private static async Task<Seed> SeedListAsync(
        IDbContextFactory<AppDbContext> factory,
        WorkspacePlan plan,
        SprintStatus sprintStatus = SprintStatus.Planned,
        string? ownerEmail = null)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var spaceId = Guid.NewGuid();
        var statusId = Guid.NewGuid();

        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace
        {
            Id = seed.WorkspaceId,
            Name = "W",
            Slug = $"w-{seed.WorkspaceId:N}",
            Subscription = new WorkspaceSubscription { WorkspaceId = seed.WorkspaceId, Plan = plan, Status = SubscriptionStatus.Active }
        });

        if (ownerEmail is not null)
        {
            var userId = Guid.NewGuid().ToString();
            db.Users.Add(new ApplicationUser { Id = userId, Email = ownerEmail, UserName = ownerEmail, NormalizedEmail = ownerEmail.ToUpperInvariant(), DisplayName = ownerEmail });
            db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = seed.WorkspaceId, UserId = userId, Role = WorkspaceRole.Owner });
        }

        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = seed.WorkspaceId, Name = "S" });
        db.TaskLists.Add(new TaskList { Id = seed.ListId, SpaceId = spaceId, Name = "L" });
        db.TaskStatusDefinitions.Add(new TaskStatusDefinition { Id = statusId, TaskListId = seed.ListId, Name = "A fazer", Category = StatusCategory.ToDo });
        db.Sprints.Add(new Sprint { Id = seed.SprintId, TaskListId = seed.ListId, Name = "Sprint 1", Status = sprintStatus });
        db.WorkItems.Add(new WorkItem { Id = seed.SprintItemId, TaskListId = seed.ListId, StatusId = statusId, SprintId = seed.SprintId, Title = "Na sprint" });
        db.WorkItems.Add(new WorkItem { Id = seed.BacklogItemId, TaskListId = seed.ListId, StatusId = statusId, Title = "No backlog" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return seed;
    }
}
