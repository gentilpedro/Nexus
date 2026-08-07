using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Infrastructure.Tests;

/// <summary>
/// Asserts the EF model constraints that the application's correctness and data integrity
/// actually depend on. Each of these is load-bearing in a way that is easy to break silently —
/// a configuration file is edited, the app still compiles, the app still runs, and the defect
/// only appears as corrupted data in production.
/// </summary>
public class ModelConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"model-{Guid.NewGuid()}")
            .Options);

    /// <summary>
    /// This unique index is what makes WorkspaceInviteService.AcceptAsync's race handling sound:
    /// two concurrent accepts of the same invite rely on the database rejecting the second
    /// insert. Without it, double-clicking an invite link creates duplicate memberships.
    /// </summary>
    [Fact]
    public void WorkspaceMember_HasUniqueIndexOnWorkspaceAndUser()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(WorkspaceMember))!;

        var index = entity.GetIndexes().SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).OrderBy(n => n).SequenceEqual(new[] { "UserId", "WorkspaceId" }));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    /// <summary>
    /// Invite tokens are looked up by value on an anonymous endpoint; the unique index both
    /// enforces non-reuse and keeps that lookup from becoming a scan.
    /// </summary>
    [Fact]
    public void WorkspaceInvite_HasUniqueIndexOnToken()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(WorkspaceInvite))!;

        var index = entity.GetIndexes().SingleOrDefault(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(WorkspaceInvite.Token));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    /// <summary>
    /// Optimistic concurrency on WorkItem. Without a concurrency token, two people editing the
    /// same task means the second save silently discards the first person's changes.
    /// </summary>
    [Fact]
    public void WorkItem_HasAConcurrencyToken()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(WorkItem))!;

        Assert.Contains(entity.GetProperties(), p => p.IsConcurrencyToken);
    }

    /// <summary>
    /// Deleting a status or a user must never cascade into deleting work items. This is the
    /// difference between "an admin removed a column" and "an admin destroyed the team's tasks".
    /// </summary>
    [Theory]
    [InlineData(nameof(WorkItem.StatusId), DeleteBehavior.Restrict)]
    [InlineData(nameof(WorkItem.CreatedByUserId), DeleteBehavior.Restrict)]
    [InlineData(nameof(WorkItem.ParentEpicId), DeleteBehavior.Restrict)]
    [InlineData(nameof(WorkItem.AssigneeId), DeleteBehavior.SetNull)]
    public void WorkItem_ForeignKeysDoNotCascadeDeletes(string foreignKeyProperty, DeleteBehavior expected)
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(WorkItem))!;

        var fk = entity.GetForeignKeys().Single(f => f.Properties.Any(p => p.Name == foreignKeyProperty));

        Assert.Equal(expected, fk.DeleteBehavior);
    }

    // Backs the new index added for DataRetentionHostedService's delete-by-age sweep (audit M10).
    [Fact]
    public void Notification_IsIndexedForTheRetentionSweep()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(Notification))!;

        Assert.Contains(entity.GetIndexes(), i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(Notification.CreatedAtUtc));
    }

    // Backs the new index added for DueDateNotificationHostedService's hourly scan (audit M10).
    [Fact]
    public void WorkItem_IsIndexedOnDueDate()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(WorkItem))!;

        Assert.Contains(entity.GetIndexes(), i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(WorkItem.DueDateUtc));
    }

    /// <summary>
    /// The Data Protection key ring lives in this context. If the entity ever stops being mapped,
    /// key persistence silently falls back and every restart logs the whole user base out.
    /// </summary>
    [Fact]
    public void DataProtectionKeys_AreMapped()
    {
        using var db = CreateContext();

        Assert.NotNull(db.Model.FindEntityType(
            typeof(Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey)));
    }
}
