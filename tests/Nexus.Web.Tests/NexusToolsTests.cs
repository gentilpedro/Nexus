using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Api;
using Nexus.Web.Mcp;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

public class NexusToolsTests
{
    private sealed record Seed(Guid WorkspaceId, Guid ListId, Guid TodoId, Guid DoneId);

    private static async Task<Seed> SeedAsync(IDbContextFactory<AppDbContext> factory)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = factory.CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = "ana", UserName = "ana", Email = "ana@acme.com.br", DisplayName = "Ana" });
        db.Workspaces.Add(new Workspace { Id = seed.WorkspaceId, Name = "Acme", Slug = "acme", CreatedAtUtc = DateTime.UtcNow });
        db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = seed.WorkspaceId, UserId = "ana", Role = WorkspaceRole.Owner, JoinedAtUtc = DateTime.UtcNow });
        var spaceId = Guid.NewGuid();
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = seed.WorkspaceId, Name = "Operações" });
        db.TaskLists.Add(new TaskList { Id = seed.ListId, SpaceId = spaceId, Name = "Pedidos" });
        db.TaskStatusDefinitions.AddRange(
            new TaskStatusDefinition { Id = seed.TodoId, TaskListId = seed.ListId, Name = "A fazer", SortOrder = 0, Category = StatusCategory.ToDo, IsDefault = true },
            new TaskStatusDefinition { Id = seed.DoneId, TaskListId = seed.ListId, Name = "Feito", SortOrder = 1, Category = StatusCategory.Done });
        await db.SaveChangesAsync();
        return seed;
    }

    // O principal que o ApiKeyAuthenticationHandler monta para uma chave, sem passar pelo HTTP.
    private static ClaimsPrincipal Principal(Guid workspaceId, ApiKeyScope scope = ApiKeyScope.ReadWrite, bool apiAccess = true, bool archived = false) =>
        new(new ClaimsIdentity(
        [
            new Claim(ApiKeyClaims.KeyId, Guid.NewGuid().ToString()),
            new Claim(ApiKeyClaims.WorkspaceId, workspaceId.ToString()),
            new Claim(ApiKeyClaims.KeyName, "ERP"),
            new Claim(ApiKeyClaims.CreatedByUserId, "ana"),
            new Claim(ApiKeyClaims.Scope, scope.ToString()),
            new Claim(ApiKeyClaims.ApiAccessAllowed, apiAccess ? "true" : "false"),
            new Claim(ApiKeyClaims.WorkspaceArchived, archived ? "true" : "false"),
        ], ApiKeyAuthenticationHandler.SchemeName));

    private static NexusTools Tools(IDbContextFactory<AppDbContext> factory)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = "https://nexus.test" }).Build();
        return new NexusTools(factory, new WorkItemService(factory, new WorkItemQueryService(factory)), configuration);
    }

    [Fact]
    public async Task CreateTask_ThenGetByCode_WorksLikeTheApi()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var tools = Tools(factory);
        var user = Principal(seed.WorkspaceId);

        var created = await tools.CreateTask(user, seed.ListId, "Separar pedido 1001", assigneeId: "ana", externalReference: "PEDIDO-1001", ct: ct);
        var fetched = await tools.GetTask(user, "task-1", ct);
        var again = await tools.CreateTask(user, seed.ListId, "Separar pedido 1001 (reenvio)", externalReference: "PEDIDO-1001", ct: ct);

        Assert.Equal("TASK-1", created.Code);
        Assert.Equal("A fazer", created.Status.Name);
        Assert.Equal("Ana", created.Assignee!.Name);
        Assert.Equal("https://nexus.test/lists/" + seed.ListId + "/list", created.Url);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.Id, again.Id);
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.WorkItems.CountAsync(ct));
        Assert.Equal("ana", (await db.WorkItems.SingleAsync(ct)).CreatedByUserId);
    }

    [Fact]
    public async Task UpdateTask_MovesStatus_AndListsReflectIt()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var tools = Tools(factory);
        var user = Principal(seed.WorkspaceId);
        await tools.CreateTask(user, seed.ListId, "Pedido 1001", ct: ct);

        var updated = await tools.UpdateTask(user, "TASK-1", statusId: seed.DoneId, priority: WorkItemPriority.High, ct: ct);
        var lists = await tools.ListLists(user, ct);
        var page = await tools.ListTasks(user, statusId: seed.DoneId, ct: ct);
        var members = await tools.ListMembers(user, ct);
        var workspace = await tools.GetWorkspace(user, ct);

        Assert.Equal("Feito", updated.Status.Name);
        Assert.Equal(WorkItemPriority.High, updated.Priority);
        Assert.Equal("Pedidos", Assert.Single(Assert.Single(lists).Lists).Name);
        Assert.Equal(1, page.Total);
        Assert.Equal("Ana", Assert.Single(members).Name);
        Assert.Equal("Acme", workspace.Name);
    }

    [Fact]
    public async Task ReadOnlyKey_CanRead_ButNotWrite()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var tools = Tools(factory);
        await tools.CreateTask(Principal(seed.WorkspaceId), seed.ListId, "Pedido 1001", ct: ct);
        var reader = Principal(seed.WorkspaceId, ApiKeyScope.ReadOnly);

        Assert.Equal("TASK-1", (await tools.GetTask(reader, "TASK-1", ct)).Code);
        var ex = await Assert.ThrowsAsync<McpException>(() => tools.CreateTask(reader, seed.ListId, "Nova", ct: ct));
        Assert.Contains("só de leitura", ex.Message);
        await Assert.ThrowsAsync<McpException>(() => tools.UpdateTask(reader, "TASK-1", title: "x", ct: ct));
    }

    [Fact]
    public async Task PlanWithoutApi_OrArchivedWorkspace_IsRefused_WithAReadableMessage()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var tools = Tools(factory);

        var noPlan = await Assert.ThrowsAsync<McpException>(() => tools.ListLists(Principal(seed.WorkspaceId, apiAccess: false), ct));
        var archived = await Assert.ThrowsAsync<McpException>(() => tools.ListLists(Principal(seed.WorkspaceId, archived: true), ct));

        Assert.Contains("plano", noPlan.Message);
        Assert.Contains("arquivado", archived.Message);
    }

    [Fact]
    public async Task ValidationErrors_BecomeAToolError_NamingTheField()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var tools = Tools(factory);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.CreateTask(Principal(seed.WorkspaceId), seed.ListId, "  ", assigneeId: "ninguem", ct: ct));

        Assert.Contains("title", ex.Message);
        Assert.Contains("assigneeId", ex.Message);
        var missing = await Assert.ThrowsAsync<McpException>(() => tools.GetTask(Principal(seed.WorkspaceId), "TASK-99", ct));
        Assert.Contains("TASK-99", missing.Message);
    }
}
