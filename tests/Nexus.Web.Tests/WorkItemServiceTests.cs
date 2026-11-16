using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

public class WorkItemServiceTests
{
    private static readonly WorkItemActor Erp = new(null, "Integração \"ERP\"");

    private sealed record Seed(Guid WorkspaceId, Guid ListId, Guid TodoStatusId, Guid DoingStatusId, Guid DoneStatusId, string MemberId, string OutsiderId);

    private static async Task<Seed> SeedAsync(IDbContextFactory<AppDbContext> factory, bool restrictTransitions = false)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        await using var db = factory.CreateDbContext();

        db.Users.Add(new ApplicationUser { Id = seed.MemberId, UserName = "ana", Email = "ana@nexus.test", DisplayName = "Ana" });
        db.Users.Add(new ApplicationUser { Id = seed.OutsiderId, UserName = "bia", Email = "bia@nexus.test", DisplayName = "Bia" });
        db.Workspaces.Add(new Workspace { Id = seed.WorkspaceId, Name = "Acme", Slug = "acme", CreatedAtUtc = DateTime.UtcNow });
        db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = seed.WorkspaceId, UserId = seed.MemberId, Role = WorkspaceRole.Member, JoinedAtUtc = DateTime.UtcNow });

        var spaceId = Guid.NewGuid();
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = seed.WorkspaceId, Name = "Operações" });
        db.TaskLists.Add(new TaskList { Id = seed.ListId, SpaceId = spaceId, Name = "Pedidos" });
        db.TaskStatusDefinitions.AddRange(
            new TaskStatusDefinition { Id = seed.DoingStatusId, TaskListId = seed.ListId, Name = "Fazendo", SortOrder = 1, Category = StatusCategory.InProgress },
            new TaskStatusDefinition { Id = seed.TodoStatusId, TaskListId = seed.ListId, Name = "A fazer", SortOrder = 0, Category = StatusCategory.ToDo, IsDefault = true },
            new TaskStatusDefinition { Id = seed.DoneStatusId, TaskListId = seed.ListId, Name = "Feito", SortOrder = 2, Category = StatusCategory.Done });

        if (restrictTransitions)
        {
            // Só "A fazer" -> "Fazendo" é permitido; pular direto para "Feito" não é.
            db.StatusTransitions.Add(new StatusTransition { Id = Guid.NewGuid(), FromStatusId = seed.TodoStatusId, ToStatusId = seed.DoingStatusId });
        }

        await db.SaveChangesAsync();
        return seed;
    }

    private static WorkItemService Service(IDbContextFactory<AppDbContext> factory) =>
        new(factory, new WorkItemQueryService(factory));

    [Fact]
    public async Task Create_UsesTheListDefaultStatus_AndTheNextWorkspaceNumber()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var first = await Service(factory).CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "  Separar pedido 1001 "), Erp, ct);
        var second = await Service(factory).CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Faturar pedido 1001"), Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.Ok, first.Status);
        Assert.Equal("Separar pedido 1001", first.Item!.Title);
        Assert.Equal(seed.TodoStatusId, first.Item.StatusId);
        Assert.Equal(WorkItemType.Task, first.Item.Type);
        Assert.Equal(WorkItemPriority.Medium, first.Item.Priority);
        Assert.Equal("TASK-1", first.Item.Code);
        Assert.Equal("TASK-2", second.Item!.Code);
    }

    [Fact]
    public async Task Create_RejectsAListFromAnotherWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var outcome = await Service(factory).CreateAsync(Guid.NewGuid(), new CreateWorkItemRequest(seed.ListId, "Qualquer"), Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.Invalid, outcome.Status);
        Assert.Contains("taskListId", outcome.Errors!.Keys);
    }

    [Fact]
    public async Task Create_CollectsEveryValidationErrorAtOnce()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var outcome = await Service(factory).CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(
            seed.ListId,
            Title: "   ",
            StatusId: Guid.NewGuid(),
            AssigneeId: seed.OutsiderId,
            StartDateUtc: new DateTime(2026, 12, 10),
            DueDateUtc: new DateTime(2026, 12, 1),
            ExternalReference: new string('x', WorkItem.MaxExternalReferenceLength + 1)), Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.Invalid, outcome.Status);
        Assert.Equal(["assigneeId", "externalReference", "startDateUtc", "statusId", "title"], outcome.Errors!.Keys.Order());
    }

    [Fact]
    public async Task Create_WithTheSameExternalReference_ReturnsTheExistingTaskInsteadOfDuplicating()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateWorkItemRequest(seed.ListId, "Pedido 1001", ExternalReference: "ERP-1001");

        var first = await Service(factory).CreateAsync(seed.WorkspaceId, request, Erp, ct);
        var retry = await Service(factory).CreateAsync(seed.WorkspaceId, request with { Title = "Pedido 1001 (reenvio)" }, Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.Conflict, retry.Status);
        Assert.Equal(first.Item!.Id, retry.Item!.Id);
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.WorkItems.CountAsync(ct));
    }

    [Fact]
    public async Task Create_WithAnAssignee_NotifiesThem_AndStoresDatesAsUtc()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var outcome = await Service(factory).CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(
            seed.ListId, "Conferir estoque", AssigneeId: seed.MemberId, DueDateUtc: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Unspecified)), Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.Ok, outcome.Status);
        Assert.Equal(DateTimeKind.Utc, outcome.Item!.DueDateUtc!.Value.Kind);
        await using var db = factory.CreateDbContext();
        var notification = await db.Notifications.SingleAsync(ct);
        Assert.Equal(seed.MemberId, notification.UserId);
        Assert.Equal(NotificationType.TaskAssigned, notification.Type);
        Assert.Contains("Integração \"ERP\" atribuiu TASK-1", notification.Message);
    }

    [Fact]
    public async Task Update_ByCode_ChangesOnlyTheFieldsSent()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var created = await Service(factory).CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(
            seed.ListId, "Pedido 1001", Description: "Cliente pediu urgência", Priority: WorkItemPriority.High, ExternalReference: "ERP-1001"), Erp, ct);

        var outcome = await Service(factory).UpdateAsync(seed.WorkspaceId, "task-1", new UpdateWorkItemRequest(
            StatusId: seed.DoingStatusId, AssigneeId: seed.MemberId, ExternalReference: ""), Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.Ok, outcome.Status);
        var item = outcome.Item!;
        Assert.Equal(created.Item!.Id, item.Id);
        Assert.Equal("Pedido 1001", item.Title);
        Assert.Equal("Cliente pediu urgência", item.Description);
        Assert.Equal(WorkItemPriority.High, item.Priority);
        Assert.Equal(seed.DoingStatusId, item.StatusId);
        Assert.Equal(seed.MemberId, item.AssigneeId);
        Assert.Null(item.ExternalReference);
        Assert.NotNull(item.UpdatedAtUtc);
    }

    [Fact]
    public async Task Update_RefusesAStatusChangeTheListWorkflowForbids()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory, restrictTransitions: true);
        var ct = TestContext.Current.CancellationToken;
        await Service(factory).CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Pedido 1001"), Erp, ct);

        var skipped = await Service(factory).UpdateAsync(seed.WorkspaceId, "TASK-1", new UpdateWorkItemRequest(StatusId: seed.DoneStatusId), Erp, ct);
        var allowed = await Service(factory).UpdateAsync(seed.WorkspaceId, "TASK-1", new UpdateWorkItemRequest(StatusId: seed.DoingStatusId), Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.Invalid, skipped.Status);
        Assert.Contains("statusId", skipped.Errors!.Keys);
        Assert.Equal(WorkItemOutcomeStatus.Ok, allowed.Status);
        Assert.Equal(seed.DoingStatusId, allowed.Item!.StatusId);
    }

    [Fact]
    public async Task Update_OfATaskFromAnotherWorkspace_IsNotFound()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var other = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var created = await Service(factory).CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, "Pedido 1001"), Erp, ct);

        var outcome = await Service(factory).UpdateAsync(other.WorkspaceId, created.Item!.Id.ToString(), new UpdateWorkItemRequest(Title: "Invadido"), Erp, ct);

        Assert.Equal(WorkItemOutcomeStatus.NotFound, outcome.Status);
        Assert.Equal("Pedido 1001", (await Service(factory).GetAsync(seed.WorkspaceId, "TASK-1", ct))!.Title);
    }

    [Fact]
    public async Task List_FiltersAndPaginates()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        for (var i = 1; i <= 5; i++)
        {
            await service.CreateAsync(seed.WorkspaceId, new CreateWorkItemRequest(seed.ListId, $"Pedido {i}", AssigneeId: i % 2 == 0 ? seed.MemberId : null), Erp, ct);
        }

        var page = await service.ListAsync(seed.WorkspaceId, new WorkItemListFilter(Page: 2, PageSize: 2), ct);
        var assigned = await service.ListAsync(seed.WorkspaceId, new WorkItemListFilter(AssigneeId: seed.MemberId, PageSize: 500), ct);
        var elsewhere = await service.ListAsync(Guid.NewGuid(), new WorkItemListFilter(), ct);

        Assert.Equal(5, page.Total);
        Assert.Equal(["TASK-3", "TASK-4"], page.Items.Select(i => i.Code));
        Assert.Equal(2, assigned.Total);
        Assert.Equal(WorkItemService.MaxPageSize, assigned.PageSize);
        Assert.Equal(0, elsewhere.Total);
    }
}
