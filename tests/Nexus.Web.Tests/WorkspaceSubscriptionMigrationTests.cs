using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nexus.Infrastructure.Data.Migrations;

namespace Nexus.Web.Tests;

/// <summary>
/// Guards the backfill that ships with the plan model (#49): every workspace that existed before
/// plans must get a Starter subscription in the same migration that creates the table.
/// </summary>
/// <remarks>
/// Asserted against the migration's recorded operations, like <see cref="MigrationBackfillTests"/>,
/// so it runs in CI without a database.
/// </remarks>
public class WorkspaceSubscriptionMigrationTests
{
    private static IReadOnlyList<MigrationOperation> UpOperations()
    {
        var up = typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");
        up.Invoke(new AddWorkspaceSubscriptions(), [builder]);
        return builder.Operations;
    }

    [Fact]
    public void Migration_BackfillsAStarterSubscriptionForEveryWorkspace()
    {
        var backfill = Assert.Single(UpOperations().OfType<SqlOperation>()).Sql;

        Assert.Contains("INSERT INTO \"WorkspaceSubscriptions\"", backfill, StringComparison.Ordinal);
        Assert.Contains("FROM \"Workspaces\" w", backfill, StringComparison.Ordinal);
        // Plan 0 = Starter and Status 0 = Active — the stored ordinals WorkspacePlanTests pins.
        Assert.Contains("SELECT w.\"Id\", 0, 0,", backfill, StringComparison.Ordinal);
    }

    [Fact]
    public void Backfill_RunsAfterTheTableIsCreated()
    {
        var ops = UpOperations();

        var create = ops.ToList().FindIndex(o => o is CreateTableOperation { Name: "WorkspaceSubscriptions" });
        var insert = ops.ToList().FindIndex(o => o is SqlOperation);

        Assert.True(create >= 0 && create < insert);
    }
}
