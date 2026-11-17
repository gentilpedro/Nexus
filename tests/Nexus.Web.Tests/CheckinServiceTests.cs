using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

public class CheckinServiceTests
{
    // Terça-feira, 17/11/2026.
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedAsync(IDbContextFactory<AppDbContext> factory)
    {
        var workspaceId = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new ApplicationUser { Id = "ana", UserName = "ana", Email = "ana@acme.com.br", DisplayName = "Ana" });
        }

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Acme", Slug = "acme", CreatedAtUtc = DateTime.UtcNow });
        var spaceId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var doing = Guid.NewGuid();
        var done = Guid.NewGuid();
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = workspaceId, Name = "Operações" });
        db.TaskLists.Add(new TaskList { Id = listId, SpaceId = spaceId, Name = "Pedidos" });
        db.TaskStatusDefinitions.AddRange(
            new TaskStatusDefinition { Id = doing, TaskListId = listId, Name = "Fazendo", Category = StatusCategory.InProgress },
            new TaskStatusDefinition { Id = done, TaskListId = listId, Name = "Feito", Category = StatusCategory.Done, SortOrder = 1 });
        db.WorkItems.AddRange(
            new WorkItem { Id = Guid.NewGuid(), Number = 1, TaskListId = listId, Title = "Feita esta semana", StatusId = done, AssigneeId = "ana", CreatedAtUtc = Now.UtcDateTime.AddDays(-10), UpdatedAtUtc = Now.UtcDateTime.AddDays(-1) },
            new WorkItem { Id = Guid.NewGuid(), Number = 2, TaskListId = listId, Title = "Feita semana passada", StatusId = done, CreatedAtUtc = Now.UtcDateTime.AddDays(-20), UpdatedAtUtc = Now.UtcDateTime.AddDays(-8) },
            new WorkItem { Id = Guid.NewGuid(), Number = 3, TaskListId = listId, Title = "Em andamento", StatusId = doing, CreatedAtUtc = Now.UtcDateTime.AddDays(-3) });
        await db.SaveChangesAsync();
        return workspaceId;
    }

    private static CheckinService Service(IDbContextFactory<AppDbContext> factory) => new(factory, new FakeTimeProvider(Now));

    [Fact]
    public void WeekStart_IsTheMondayOfTheWeek_AtUtcMidnight()
    {
        Assert.Equal(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), WorkspaceCheckin.WeekStartOf(new DateTime(2026, 11, 17, 15, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), WorkspaceCheckin.WeekStartOf(new DateTime(2026, 11, 22, 23, 59, 0)));
        Assert.Equal(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), WorkspaceCheckin.WeekStartOf(new DateTime(2026, 11, 16)));
        Assert.Equal(DateTimeKind.Utc, WorkspaceCheckin.WeekStartOf(DateTime.Now).Kind);
    }

    [Fact]
    public async Task Save_CreatesOnePerWeek_AndSavingAgainEditsIt()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        var first = await service.SaveAsync(workspaceId, new CheckinInput(Now.UtcDateTime, CheckinPerception.OnTrack, " Entregamos a API ", "", "Webhooks", ""), "ana", ct);
        var second = await service.SaveAsync(workspaceId, new CheckinInput(Now.UtcDateTime.AddDays(3), CheckinPerception.AtRisk, "Entregamos a API", "Horas", "Webhooks", "Cliente sumiu"), "ana", ct);

        Assert.Equal(CheckinSaveStatus.Created, first.Status);
        Assert.Equal(CheckinSaveStatus.Updated, second.Status);
        Assert.Equal(first.Checkin!.Id, second.Checkin!.Id);
        var list = await service.ListAsync(workspaceId, ct: ct);
        var only = Assert.Single(list);
        Assert.Equal(CheckinPerception.AtRisk, only.Perception);
        Assert.Equal("Entregamos a API", only.Finished);
        Assert.Equal("Cliente sumiu", only.Observations);
        Assert.NotNull(only.UpdatedAtUtc);
        Assert.Equal("Ana", ApiMappingNames.DisplayNameOf(only.Author));
    }

    [Fact]
    public async Task Save_RefusesFutureWeeks_AndEmptyCheckins()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        var future = await service.SaveAsync(workspaceId, new CheckinInput(Now.UtcDateTime.AddDays(7), CheckinPerception.OnTrack, "x", "", "", ""), "ana", ct);
        var empty = await service.SaveAsync(workspaceId, new CheckinInput(Now.UtcDateTime, CheckinPerception.OnTrack, "", "  ", "", "só observação"), "ana", ct);
        var past = await service.SaveAsync(workspaceId, new CheckinInput(Now.UtcDateTime.AddDays(-7), CheckinPerception.Frozen, "semana passada", "", "", ""), "ana", ct);

        Assert.Equal(CheckinSaveStatus.Invalid, future.Status);
        Assert.Equal(CheckinSaveStatus.Invalid, empty.Status);
        Assert.Equal(CheckinSaveStatus.Created, past.Status);
    }

    [Fact]
    public async Task Latest_PicksTheMostRecentWeek_PerWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var a = await SeedAsync(factory);
        var b = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        await service.SaveAsync(a, new CheckinInput(Now.UtcDateTime.AddDays(-7), CheckinPerception.AtRisk, "antiga", "", "", ""), "ana", ct);
        await service.SaveAsync(a, new CheckinInput(Now.UtcDateTime, CheckinPerception.OnTrack, "nova", "", "", ""), "ana", ct);

        var latest = await service.LatestByWorkspaceAsync([a, b], ct);

        Assert.Equal(CheckinPerception.OnTrack, latest[a].Perception);
        Assert.False(latest.ContainsKey(b));
    }

    [Fact]
    public async Task Draft_ListsTasksDoneThisWeek_AndInProgress()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var draft = await Service(factory).DraftAsync(workspaceId, Now.UtcDateTime, ct);

        Assert.Equal("- TASK-1 Feita esta semana (Ana)", draft.Finished);
        Assert.Equal("- TASK-3 Em andamento", draft.InProgress);
    }
}
