using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers invite acceptance — the one anonymous-reachable flow that grants access to a
/// workspace, so its failure modes are privilege escalation rather than mere bugs.
/// </summary>
/// <remarks>
/// These behaviours existed and were correct before this audit; they are pinned here because H3
/// left them with no test at all, and every one of them is a security boundary.
/// </remarks>
public class WorkspaceInviteServiceTests
{
    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager() => Initialize("https://usenexus.runasp.net/", "https://usenexus.runasp.net/");
    }

    private static WorkspaceInviteService CreateService(IDbContextFactory<AppDbContext> factory)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = "https://usenexus.runasp.net" })
            .Build();

        var mailer = new BrevoMailer(Options.Create(new BrevoOptions()), NullLogger<BrevoMailer>.Instance);
        return new WorkspaceInviteService(factory, mailer, new PublicUrlBuilder(config, new FakeNavigationManager()));
    }

    private static async Task<(Guid WorkspaceId, string Token)> SeedInviteAsync(
        IDbContextFactory<AppDbContext> factory,
        string email,
        WorkspaceRole role = WorkspaceRole.Member,
        string? invitedBy = null,
        DateTime? expiresAt = null,
        DateTime? acceptedAt = null)
    {
        var workspaceId = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N");

        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "W", Slug = $"w-{workspaceId:N}" });
        db.WorkspaceInvites.Add(new WorkspaceInvite
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Email = email,
            Role = role,
            Token = token,
            InvitedByUserId = invitedBy,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAt ?? DateTime.UtcNow.AddDays(7),
            AcceptedAtUtc = acceptedAt,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (workspaceId, token);
    }

    [Fact]
    public async Task ValidInvite_IsAccepted_AndCreatesMembership()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (workspaceId, token) = await SeedInviteAsync(factory, "invitee@example.com");

        var result = await service.AcceptAsync(token, "user-1", "invitee@example.com");

        Assert.Equal(InviteAcceptResult.Accepted, result);

        await using var db = factory.CreateDbContext();
        Assert.True(await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == "user-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The invite is bound to the address it was sent to. Without this, anyone holding a leaked
    /// invite link (a forwarded e-mail, a shared screenshot) joins the workspace.
    /// </summary>
    [Fact]
    public async Task InviteForAnotherEmail_IsRejected()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (workspaceId, token) = await SeedInviteAsync(factory, "invitee@example.com");

        var result = await service.AcceptAsync(token, "attacker", "attacker@evil.com");

        Assert.Equal(InviteAcceptResult.EmailMismatch, result);

        await using var db = factory.CreateDbContext();
        Assert.False(await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmailComparison_IsCaseInsensitive()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (_, token) = await SeedInviteAsync(factory, "Invitee@Example.com");

        Assert.Equal(InviteAcceptResult.Accepted, await service.AcceptAsync(token, "user-1", "invitee@example.COM"));
    }

    [Fact]
    public async Task ExpiredInvite_IsRejected()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (_, token) = await SeedInviteAsync(factory, "invitee@example.com", expiresAt: DateTime.UtcNow.AddDays(-1));

        Assert.Equal(InviteAcceptResult.Expired, await service.AcceptAsync(token, "user-1", "invitee@example.com"));
    }

    /// <summary>
    /// An invite is single-use. Otherwise someone removed from a workspace could silently rejoin
    /// with the same old link.
    /// </summary>
    [Fact]
    public async Task AlreadyAcceptedInvite_CannotBeReused()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (_, token) = await SeedInviteAsync(factory, "invitee@example.com", acceptedAt: DateTime.UtcNow.AddHours(-1));

        Assert.Equal(InviteAcceptResult.Expired, await service.AcceptAsync(token, "user-1", "invitee@example.com"));
    }

    [Fact]
    public async Task UnknownToken_IsRejected()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        await SeedInviteAsync(factory, "invitee@example.com");

        Assert.Equal(InviteAcceptResult.NotFound, await service.AcceptAsync("not-a-real-token", "user-1", "invitee@example.com"));
    }

    /// <summary>
    /// Privilege escalation guard: an Admin invite is only honoured as Admin if the person who
    /// issued it still holds Admin/Owner. An invite can sit unaccepted for seven days.
    /// </summary>
    [Fact]
    public async Task AdminInviteFromSomeoneNoLongerAdmin_IsDowngradedToMember()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (workspaceId, token) = await SeedInviteAsync(
            factory, "invitee@example.com", WorkspaceRole.Admin, invitedBy: "ex-admin");

        // "ex-admin" is not a member of the workspace at all any more.
        await service.AcceptAsync(token, "user-1", "invitee@example.com");

        await using var db = factory.CreateDbContext();
        var member = await db.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == workspaceId && m.UserId == "user-1", TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceRole.Member, member.Role);
    }

    [Fact]
    public async Task AdminInviteFromAStillActiveAdmin_GrantsAdmin()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (workspaceId, token) = await SeedInviteAsync(
            factory, "invitee@example.com", WorkspaceRole.Admin, invitedBy: "boss");

        await using (var db = factory.CreateDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                UserId = "boss",
                Role = WorkspaceRole.Admin,
                JoinedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await service.AcceptAsync(token, "user-1", "invitee@example.com");

        await using var verify = factory.CreateDbContext();
        var member = await verify.WorkspaceMembers.SingleAsync(m => m.WorkspaceId == workspaceId && m.UserId == "user-1", TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceRole.Admin, member.Role);
    }

    [Fact]
    public async Task AcceptingWhenAlreadyAMember_DoesNotCreateADuplicate()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (workspaceId, token) = await SeedInviteAsync(factory, "invitee@example.com");

        await using (var db = factory.CreateDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                UserId = "user-1",
                Role = WorkspaceRole.Member,
                JoinedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(InviteAcceptResult.Accepted, await service.AcceptAsync(token, "user-1", "invitee@example.com"));

        await using var verify = factory.CreateDbContext();
        Assert.Equal(1, await verify.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId && m.UserId == "user-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcceptedInvite_IsMarkedSpent()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (_, token) = await SeedInviteAsync(factory, "invitee@example.com");

        await service.AcceptAsync(token, "user-1", "invitee@example.com");

        await using var db = factory.CreateDbContext();
        Assert.NotNull((await db.WorkspaceInvites.SingleAsync(i => i.Token == token, TestContext.Current.CancellationToken)).AcceptedAtUtc);
    }
}
