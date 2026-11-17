using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;
using Nexus.Web.Services.TimeTracking;

namespace Nexus.Web.Tests;

public class TimeTrackingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    private sealed record Seed(Guid WorkspaceId, Guid ListId, Guid TaskId);

    private static async Task<Seed> SeedAsync(IDbContextFactory<AppDbContext> factory)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = factory.CreateDbContext();
        if (!await db.Users.AnyAsync())
        {
            db.Users.AddRange(
                new ApplicationUser { Id = "ana", UserName = "ana", Email = "ana@acme.com.br", DisplayName = "Ana" },
                new ApplicationUser { Id = "bia", UserName = "bia", Email = "bia@acme.com.br", DisplayName = "Bia" },
                new ApplicationUser { Id = "fora", UserName = "fora", Email = "fora@x.com", DisplayName = "Fora" });
        }

        db.Workspaces.Add(new Workspace { Id = seed.WorkspaceId, Name = "Acme", Slug = "acme", CreatedAtUtc = DateTime.UtcNow });
        db.WorkspaceMembers.AddRange(
            new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = seed.WorkspaceId, UserId = "ana", Role = WorkspaceRole.Owner, JoinedAtUtc = DateTime.UtcNow },
            new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = seed.WorkspaceId, UserId = "bia", Role = WorkspaceRole.Member, JoinedAtUtc = DateTime.UtcNow });
        var spaceId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = seed.WorkspaceId, Name = "Operações" });
        db.TaskLists.Add(new TaskList { Id = seed.ListId, SpaceId = spaceId, Name = "Pedidos" });
        db.TaskStatusDefinitions.Add(new TaskStatusDefinition { Id = statusId, TaskListId = seed.ListId, Name = "A fazer", Category = StatusCategory.ToDo, IsDefault = true });
        db.WorkItems.Add(new WorkItem { Id = seed.TaskId, Number = 1, TaskListId = seed.ListId, Title = "Pedido 1001", StatusId = statusId, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return seed;
    }

    private static (TimeTrackingService Time, ContractService Contracts, FakeTimeProvider Clock) Services(IDbContextFactory<AppDbContext> factory)
    {
        var clock = new FakeTimeProvider(Now);
        var contracts = new ContractService(factory, new AuditLogService(factory), clock);
        return (new TimeTrackingService(factory, contracts, new AuditLogService(factory), clock), contracts, clock);
    }

    private static DateTime Day(int day) => new(2026, 11, day);

    [Fact]
    public async Task Log_RoundsHours_StoresTheDateAsUtcMidnight_AndSeedsDefaultActivities()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, _, _) = Services(factory);
        var activities = await time.ListActivitiesAsync(seed.WorkspaceId, ct: ct);

        var result = await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 1.456m, seed.TaskId, activities[0].Id, Comment: " ajuste "), ct: ct);

        Assert.Equal(TimeEntryStatus.Ok, result.Status);
        Assert.Equal(TimeActivity.DefaultNames, activities.Select(a => a.Name));
        var entry = result.Entry!;
        Assert.Equal(1.46m, entry.Hours);
        Assert.Equal(DateTimeKind.Utc, entry.Date.Kind);
        Assert.Equal(new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), entry.Date);
        Assert.Equal("ajuste", entry.Comment);
        Assert.Equal("Desenvolvimento", entry.Activity!.Name);
        Assert.Equal("TASK-1", entry.WorkItem!.Code);
        Assert.Null(entry.ContractId);
        Assert.Equal(1.46m, await time.TotalForTaskAsync(seed.TaskId, ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public async Task Log_RefusesHoursOutOfRange(double hours)
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, _, _) = Services(factory);

        var result = await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), (decimal)hours), ct: ct);

        Assert.Equal(TimeEntryStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Log_RefusesNonMembers_FarDates_AndTasksOfOtherWorkspaces()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var other = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, _, _) = Services(factory);

        Assert.Equal(TimeEntryStatus.Forbidden, (await time.LogAsync(seed.WorkspaceId, "fora", new TimeEntryInput(Day(16), 1), ct: ct)).Status);
        Assert.Equal(TimeEntryStatus.Invalid, (await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16).AddDays(5), 1), ct: ct)).Status);
        Assert.Equal(TimeEntryStatus.Invalid, (await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16).AddYears(-2), 1), ct: ct)).Status);
        Assert.Equal(TimeEntryStatus.Invalid, (await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 1, other.TaskId), ct: ct)).Status);
    }

    [Fact]
    public async Task Contracts_OneActiveLinksAutomatically_TwoRequireAChoice_LockedIsRefused()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, contracts, _) = Services(factory);

        var a = (await contracts.CreateAsync(seed.WorkspaceId, new ContractInput("Suporte 2026", ContractType.Hourly, 100, null, null, null), "ana", ct)).Contract!;
        var one = await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 2), ct: ct);
        Assert.Equal(a.Id, one.Entry!.ContractId);

        var b = (await contracts.CreateAsync(seed.WorkspaceId, new ContractInput("Projeto X", ContractType.Fixed, null, null, null, null), "ana", ct)).Contract!;
        var ambiguous = await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 2), ct: ct);
        Assert.Equal(TimeEntryStatus.Invalid, ambiguous.Status);
        Assert.Contains("mais de um contrato", ambiguous.Error);
        var chosen = await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 2, ContractId: b.Id), ct: ct);
        Assert.Equal(b.Id, chosen.Entry!.ContractId);

        await contracts.SetStatusAsync(seed.WorkspaceId, b.Id, ContractStatus.Locked, "ana", ct);
        var locked = await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 1, ContractId: b.Id), ct: ct);
        Assert.Equal(TimeEntryStatus.Invalid, locked.Status);
        Assert.Contains("travado", locked.Error);

        // Com o B travado só sobra o A ativo: volta a vincular sozinho.
        var again = await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 1), ct: ct);
        Assert.Equal(a.Id, again.Entry!.ContractId);

        var usage = await contracts.ListAsync(seed.WorkspaceId, ct);
        Assert.Equal(3m, usage.Single(u => u.Contract.Id == a.Id).HoursLogged);
        Assert.Equal(97m, usage.Single(u => u.Contract.Id == a.Id).Remaining);
    }

    [Fact]
    public async Task Contract_At80Percent_NotifiesOwnersAndAdminsOnce()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, contracts, _) = Services(factory);
        var c = (await contracts.CreateAsync(seed.WorkspaceId, new ContractInput("Suporte", ContractType.Hourly, 10, null, null, null), "ana", ct)).Contract!;

        await time.LogAsync(seed.WorkspaceId, "bia", new TimeEntryInput(Day(16), 7.5m), ct: ct);
        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(0, await db.Notifications.CountAsync(ct));
        }

        await time.LogAsync(seed.WorkspaceId, "bia", new TimeEntryInput(Day(16), 0.5m), ct: ct);
        await time.LogAsync(seed.WorkspaceId, "bia", new TimeEntryInput(Day(17), 1m), ct: ct);

        await using var check = factory.CreateDbContext();
        var notification = await check.Notifications.SingleAsync(ct);
        Assert.Equal("ana", notification.UserId);
        Assert.Equal(NotificationType.ContractAlmostUsed, notification.Type);
        Assert.Contains("80%", notification.Message);
        Assert.NotNull((await check.WorkspaceContracts.SingleAsync(ct)).WarningNotifiedAtUtc);
        Assert.True((await contracts.ListAsync(seed.WorkspaceId, ct)).Single().IsNearLimit);
    }

    [Fact]
    public async Task Timer_StartStop_CreatesAnEntryWithWholeMinutes_AndOnlyOneTimerPerPerson()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, _, clock) = Services(factory);

        var started = await time.StartAsync("ana", seed.WorkspaceId, seed.TaskId, null, "ajustando", ct);
        Assert.NotNull(started.Timer);
        var second = await time.StartAsync("ana", seed.WorkspaceId, null, null, null, ct);
        Assert.Null(second.Timer);
        Assert.Equal(started.Timer.Id, second.AlreadyRunning!.Id);

        clock.Advance(TimeSpan.FromMinutes(7) + TimeSpan.FromSeconds(40));
        var stopped = await time.StopAsync("ana", ct: ct);

        Assert.Equal(TimeEntryStatus.Ok, stopped.Status);
        Assert.Equal(0.13m, stopped.Entry!.Hours);
        Assert.Equal(TimeEntrySource.Timer, stopped.Entry.Source);
        Assert.Equal(seed.TaskId, stopped.Entry.WorkItemId);
        Assert.Equal("ajustando", stopped.Entry.Comment);
        Assert.Null(await time.GetRunningAsync("ana", ct));
        Assert.Equal(TimeEntryStatus.NotFound, (await time.StopAsync("ana", ct: ct)).Status);
        Assert.Equal(0.02m, TimeTrackingService.HoursBetween(Now.UtcDateTime, Now.UtcDateTime.AddSeconds(5)));
    }

    [Fact]
    public async Task Update_AndDelete_AreLimitedToTheOwnerOrAnAdmin()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, _, _) = Services(factory);
        var entry = (await time.LogAsync(seed.WorkspaceId, "bia", new TimeEntryInput(Day(16), 2), ct: ct)).Entry!;

        Assert.Equal(TimeEntryStatus.Forbidden, (await time.UpdateAsync(seed.WorkspaceId, entry.Id, "ana", false, new TimeEntryInput(Day(16), 3), ct)).Status);
        Assert.Equal(TimeEntryStatus.Ok, (await time.UpdateAsync(seed.WorkspaceId, entry.Id, "bia", false, new TimeEntryInput(Day(15), 3, Comment: "revisado"), ct)).Status);
        var mine = await time.ListAsync(seed.WorkspaceId, "bia", Day(1), Day(30), ct);
        Assert.Equal(3m, Assert.Single(mine).Hours);
        Assert.Equal(TimeEntryStatus.Forbidden, await time.DeleteAsync(seed.WorkspaceId, entry.Id, "ana", false, ct));
        Assert.Equal(TimeEntryStatus.Ok, await time.DeleteAsync(seed.WorkspaceId, entry.Id, "ana", true, ct));
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.TimeEntryDeletedByAdmin, ct));
    }

    [Fact]
    public async Task Report_GroupsByPersonListAndContract_WithTotals()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var (time, contracts, _) = Services(factory);
        var c = (await contracts.CreateAsync(seed.WorkspaceId, new ContractInput("Suporte", ContractType.Hourly, 50, null, null, null), "ana", ct)).Contract!;
        await time.LogAsync(seed.WorkspaceId, "ana", new TimeEntryInput(Day(16), 2, seed.TaskId), ct: ct);
        await time.LogAsync(seed.WorkspaceId, "bia", new TimeEntryInput(Day(16), 1.5m), ct: ct);
        await time.LogAsync(seed.WorkspaceId, "bia", new TimeEntryInput(Day(3), 4), ct: ct);

        var byPerson = await time.ReportAsync(seed.WorkspaceId, Day(10), Day(20), TimeReportGroup.Person, ct: ct);
        var byList = await time.ReportAsync(seed.WorkspaceId, Day(10), Day(20), TimeReportGroup.List, ct: ct);
        var byContract = await time.ReportAsync(seed.WorkspaceId, Day(1), Day(30), TimeReportGroup.Contract, ct: ct);
        var onlyBia = await time.ReportAsync(seed.WorkspaceId, Day(1), Day(30), TimeReportGroup.Day, "bia", ct);

        Assert.Equal(3.5m, byPerson.TotalHours);
        Assert.Equal(["Ana", "Bia"], byPerson.Rows.Select(r => r.Label));
        Assert.Equal(["Pedidos", "Avulso (sem tarefa)"], byList.Rows.Select(r => r.Label));
        Assert.Equal("Suporte", Assert.Single(byContract.Rows).Label);
        Assert.Equal(7.5m, byContract.TotalHours);
        Assert.Equal(5.5m, onlyBia.TotalHours);
        _ = c;
    }
}
