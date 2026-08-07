using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nexus.Infrastructure.Data.Migrations;

namespace Nexus.Web.Tests;

/// <summary>
/// Guards the data backfill that accompanies H4. Enabling
/// <c>SignIn.RequireConfirmedAccount</c> without it locks every existing account out of the
/// product on the next deploy, because none of them were ever sent a confirmation e-mail.
/// </summary>
/// <remarks>
/// Asserted against the migration's recorded operations rather than a live PostgreSQL instance,
/// so the check runs in CI without a database. It verifies the UPDATE is present, targets the
/// right column, and is ordered before the index builds.
/// </remarks>
public class MigrationBackfillTests
{
    private static IReadOnlyList<MigrationOperation> UpOperations()
    {
        var migration = new ConfirmExistingUsersAndAddPerformanceIndexes();

        var up = typeof(Migration)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");
        up.Invoke(migration, [builder]);

        return builder.Operations;
    }

    [Fact]
    public void Migration_BackfillsEmailConfirmedForExistingUsers()
    {
        var sql = UpOperations().OfType<SqlOperation>().Select(o => o.Sql).ToList();

        var backfill = Assert.Single(sql, s => s.Contains("AspNetUsers", StringComparison.Ordinal));

        Assert.Contains("EmailConfirmed", backfill, StringComparison.Ordinal);
        Assert.Contains("UPDATE", backfill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TRUE", backfill, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_CreatesThePerformanceIndexes()
    {
        var indexes = UpOperations().OfType<CreateIndexOperation>().ToList();

        Assert.Contains(indexes, i => i.Table == "WorkItems" && i.Columns.Contains("DueDateUtc"));
        Assert.Contains(indexes, i => i.Table == "Notifications" && i.Columns.Contains("CreatedAtUtc"));
    }

    /// <summary>
    /// The backfill must run before the index builds: index creation takes an exclusive lock, and
    /// ordering the user-facing unlock first keeps the window where accounts are locked out as
    /// short as possible.
    /// </summary>
    [Fact]
    public void Backfill_RunsBeforeIndexCreation()
    {
        var operations = UpOperations();

        var backfillIndex = operations.ToList().FindIndex(o => o is SqlOperation);
        var firstIndexBuild = operations.ToList().FindIndex(o => o is CreateIndexOperation);

        Assert.True(backfillIndex >= 0, "no backfill SQL operation found");
        Assert.True(backfillIndex < firstIndexBuild, "backfill must precede index creation");
    }
}
