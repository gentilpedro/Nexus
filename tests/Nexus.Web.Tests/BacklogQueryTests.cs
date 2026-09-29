using Nexus.Domain.Entities;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the backlog queries of <see cref="WorkItemQueryService"/>.
/// </summary>
public class BacklogQueryTests
{
    /// <summary>
    /// Regression: completed sprints were loaded without their items' Status, but the backlog's
    /// history and velocity read <c>Status.Category</c> — a null reference as soon as a sprint was
    /// completed with items in it.
    /// </summary>
    [Fact]
    public async Task CompletedSprints_BringTheItemsStatus()
    {
        var factory = TestDb.CreateFactory();
        var listId = Guid.NewGuid();
        var doneId = Guid.NewGuid();
        var sprintId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.TaskStatusDefinitions.Add(new TaskStatusDefinition
            {
                Id = doneId,
                TaskListId = listId,
                Name = "Done",
                Category = StatusCategory.Done,
            });
            db.Sprints.Add(new Sprint
            {
                Id = sprintId,
                TaskListId = listId,
                Name = "Sprint 1",
                Status = SprintStatus.Completed,
                CompletedAtUtc = DateTime.UtcNow,
            });
            db.WorkItems.Add(new WorkItem
            {
                Id = Guid.NewGuid(),
                TaskListId = listId,
                StatusId = doneId,
                SprintId = sprintId,
                Title = "Entregue",
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var sprints = await new WorkItemQueryService(factory)
            .GetCompletedSprintsForListAsync(listId, TestContext.Current.CancellationToken);

        var item = Assert.Single(Assert.Single(sprints).WorkItems);
        Assert.NotNull(item.Status);
        Assert.Equal(StatusCategory.Done, item.Status.Category);
    }

    /// <summary>
    /// The backlog shows comment counts without loading comment bodies. Only the list's items are
    /// counted, and items without comments are simply absent.
    /// </summary>
    [Fact]
    public async Task CommentCounts_ArePerItemAndScopedToTheList()
    {
        var factory = TestDb.CreateFactory();
        var listId = Guid.NewGuid();
        var otherListId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        var withComments = Guid.NewGuid();
        var withoutComments = Guid.NewGuid();
        var otherListItem = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            foreach (var (id, list) in new[] { (withComments, listId), (withoutComments, listId), (otherListItem, otherListId) })
            {
                db.WorkItems.Add(new WorkItem { Id = id, TaskListId = list, StatusId = statusId, Title = "T" });
            }

            for (var i = 0; i < 3; i++)
            {
                db.WorkItemComments.Add(new WorkItemComment { Id = Guid.NewGuid(), WorkItemId = withComments, Content = "c" });
            }
            db.WorkItemComments.Add(new WorkItemComment { Id = Guid.NewGuid(), WorkItemId = otherListItem, Content = "c" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var counts = await new WorkItemQueryService(factory)
            .GetCommentCountsForListAsync(listId, TestContext.Current.CancellationToken);

        Assert.Equal(3, counts[withComments]);
        Assert.DoesNotContain(withoutComments, counts.Keys);
        Assert.DoesNotContain(otherListItem, counts.Keys);
    }
}
