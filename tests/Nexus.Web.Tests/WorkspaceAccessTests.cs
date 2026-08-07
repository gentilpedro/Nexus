using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Domain.Entities;
using Nexus.Web.Authorization;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the workspace RBAC handler and the H6 fix (authorization re-checked at write time
/// instead of only when the page first rendered).
/// </summary>
public class WorkspaceAccessTests
{
    private static ClaimsPrincipal User(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));

    private static (WorkspaceAccessGuard Guard, Microsoft.EntityFrameworkCore.IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> Factory) BuildGuard()
    {
        var factory = TestDb.CreateFactory();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton(factory);
        services.AddScoped<IAuthorizationHandler, WorkspaceAuthorizationHandler>();
        services.AddScoped<WorkspaceAccessGuard>();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<WorkspaceAccessGuard>(), factory);
    }

    private static Task<AuthenticationState> AuthState(string userId) =>
        Task.FromResult(new AuthenticationState(User(userId)));

    private static async Task SeedMemberAsync(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> factory,
        Guid workspaceId,
        string userId,
        WorkspaceRole role)
    {
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "W", Slug = $"w-{workspaceId:N}" });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
            JoinedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Member_HasMemberAccess()
    {
        var (guard, factory) = BuildGuard();
        var ws = Guid.NewGuid();
        await SeedMemberAsync(factory, ws, "u1", WorkspaceRole.Member);

        Assert.True(await guard.HasAccessAsync(AuthState("u1"), ws, WorkspaceRole.Member));
    }

    // Lower enum value = higher privilege (Owner=0, Admin=1, Member=2).
    [Theory]
    [InlineData(WorkspaceRole.Owner, WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Owner, WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Admin, WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Member, WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Member, WorkspaceRole.Admin, false)]
    public async Task RoleHierarchy_IsEnforced(WorkspaceRole actual, WorkspaceRole required, bool expected)
    {
        var (guard, factory) = BuildGuard();
        var ws = Guid.NewGuid();
        await SeedMemberAsync(factory, ws, "u1", actual);

        Assert.Equal(expected, await guard.HasAccessAsync(AuthState("u1"), ws, required));
    }

    /// <summary>
    /// The core IDOR check: a user who is not a member of the workspace gets nothing, no matter
    /// what workspace id they present.
    /// </summary>
    [Fact]
    public async Task NonMember_IsDenied()
    {
        var (guard, factory) = BuildGuard();
        var ws = Guid.NewGuid();
        await SeedMemberAsync(factory, ws, "owner", WorkspaceRole.Owner);

        Assert.False(await guard.HasAccessAsync(AuthState("outsider"), ws, WorkspaceRole.Member));
    }

    [Fact]
    public async Task UnknownWorkspaceId_IsDenied()
    {
        var (guard, factory) = BuildGuard();
        await SeedMemberAsync(factory, Guid.NewGuid(), "u1", WorkspaceRole.Owner);

        Assert.False(await guard.HasAccessAsync(AuthState("u1"), Guid.NewGuid(), WorkspaceRole.Member));
    }

    [Fact]
    public async Task AnonymousPrincipal_IsDenied()
    {
        var (guard, _) = BuildGuard();

        var anonymous = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.False(await guard.HasAccessAsync(anonymous, Guid.NewGuid(), WorkspaceRole.Member));
    }

    [Fact]
    public async Task NullAuthState_IsDenied()
    {
        var (guard, _) = BuildGuard();

        Assert.False(await guard.HasAccessAsync(null, Guid.NewGuid(), WorkspaceRole.Member));
    }

    /// <summary>
    /// H6 proper: the guard must observe a membership change that happens *after* the page
    /// authorized. This is what stops a removed member from continuing to write through a
    /// still-open Blazor circuit.
    /// </summary>
    [Fact]
    public async Task RemovedMember_LosesAccessImmediately()
    {
        var (guard, factory) = BuildGuard();
        var ws = Guid.NewGuid();
        await SeedMemberAsync(factory, ws, "u1", WorkspaceRole.Member);

        // Page load: access granted.
        Assert.True(await guard.HasAccessAsync(AuthState("u1"), ws, WorkspaceRole.Member));

        // Owner removes the user while their circuit is still open.
        await using (var db = factory.CreateDbContext())
        {
            db.WorkspaceMembers.RemoveRange(db.WorkspaceMembers.Where(m => m.UserId == "u1"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Write time: must now be denied.
        Assert.False(await guard.HasAccessAsync(AuthState("u1"), ws, WorkspaceRole.Member));
    }

    [Fact]
    public async Task DemotedAdmin_LosesAdminAccessImmediately()
    {
        var (guard, factory) = BuildGuard();
        var ws = Guid.NewGuid();
        await SeedMemberAsync(factory, ws, "u1", WorkspaceRole.Admin);

        Assert.True(await guard.HasAccessAsync(AuthState("u1"), ws, WorkspaceRole.Admin));

        await using (var db = factory.CreateDbContext())
        {
            var member = db.WorkspaceMembers.Single(m => m.UserId == "u1");
            member.Role = WorkspaceRole.Member;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.False(await guard.HasAccessAsync(AuthState("u1"), ws, WorkspaceRole.Admin));
        Assert.True(await guard.HasAccessAsync(AuthState("u1"), ws, WorkspaceRole.Member));
    }
}
