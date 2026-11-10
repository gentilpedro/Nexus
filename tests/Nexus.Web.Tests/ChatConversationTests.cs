using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data.Migrations;

namespace Nexus.Web.Tests;

/// <summary>
/// The parts of private conversations that hold without a database: the key that makes a direct
/// conversation unique per pair, and the order of the migration that moves existing messages into
/// their workspace's conversation.
/// </summary>
public class ChatConversationTests
{
    [Fact]
    public void DirectKey_IsTheSameWhicheverSideStarts()
    {
        // Two people opening a conversation with each other must land on the same row, so the
        // key cannot depend on who clicked first.
        Assert.Equal(ChatConversation.BuildDirectKey("b", "a"), ChatConversation.BuildDirectKey("a", "b"));
    }

    [Fact]
    public void DirectKey_DiffersBetweenPairs()
    {
        Assert.NotEqual(ChatConversation.BuildDirectKey("a", "b"), ChatConversation.BuildDirectKey("a", "c"));
    }

    private static List<MigrationOperation> UpOperations()
    {
        var migration = new AddChatConversations();
        var up = typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");
        up.Invoke(migration, [builder]);
        return builder.Operations.ToList();
    }

    [Fact]
    public void Migration_GivesEveryWorkspaceAConversationBeforePointingMessagesAtIt()
    {
        var operations = UpOperations();

        var createConversations = operations.FindIndex(o => o is CreateTableOperation { Name: "ChatConversations" });
        var insertConversations = operations.FindIndex(o => o is SqlOperation s
            && s.Sql.Contains("INSERT INTO \"ChatConversations\"", StringComparison.Ordinal)
            && s.Sql.Contains("FROM \"Workspaces\"", StringComparison.Ordinal));
        var addColumnNullable = operations.FindIndex(o => o is AddColumnOperation { Table: "ChatMessages", Name: "ConversationId", IsNullable: true });
        var backfillMessages = operations.FindIndex(o => o is SqlOperation s
            && s.Sql.Contains("UPDATE \"ChatMessages\"", StringComparison.Ordinal)
            && s.Sql.Contains("\"ConversationId\"", StringComparison.Ordinal));
        var makeRequired = operations.FindIndex(o => o is AlterColumnOperation { Table: "ChatMessages", Name: "ConversationId", IsNullable: false });
        var foreignKey = operations.FindIndex(o => o is AddForeignKeyOperation { Table: "ChatMessages", PrincipalTable: "ChatConversations" });

        Assert.True(createConversations >= 0, "ChatConversations table is not created");
        Assert.True(insertConversations > createConversations, "workspace conversations must be inserted after the table exists");
        Assert.True(addColumnNullable >= 0, "ConversationId must first be added as nullable");
        Assert.True(backfillMessages > addColumnNullable, "messages must be backfilled after the column exists");
        Assert.True(backfillMessages > insertConversations, "messages must be backfilled after the conversations exist");
        Assert.True(makeRequired > backfillMessages, "the column can only become NOT NULL after the backfill");
        Assert.True(foreignKey > makeRequired, "the foreign key must be added after the backfill");
    }

    [Fact]
    public void Migration_NeverAddsConversationIdAsRequiredWithAnEmptyDefault()
    {
        // The default EF scaffold does exactly this, which would silently leave every existing
        // message pointing at a conversation that does not exist.
        var operations = UpOperations();

        Assert.DoesNotContain(operations, o => o is AddColumnOperation { Table: "ChatMessages", Name: "ConversationId", IsNullable: false });
    }

    [Fact]
    public void Migration_MarksTheSharedChatAsReadForExistingMembers()
    {
        var sql = UpOperations().OfType<SqlOperation>().Select(o => o.Sql).ToList();

        var markers = Assert.Single(sql, s => s.Contains("INSERT INTO \"ChatConversationMembers\"", StringComparison.Ordinal));
        Assert.Contains("\"LastReadAtUtc\"", markers, StringComparison.Ordinal);
        Assert.Contains("FROM \"ChatConversations\"", markers, StringComparison.Ordinal);
        Assert.Contains("\"WorkspaceMembers\"", markers, StringComparison.Ordinal);
    }
}
