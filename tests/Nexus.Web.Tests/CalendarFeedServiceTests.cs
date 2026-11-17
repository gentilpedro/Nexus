using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

public class CalendarFeedServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string Base = "https://nexus.test";

    private sealed record Seed(Guid ListId, Guid TodoId, Guid DoneId);

    private static async Task<Seed> SeedAsync(IDbContextFactory<AppDbContext> factory)
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = factory.CreateDbContext();
        db.Users.AddRange(
            new ApplicationUser { Id = "ana", UserName = "ana", Email = "ana@acme.com.br", DisplayName = "Ana" },
            new ApplicationUser { Id = "bia", UserName = "bia", Email = "bia@acme.com.br", DisplayName = "Bia" });
        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Acme; Ltda", Slug = "acme", CreatedAtUtc = DateTime.UtcNow });
        var spaceId = Guid.NewGuid();
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = workspaceId, Name = "Operações" });
        db.TaskLists.Add(new TaskList { Id = seed.ListId, SpaceId = spaceId, Name = "Pedidos" });
        db.TaskStatusDefinitions.AddRange(
            new TaskStatusDefinition { Id = seed.TodoId, TaskListId = seed.ListId, Name = "A fazer", Category = StatusCategory.ToDo, IsDefault = true },
            new TaskStatusDefinition { Id = seed.DoneId, TaskListId = seed.ListId, Name = "Feito", Category = StatusCategory.Done, SortOrder = 1 });
        await db.SaveChangesAsync();
        return seed;
    }

    private static CalendarFeedService Service(IDbContextFactory<AppDbContext> factory) =>
        new(factory, new EphemeralDataProtectionProvider(), new FakeTimeProvider(Now));

    private static WorkItem Task(Seed seed, int number, string title, string? assignee, DateTime? due, DateTime? start = null, Guid? statusId = null, DateTime? updated = null) => new()
    {
        Id = Guid.NewGuid(), Number = number, TaskListId = seed.ListId, Title = title, AssigneeId = assignee,
        DueDateUtc = due, StartDateUtc = start, StatusId = statusId ?? seed.TodoId, CreatedAtUtc = Now.UtcDateTime.AddDays(-10), UpdatedAtUtc = updated,
    };

    [Fact]
    public async Task Regenerate_StoresHashAndProtectedToken_AndGetReturnsTheSameUrl()
    {
        var factory = TestDb.CreateFactory();
        await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        Assert.Null(await service.GetAsync("ana", Base, ct));
        var created = await service.RegenerateAsync("ana", Base, ct);
        var read = await service.GetAsync("ana", Base, ct);

        Assert.StartsWith(Base + "/calendar/nxc_", created.Url);
        Assert.EndsWith(".ics", created.Url);
        Assert.Equal(created.Url, read!.Url);
        await using var db = factory.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == "ana", ct);
        var token = created.Url.Split('/')[^1][..^4];
        Assert.Equal(CalendarFeedService.Hash(token), user.CalendarFeedTokenHash);
        Assert.DoesNotContain(token, user.CalendarFeedTokenProtected);
    }

    [Fact]
    public async Task Build_ReturnsOnlyDatedTasksAssignedToTheOwner_AndNullForUnknownTokens()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var feed = await service.RegenerateAsync("ana", Base, ct);
        var token = feed.Url.Split('/')[^1][..^4];
        await using (var db = factory.CreateDbContext())
        {
            db.WorkItems.AddRange(
                Task(seed, 1, "Com prazo", "ana", new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc)),
                Task(seed, 2, "Sem data", "ana", null),
                Task(seed, 3, "De outra pessoa", "bia", new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc)),
                Task(seed, 4, "Feita recentemente", "ana", new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), statusId: seed.DoneId, updated: Now.UtcDateTime.AddDays(-5)),
                Task(seed, 5, "Feita há muito", "ana", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), statusId: seed.DoneId, updated: Now.UtcDateTime.AddDays(-60)),
                Task(seed, 6, "Só início", "ana", null, start: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync(ct);
        }

        var ics = await service.BuildAsync(token, Base, ct);

        Assert.NotNull(ics);
        Assert.Contains("SUMMARY:TASK-1 Com prazo", ics);
        Assert.Contains("SUMMARY:✓ TASK-4 Feita recentemente", ics);
        Assert.Contains("SUMMARY:TASK-6 Só início", ics);
        Assert.DoesNotContain("Sem data", ics);
        Assert.DoesNotContain("De outra pessoa", ics);
        Assert.DoesNotContain("Feita há muito", ics);
        Assert.Equal(3, ics.Split("BEGIN:VEVENT").Length - 1);
        Assert.Null(await service.BuildAsync("nxc_" + new string('x', 43), Base, ct));
        Assert.Null(await service.BuildAsync("", Base, ct));
        Assert.Null(await service.BuildAsync(token.Replace("nxc_", "abc_"), Base, ct));
    }

    [Fact]
    public async Task Build_WritesAllDayEvents_WithExclusiveEnd_EscapedText_AndFoldedLines()
    {
        var factory = TestDb.CreateFactory();
        var seed = await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var feed = await service.RegenerateAsync("ana", Base, ct);
        var token = feed.Url.Split('/')[^1][..^4];
        var longTitle = string.Concat(Enumerable.Repeat("Pedido muito longo com detalhes, ", 5));
        await using (var db = factory.CreateDbContext())
        {
            db.WorkItems.Add(Task(seed, 7, longTitle, "ana", new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc), start: new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync(ct);
        }

        var ics = await service.BuildAsync(token, Base, ct);

        Assert.NotNull(ics);
        Assert.Contains("\r\nDTSTART;VALUE=DATE:20261123\r\n", ics);
        Assert.Contains("\r\nDTEND;VALUE=DATE:20261126\r\n", ics);
        Assert.Contains("DESCRIPTION:Acme\\; Ltda · Pedidos · A fazer", ics);
        Assert.Contains($"URL:{Base}/lists/{seed.ListId}/list", ics);
        Assert.Contains("X-WR-CALNAME:Nexus · Minhas tarefas", ics);
        Assert.All(ics.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.Contains("\r\n ", ics);
        Assert.Contains("TASK-7 " + longTitle.Replace(",", "\\,").Substring(0, 30), ics.Replace("\r\n ", ""));
    }

    [Fact]
    public async Task Regenerate_InvalidatesTheOldToken_AndDisableRemovesIt()
    {
        var factory = TestDb.CreateFactory();
        await SeedAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var first = await service.RegenerateAsync("ana", Base, ct);
        var firstToken = first.Url.Split('/')[^1][..^4];

        var second = await service.RegenerateAsync("ana", Base, ct);
        var secondToken = second.Url.Split('/')[^1][..^4];

        Assert.NotEqual(firstToken, secondToken);
        Assert.Null(await service.BuildAsync(firstToken, Base, ct));
        Assert.NotNull(await service.BuildAsync(secondToken, Base, ct));

        await service.DisableAsync("ana", ct);
        Assert.Null(await service.BuildAsync(secondToken, Base, ct));
        Assert.Null(await service.GetAsync("ana", Base, ct));
    }
}
