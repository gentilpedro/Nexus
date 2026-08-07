using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Regression tests for P1 — list views materialized every work item in a list with no limit.
/// </summary>
/// <remarks>
/// Measured before the cap, on a list of 20,000 items: one request took 2.9s, emitted 10.3 MB of
/// HTML and grew the server process by ~416 MB; five concurrent requests took it from 537 MB to
/// 1.84 GB. Blazor Server retains that per circuit, so it is resident memory, not a spike. These
/// tests pin the ceiling and the truncation signal that keeps the UI honest about it.
/// </remarks>
public class WorkItemQueryCapTests
{
    private static async Task<(WorkItemQueryService Service, Guid ListId)> SeedAsync(int itemCount)
    {
        var factory = TestDb.CreateFactory();
        var listId = Guid.NewGuid();
        var statusId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.TaskStatusDefinitions.Add(new TaskStatusDefinition
            {
                Id = statusId,
                TaskListId = listId,
                Name = "To Do",
                Category = StatusCategory.ToDo,
                SortOrder = 0,
            });

            for (var i = 0; i < itemCount; i++)
            {
                db.WorkItems.Add(new WorkItem
                {
                    Id = Guid.NewGuid(),
                    TaskListId = listId,
                    StatusId = statusId,
                    Title = $"Item {i}",
                    SortOrder = i,
                });
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return (new WorkItemQueryService(factory), listId);
    }

    [Fact]
    public async Task ListBelowTheCap_IsReturnedWholeAndNotFlaggedTruncated()
    {
        var (service, listId) = await SeedAsync(50);

        var page = await service.GetWorkItemsForListAsync(listId, TestContext.Current.CancellationToken);

        Assert.Equal(50, page.Items.Count);
        Assert.False(page.IsTruncated);
    }

    /// <summary>
    /// The boundary matters: exactly the cap must not be reported as truncated, or every list of
    /// exactly 1000 items would show a misleading warning.
    /// </summary>
    [Fact]
    public async Task ListExactlyAtTheCap_IsNotFlaggedTruncated()
    {
        var (service, listId) = await SeedAsync(WorkItemQueryService.MaxItemsPerView);

        var page = await service.GetWorkItemsForListAsync(listId, TestContext.Current.CancellationToken);

        Assert.Equal(WorkItemQueryService.MaxItemsPerView, page.Items.Count);
        Assert.False(page.IsTruncated);
    }

    /// <summary>
    /// The actual fix: a list larger than the cap never materializes more than the cap, and the
    /// caller is told so.
    /// </summary>
    [Fact]
    public async Task ListAboveTheCap_IsClampedAndFlaggedTruncated()
    {
        var (service, listId) = await SeedAsync(WorkItemQueryService.MaxItemsPerView + 250);

        var page = await service.GetWorkItemsForListAsync(listId, TestContext.Current.CancellationToken);

        Assert.Equal(WorkItemQueryService.MaxItemsPerView, page.Items.Count);
        Assert.True(page.IsTruncated);
    }

    [Fact]
    public async Task EmptyList_IsHandled()
    {
        var (service, listId) = await SeedAsync(0);

        var page = await service.GetWorkItemsForListAsync(listId, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.False(page.IsTruncated);
    }

    /// <summary>
    /// Truncation must cut the tail, not an arbitrary slice: the items kept have to be the first
    /// ones by SortOrder, which is the order the views render.
    /// </summary>
    [Fact]
    public async Task Truncation_KeepsTheFirstItemsBySortOrder()
    {
        var (service, listId) = await SeedAsync(WorkItemQueryService.MaxItemsPerView + 10);

        var page = await service.GetWorkItemsForListAsync(listId, TestContext.Current.CancellationToken);

        Assert.Equal(0, page.Items.First().SortOrder);
        Assert.Equal(WorkItemQueryService.MaxItemsPerView - 1, page.Items.Last().SortOrder);
    }

    [Fact]
    public async Task Backlog_IsCappedTheSameWay()
    {
        var (service, listId) = await SeedAsync(WorkItemQueryService.MaxItemsPerView + 100);

        // Seeded items have no SprintId, so they are all backlog.
        var page = await service.GetBacklogItemsAsync(listId, TestContext.Current.CancellationToken);

        Assert.Equal(WorkItemQueryService.MaxItemsPerView, page.Items.Count);
        Assert.True(page.IsTruncated);
    }

    /// <summary>
    /// The cap has to be low enough to matter. 20,000 items at roughly 5 KB per materialized row
    /// was the measured failure; anything near that ceiling would not have fixed anything.
    /// </summary>
    [Fact]
    public void Cap_IsWellBelowTheMeasuredFailurePoint()
    {
        Assert.InRange(WorkItemQueryService.MaxItemsPerView, 1, 2000);
    }

    [Fact]
    public async Task ItemsOfOtherLists_AreNeverIncluded()
    {
        var (service, listId) = await SeedAsync(10);
        var (_, otherListId) = await SeedAsync(10);

        var page = await service.GetWorkItemsForListAsync(listId, TestContext.Current.CancellationToken);

        Assert.All(page.Items, i => Assert.Equal(listId, i.TaskListId));
        Assert.NotEqual(listId, otherListId);
    }
}
