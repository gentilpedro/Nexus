using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Authorization;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers <see cref="ListViewLoader"/>, which replaced the ~12 sequential queries every list view
/// ran on navigation. Running them concurrently must not loosen the access check: the view's own
/// data still only loads for a member.
/// </summary>
public class ListViewLoaderTests
{
    private static ClaimsPrincipal User(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));

    private static (ListViewLoader Loader, IDbContextFactory<AppDbContext> Factory) Build()
    {
        var factory = TestDb.CreateFactory();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton(factory);
        services.AddScoped<IAuthorizationHandler, WorkspaceAuthorizationHandler>();
        services.AddScoped<WorkItemQueryService>();
        services.AddScoped<ListViewLoader>();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ListViewLoader>(), factory);
    }

    private static async Task<Guid> SeedListAsync(IDbContextFactory<AppDbContext> factory, string userId, WorkspaceRole role)
    {
        var workspaceId = Guid.NewGuid();
        var spaceId = Guid.NewGuid();
        var listId = Guid.NewGuid();

        await using var db = factory.CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, DisplayName = "Ana" });
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "W", Slug = $"w-{workspaceId:N}" });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
            JoinedAtUtc = DateTime.UtcNow,
        });
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = workspaceId, Name = "S" });
        db.TaskLists.Add(new TaskList { Id = listId, SpaceId = spaceId, Name = "L" });
        db.TaskStatusDefinitions.Add(new TaskStatusDefinition
        {
            Id = Guid.NewGuid(),
            TaskListId = listId,
            Name = "To Do",
            Category = StatusCategory.ToDo,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return listId;
    }

    [Fact]
    public async Task Member_GetsTheShellAndTheViewData()
    {
        var (loader, factory) = Build();
        var listId = await SeedListAsync(factory, "u1", WorkspaceRole.Member);
        var viewDataLoaded = false;

        var shell = await loader.LoadAsync(listId, User("u1"), () => { viewDataLoaded = true; return Task.CompletedTask; });

        Assert.NotNull(shell);
        Assert.True(viewDataLoaded);
        Assert.False(shell.CanManage);
        Assert.Equal("W", shell.TaskList.Space.Workspace.Name);
        Assert.Single(shell.Statuses);
        Assert.Equal(("u1", "Ana"), Assert.Single(shell.AssigneeOptions));
    }

    [Theory]
    [InlineData(WorkspaceRole.Owner)]
    [InlineData(WorkspaceRole.Admin)]
    public async Task AdminOrOwner_CanManage(WorkspaceRole role)
    {
        var (loader, factory) = Build();
        var listId = await SeedListAsync(factory, "u1", role);

        var shell = await loader.LoadAsync(listId, User("u1"), () => Task.CompletedTask);

        Assert.NotNull(shell);
        Assert.True(shell.CanManage);
    }

    [Fact]
    public async Task NonMember_GetsNothing_AndTheViewDataNeverLoads()
    {
        var (loader, factory) = Build();
        var listId = await SeedListAsync(factory, "u1", WorkspaceRole.Owner);
        var viewDataLoaded = false;

        var shell = await loader.LoadAsync(listId, User("intruso"), () => { viewDataLoaded = true; return Task.CompletedTask; });

        Assert.Null(shell);
        Assert.False(viewDataLoaded);
    }

    [Fact]
    public async Task UnknownList_GetsNothing()
    {
        var (loader, _) = Build();

        var shell = await loader.LoadAsync(Guid.NewGuid(), User("u1"), () => Task.CompletedTask);

        Assert.Null(shell);
    }
}
