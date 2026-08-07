using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Infrastructure.Data.Migrations;

namespace Nexus.Web.Tests;

/// <summary>
/// Regression tests for T1 — user-authored columns stored as unbounded PostgreSQL `text`, and
/// for the pre-flight guard that keeps the narrowing migration from taking the site down.
/// </summary>
public class ContentLimitTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"limits-{Guid.NewGuid()}")
            .Options);

    [Theory]
    [InlineData(typeof(WorkItem), nameof(WorkItem.Description), WorkItem.MaxDescriptionLength)]
    [InlineData(typeof(DocPage), nameof(DocPage.ContentHtml), DocPage.MaxContentHtmlLength)]
    [InlineData(typeof(DocPage), nameof(DocPage.GridDataJson), DocPage.MaxGridDataJsonLength)]
    public void UserAuthoredColumns_HaveAMaximumLength(Type entity, string property, int expected)
    {
        using var db = CreateContext();

        var maxLength = db.Model.FindEntityType(entity)!.FindProperty(property)!.GetMaxLength();

        Assert.Equal(expected, maxLength);
    }

    /// <summary>
    /// Everything a user can type into must be bounded somewhere. This sweeps the content-bearing
    /// entities so a newly added free-text column cannot quietly ship as unbounded text.
    /// </summary>
    [Fact]
    public void NoContentBearingStringColumnIsUnbounded()
    {
        using var db = CreateContext();

        var unbounded = new List<string>();
        foreach (var entityType in new[] { typeof(WorkItem), typeof(DocPage), typeof(ChatMessage), typeof(WorkItemComment) })
        {
            var model = db.Model.FindEntityType(entityType)!;
            foreach (var property in model.GetProperties())
            {
                if (property.ClrType != typeof(string) || property.GetMaxLength() is not null)
                {
                    continue;
                }

                // Identity foreign keys (UserId, CreatedByUserId, ...) are framework-owned columns,
                // not user-authored content — their shape is fixed by ASP.NET Identity.
                if (property.Name.EndsWith("UserId", StringComparison.Ordinal) || property.IsForeignKey())
                {
                    continue;
                }

                unbounded.Add($"{entityType.Name}.{property.Name}");
            }
        }

        Assert.Empty(unbounded);
    }

    // ---- migration pre-flight guard ------------------------------------------------------

    private static IReadOnlyList<MigrationOperation> UpOperations(Migration migration)
    {
        var up = typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");
        up.Invoke(migration, [builder]);
        return builder.Operations;
    }

    /// <summary>
    /// The narrowing migration must refuse to run when data would not fit, rather than letting
    /// PostgreSQL abort with a generic "value too long" error — migrations apply on startup, so
    /// that abort is a site outage with no diagnosis attached.
    /// </summary>
    [Fact]
    public void NarrowingMigration_ChecksForOversizedRowsFirst()
    {
        var operations = UpOperations(new AddContentLengthLimits());

        var guard = operations.OfType<SqlOperation>().FirstOrDefault();
        Assert.NotNull(guard);

        Assert.Contains("RAISE EXCEPTION", guard!.Sql, StringComparison.Ordinal);
        Assert.Contains("WorkItems", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("DocPages", guard.Sql, StringComparison.Ordinal);

        // Must not silently repair data to make the deploy pass.
        Assert.DoesNotContain("UPDATE", guard.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", guard.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("substring", guard.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NarrowingMigration_RunsTheGuardBeforeAlteringColumns()
    {
        var operations = UpOperations(new AddContentLengthLimits()).ToList();

        var guardIndex = operations.FindIndex(o => o is SqlOperation);
        var firstAlter = operations.FindIndex(o => o is AlterColumnOperation);

        Assert.True(guardIndex >= 0, "no pre-flight guard found");
        Assert.True(guardIndex < firstAlter, "guard must run before any ALTER COLUMN");
    }

    [Fact]
    public void NarrowingMigration_GuardThresholdsMatchTheModel()
    {
        var sql = UpOperations(new AddContentLengthLimits()).OfType<SqlOperation>().First().Sql;

        Assert.Contains(WorkItem.MaxDescriptionLength.ToString(), sql, StringComparison.Ordinal);
        Assert.Contains(DocPage.MaxContentHtmlLength.ToString(), sql, StringComparison.Ordinal);
        Assert.Contains(DocPage.MaxGridDataJsonLength.ToString(), sql, StringComparison.Ordinal);
    }

    // ---- partial index (IX1) --------------------------------------------------------------

    /// <summary>
    /// The due-date index must be partial. A plain btree also indexes every NULL row, which made
    /// it nearly as large as the table while the planner ignored it in favour of a sequential scan.
    /// </summary>
    [Fact]
    public void DueDateIndex_IsPartial()
    {
        using var db = CreateContext();

        var index = db.Model.FindEntityType(typeof(WorkItem))!
            .GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(WorkItem.DueDateUtc));

        Assert.Equal("\"DueDateUtc\" IS NOT NULL", index.GetFilter());
    }
}
