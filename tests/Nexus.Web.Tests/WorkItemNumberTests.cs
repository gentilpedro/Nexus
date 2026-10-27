using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Infrastructure.Data.Migrations;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Código da tarefa (#63): era o começo do GUID e repetia; agora é um número sequencial por
/// workspace guardado na tarefa.
/// </summary>
public class WorkItemNumberTests
{
    private static async Task<Guid> AddWorkspaceAsync(IDbContextFactory<AppDbContext> factory, int lastNumber = 0)
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = id, Name = "W", Slug = "w-" + id.ToString("N"), CreatedAtUtc = DateTime.UtcNow, LastWorkItemNumber = lastNumber });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task<int> NextAsync(IDbContextFactory<AppDbContext> factory, Guid workspaceId)
    {
        await using var db = factory.CreateDbContext();
        var number = await WorkItemNumbers.NextAsync(db, workspaceId, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return number;
    }

    [Fact]
    public async Task Numbers_AreSequentialPerWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var a = await AddWorkspaceAsync(factory);
        var b = await AddWorkspaceAsync(factory, lastNumber: 41);

        Assert.Equal(1, await NextAsync(factory, a));
        Assert.Equal(2, await NextAsync(factory, a));
        Assert.Equal(42, await NextAsync(factory, b));
        Assert.Equal(3, await NextAsync(factory, a));
    }

    [Theory]
    [InlineData(42, "TASK-42")]
    [InlineData(1, "TASK-1")]
    [InlineData(0, "")]
    public void Code_IsTaskDashNumber(int number, string expected)
    {
        Assert.Equal(expected, new WorkItem { Number = number }.Code);
    }

    [Theory]
    [InlineData("TASK-42", 42)]
    [InlineData("task-42", 42)]
    [InlineData(" Task 42 ", 42)]
    [InlineData("#42", 42)]
    [InlineData("42", 42)]
    [InlineData("TASK-", null)]
    [InlineData("TASK-0", null)]
    [InlineData("pix 42", null)]
    [InlineData("TASK-D3E700", null)]
    [InlineData("", null)]
    public void ParseNumber_RecognizesTaskCodes(string text, int? expected)
    {
        Assert.Equal(expected, WorkItemCodeSearch.ParseNumber(text));
    }

    [Fact]
    public async Task Search_FindsTheTaskByItsCode()
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;
        var workspaceId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        const string userId = "u1";

        await using (var db = factory.CreateDbContext())
        {
            db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "W", Slug = "w", CreatedAtUtc = DateTime.UtcNow });
            db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = userId, Role = WorkspaceRole.Member, JoinedAtUtc = DateTime.UtcNow });
            db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = workspaceId, Name = "S" });
            db.TaskLists.Add(new TaskList { Id = listId, SpaceId = spaceId, Name = "L" });
            db.TaskStatusDefinitions.Add(new TaskStatusDefinition { Id = statusId, TaskListId = listId, Name = "A fazer", Category = StatusCategory.ToDo });
            db.WorkItems.Add(new WorkItem { Id = Guid.NewGuid(), Number = 7, TaskListId = listId, StatusId = statusId, Title = "Pagamento por Pix" });
            db.WorkItems.Add(new WorkItem { Id = Guid.NewGuid(), Number = 8, TaskListId = listId, StatusId = statusId, Title = "Recibo por e-mail" });
            await db.SaveChangesAsync(ct);
        }

        var service = new WorkItemQueryService(factory);

        var byCode = await service.SearchWorkItemsAsync(userId, new WorkItemSearchFilters { Text = "TASK-8" }, ct);
        Assert.Equal("Recibo por e-mail", Assert.Single(byCode).Title);

        var byTitle = await service.SearchWorkItemsAsync(userId, new WorkItemSearchFilters { Text = "Pix" }, ct);
        Assert.Equal(7, Assert.Single(byTitle).Number);
    }

    [Fact]
    public void Migration_NumbersExistingTasksAndSetsTheCounter()
    {
        var builder = new MigrationBuilder(activeProvider: "Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddWorkItemNumber(), [builder]);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Contains(columns, c => c.Table == "WorkItems" && c.Name == "Number");
        Assert.Contains(columns, c => c.Table == "Workspaces" && c.Name == "LastWorkItemNumber");

        var backfill = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("row_number() OVER (PARTITION BY s.\"WorkspaceId\" ORDER BY wi.\"CreatedAtUtc\"", backfill, StringComparison.Ordinal);
        Assert.Contains("\"LastWorkItemNumber\" = m.max_number", backfill, StringComparison.Ordinal);

        // The backfill reads the new columns, so it must come after both are added.
        Assert.IsType<SqlOperation>(builder.Operations[^1]);
    }
}
