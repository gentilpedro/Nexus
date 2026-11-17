using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;
using Nexus.Web.Services.Webhooks;

namespace Nexus.Web.Tests;

public class WorkItemEventPublisherTests
{
    private sealed record Seed(Guid WorkspaceId, Guid ListId, Guid TodoId, Guid DoneId);

    private static async Task<Seed> SeedAsync(IDbContextFactory<AppDbContext> factory)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = factory.CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = "ana", UserName = "ana", Email = "ana@acme.com.br", DisplayName = "Ana" });
        db.Workspaces.Add(new Workspace { Id = seed.WorkspaceId, Name = "Acme", Slug = "acme", CreatedAtUtc = DateTime.UtcNow });
        db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = seed.WorkspaceId, UserId = "ana", Role = WorkspaceRole.Member, JoinedAtUtc = DateTime.UtcNow });
        var spaceId = Guid.NewGuid();
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = seed.WorkspaceId, Name = "Operações" });
        db.TaskLists.Add(new TaskList { Id = seed.ListId, SpaceId = spaceId, Name = "Pedidos" });
        db.TaskStatusDefinitions.AddRange(
            new TaskStatusDefinition { Id = seed.TodoId, TaskListId = seed.ListId, Name = "A fazer", SortOrder = 0, Category = StatusCategory.ToDo, IsDefault = true },
            new TaskStatusDefinition { Id = seed.DoneId, TaskListId = seed.ListId, Name = "Feito", SortOrder = 1, Category = StatusCategory.Done });
        await db.SaveChangesAsync();
        return seed;
    }

    private static (WorkItemService Tasks, WebhookService Webhooks) Services(IDbContextFactory<AppDbContext> factory)
    {
        var webhooks = WebhookServiceTests.Service(factory);
        var publisher = new WorkItemEventPublisher(webhooks);
        return (new WorkItemService(factory, new WorkItemQueryService(factory, events: publisher), events: publisher), webhooks);
    }

    private static async Task<List<WebhookDelivery>> DeliveriesAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.WebhookDeliveries.AsNoTracking().OrderBy(d => d.EventType).ToListAsync(TestContext.Current.CancellationToken);
    }

    private static JsonElement Body(WebhookDelivery delivery) => JsonDocument.Parse(delivery.Payload).RootElement;

    [Fact]
    public async Task CreatingATaskThroughTheApi_EnqueuesTaskCreated_WithTheTaskInApiFormat()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (tasks, webhooks) = Services(factory);
        await webhooks.CreateAsync(seed.WorkspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.All, null, ct);

        var outcome = await tasks.CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Pedido 1001", AssigneeId: "ana", ExternalReference: "ERP-1001"), new WorkItemActor(null, "Integração \"ERP\""), ct);

        Assert.Equal(WorkItemOutcomeStatus.Ok, outcome.Status);
        var delivery = Assert.Single(await DeliveriesAsync(factory));
        Assert.Equal("task.created", delivery.EventType);
        var body = Body(delivery);
        Assert.Equal("task.created", body.GetProperty("type").GetString());
        Assert.Equal(seed.WorkspaceId.ToString(), body.GetProperty("workspaceId").GetString());
        Assert.Equal("TASK-1", body.GetProperty("task").GetProperty("code").GetString());
        Assert.Equal("ERP-1001", body.GetProperty("task").GetProperty("externalReference").GetString());
        Assert.Equal("A fazer", body.GetProperty("task").GetProperty("status").GetProperty("name").GetString());
        Assert.Equal("Ana", body.GetProperty("task").GetProperty("assignee").GetProperty("name").GetString());
        Assert.Equal("Integração \"ERP\"", body.GetProperty("actor").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("previous").ValueKind);
    }

    [Fact]
    public async Task ChangingStatusAndTitle_EnqueuesOneEventPerKindOfChange_WithThePreviousValues()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (tasks, webhooks) = Services(factory);
        var actor = new WorkItemActor("ana", "Ana");
        await tasks.CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Pedido 1001"), actor, ct);
        await webhooks.CreateAsync(seed.WorkspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.All, null, ct);

        await tasks.UpdateAsync(seed.WorkspaceId, "TASK-1", new UpdateWorkItemRequest(Title: "Pedido 1001 urgente", StatusId: seed.DoneId, AssigneeId: "ana"), actor, ct);

        var deliveries = await DeliveriesAsync(factory);
        Assert.Equal(["task.assignee_changed", "task.status_changed", "task.updated"], deliveries.Select(d => d.EventType));
        var status = Body(deliveries[1]);
        Assert.Equal("A fazer", status.GetProperty("previous").GetProperty("status").GetProperty("name").GetString());
        Assert.Equal("Feito", status.GetProperty("task").GetProperty("status").GetProperty("name").GetString());
        Assert.Equal("ana", status.GetProperty("actor").GetProperty("id").GetString());
        var assignee = Body(deliveries[0]);
        Assert.Equal(JsonValueKind.Null, assignee.GetProperty("previous").GetProperty("assignee").ValueKind);
        var updated = Body(deliveries[2]);
        Assert.Equal("Pedido 1001", updated.GetProperty("previous").GetProperty("title").GetString());
        Assert.Equal("Pedido 1001 urgente", updated.GetProperty("task").GetProperty("title").GetString());
        Assert.NotEqual(deliveries[0].EventId, deliveries[1].EventId);
    }

    [Fact]
    public async Task OnlySubscribedEventsAreEnqueued_AndNothingWithoutWebhooks()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (tasks, webhooks) = Services(factory);
        var actor = new WorkItemActor("ana", "Ana");

        await tasks.CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Sem webhook"), actor, ct);
        Assert.Empty(await DeliveriesAsync(factory));

        await webhooks.CreateAsync(seed.WorkspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.TaskStatusChanged, null, ct);
        await tasks.UpdateAsync(seed.WorkspaceId, "TASK-1", new UpdateWorkItemRequest(Title: "Só o título mudou"), actor, ct);
        Assert.Empty(await DeliveriesAsync(factory));

        await tasks.UpdateAsync(seed.WorkspaceId, "TASK-1", new UpdateWorkItemRequest(StatusId: seed.DoneId, Priority: WorkItemPriority.High), actor, ct);
        var delivery = Assert.Single(await DeliveriesAsync(factory));
        Assert.Equal("task.status_changed", delivery.EventType);
    }

    [Fact]
    public async Task MovingACardOnTheBoard_EnqueuesTaskStatusChanged_WithoutActor()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (tasks, webhooks) = Services(factory);
        var created = await tasks.CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Pedido 1001"), new WorkItemActor("ana", "Ana"), ct);
        await webhooks.CreateAsync(seed.WorkspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.TaskStatusChanged, null, ct);
        var queries = new WorkItemQueryService(factory, events: new WorkItemEventPublisher(webhooks));

        Assert.True(await queries.UpdateWorkItemStatusAsync(seed.ListId, created.Item!.Id, seed.DoneId, 0, ct));

        var delivery = Assert.Single(await DeliveriesAsync(factory));
        Assert.Equal("task.status_changed", delivery.EventType);
        Assert.Equal(JsonValueKind.Null, Body(delivery).GetProperty("actor").ValueKind);
    }

    [Fact]
    public async Task ACommentEnqueuesCommentCreated_WithAuthorAndTask()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (tasks, webhooks) = Services(factory);
        var created = await tasks.CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Pedido 1001"), new WorkItemActor("ana", "Ana"), ct);
        await webhooks.CreateAsync(seed.WorkspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.CommentCreated, null, ct);
        var commentId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.WorkItemComments.Add(new WorkItemComment { Id = commentId, WorkItemId = created.Item!.Id, UserId = "ana", Content = "Cliente ligou", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await new WorkItemEventPublisher(webhooks).CommentCreatedAsync(db, commentId, new EventActor("ana"), ct);
        }

        var delivery = Assert.Single(await DeliveriesAsync(factory));
        var body = Body(delivery);
        Assert.Equal("comment.created", body.GetProperty("type").GetString());
        Assert.Equal("Cliente ligou", body.GetProperty("comment").GetProperty("content").GetString());
        Assert.Equal("Ana", body.GetProperty("comment").GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("TASK-1", body.GetProperty("task").GetProperty("code").GetString());
        Assert.Equal("Ana", body.GetProperty("actor").GetProperty("name").GetString());
    }
}
