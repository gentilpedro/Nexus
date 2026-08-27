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

    /// <summary>Never used: BrevoMailer's constructor needs one, but FakeMailer overrides the only
    /// method that would call it.</summary>
    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("A test must not make HTTP calls.");
    }

    /// <summary>Records what would have been e-mailed instead of calling Brevo.</summary>
    private sealed class FakeMailer(bool succeeds = true)
        : BrevoMailer(Options.Create(new BrevoOptions()), new UnusedHttpClientFactory(), NullLogger<BrevoMailer>.Instance)
    {
        public List<(string To, string Subject, string Body, string? Text)> Sent { get; } = [];

        public override Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null)
        {
            Sent.Add((toEmail, subject, htmlBody, textBody));
            return Task.FromResult(succeeds);
        }
    }

    private static WorkspaceInviteService CreateService(IDbContextFactory<AppDbContext> factory)
        => CreateService(factory, new FakeMailer());

    private static WorkspaceInviteService CreateService(IDbContextFactory<AppDbContext> factory, BrevoMailer mailer)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = "https://usenexus.runasp.net" })
            .Build();

        return new WorkspaceInviteService(factory, mailer, new PublicUrlBuilder(config, new FakeNavigationManager()));
    }

    private static async Task<Guid> SeedWorkspaceAsync(IDbContextFactory<AppDbContext> factory)
    {
        var workspaceId = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "W", Slug = $"w-{workspaceId:N}" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return workspaceId;
    }

    private static async Task<ApplicationUser> SeedUserAsync(IDbContextFactory<AppDbContext> factory, string email, string? displayName = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName = displayName ?? "",
        };

        await using var db = factory.CreateDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
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

    /// <summary>
    /// The rule this whole flow exists for: having an account is not consent. Someone who already
    /// uses Nexus is invited, not enrolled — no WorkspaceMember row exists until they accept.
    /// </summary>
    [Fact]
    public async Task InvitingAnExistingUser_CreatesAPendingInvite_NotAMembership()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var workspaceId = await SeedWorkspaceAsync(factory);
        var user = await SeedUserAsync(factory, "ja-tem-conta@example.com");

        var outcome = await service.CreateAndSendAsync(workspaceId, "W", user.Email!, WorkspaceRole.Member, invitedByUserId: null);

        Assert.Equal(InviteSendStatus.Sent, outcome.Status);
        Assert.True(outcome.NotifiedInApp);

        await using var db = factory.CreateDbContext();
        Assert.False(await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId, TestContext.Current.CancellationToken));
        var invite = await db.WorkspaceInvites.SingleAsync(i => i.WorkspaceId == workspaceId, TestContext.Current.CancellationToken);
        Assert.Null(invite.AcceptedAtUtc);
    }

    /// <summary>An address that already has an account is also pinged inside the platform.</summary>
    [Fact]
    public async Task InvitingAnExistingUser_AlsoCreatesAnInAppNotification()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var workspaceId = await SeedWorkspaceAsync(factory);
        var inviter = await SeedUserAsync(factory, "chefe@example.com", "Pedro");
        var user = await SeedUserAsync(factory, "ja-tem-conta@example.com");

        await service.CreateAndSendAsync(workspaceId, "Marketing", user.Email!, WorkspaceRole.Member, inviter.Id);

        await using var db = factory.CreateDbContext();
        var notification = await db.Notifications.SingleAsync(n => n.UserId == user.Id, TestContext.Current.CancellationToken);
        var invite = await db.WorkspaceInvites.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(NotificationType.WorkspaceInvite, notification.Type);
        Assert.Contains("Pedro", notification.Message);
        Assert.Contains("Marketing", notification.Message);
        // Points at the accept page, not at the workspace — the recipient can't open the workspace yet.
        Assert.Equal($"/convite/{invite.Token}", notification.LinkUrl);
        Assert.Null(notification.WorkspaceId);
    }

    [Fact]
    public async Task InvitingAnAddressWithoutAnAccount_CreatesAnInviteAndNoNotification()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var workspaceId = await SeedWorkspaceAsync(factory);

        var outcome = await service.CreateAndSendAsync(workspaceId, "W", "novo@example.com", WorkspaceRole.Member, invitedByUserId: null);

        Assert.Equal(InviteSendStatus.Sent, outcome.Status);
        Assert.False(outcome.NotifiedInApp);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.WorkspaceInvites.CountAsync(i => i.WorkspaceId == workspaceId, TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.Notifications.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The invite e-mail is all an address without an account has; it must always go out.</summary>
    [Fact]
    public async Task InviteEmail_IsSentToTheInvitedAddress_WithTheAcceptLink()
    {
        var factory = TestDb.CreateFactory();
        var mailer = new FakeMailer();
        var service = CreateService(factory, mailer);
        var workspaceId = await SeedWorkspaceAsync(factory);

        await service.CreateAndSendAsync(workspaceId, "W", "novo@example.com", WorkspaceRole.Member, invitedByUserId: null);

        await using var db = factory.CreateDbContext();
        var token = (await db.WorkspaceInvites.SingleAsync(TestContext.Current.CancellationToken)).Token;

        var sent = Assert.Single(mailer.Sent);
        Assert.Equal("novo@example.com", sent.To);
        Assert.Contains($"https://usenexus.runasp.net/convite/{token}", sent.Body);
    }

    [Fact]
    public async Task InviteEmailFailure_StillLeavesThePendingInviteBehind()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory, new FakeMailer(succeeds: false));
        var workspaceId = await SeedWorkspaceAsync(factory);

        var outcome = await service.CreateAndSendAsync(workspaceId, "W", "novo@example.com", WorkspaceRole.Member, invitedByUserId: null);

        Assert.Equal(InviteSendStatus.SentWithoutEmail, outcome.Status);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.WorkspaceInvites.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvitingSomeoneWhoIsAlreadyAMember_IsRefused()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var workspaceId = await SeedWorkspaceAsync(factory);
        var user = await SeedUserAsync(factory, "membro@example.com");

        await using (var db = factory.CreateDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                UserId = user.Id,
                Role = WorkspaceRole.Member,
                JoinedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var outcome = await service.CreateAndSendAsync(workspaceId, "W", user.Email!, WorkspaceRole.Member, invitedByUserId: null);

        Assert.Equal(InviteSendStatus.AlreadyMember, outcome.Status);

        await using var verify = factory.CreateDbContext();
        Assert.Equal(0, await verify.WorkspaceInvites.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Re-inviting a pending address refreshes the existing row. Otherwise the members panel shows
    /// the same person as pending several times and every old token stays live.
    /// </summary>
    [Fact]
    public async Task ReInvitingAPendingAddress_RefreshesTheSameInvite()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var workspaceId = await SeedWorkspaceAsync(factory);

        await service.CreateAndSendAsync(workspaceId, "W", "novo@example.com", WorkspaceRole.Member, invitedByUserId: null);
        // Same address, different casing and a promoted role.
        var outcome = await service.CreateAndSendAsync(workspaceId, "W", "NOVO@example.com", WorkspaceRole.Admin, invitedByUserId: null);

        Assert.Equal(InviteSendStatus.Resent, outcome.Status);

        await using var db = factory.CreateDbContext();
        var invite = await db.WorkspaceInvites.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkspaceRole.Admin, invite.Role);
        Assert.True(invite.ExpiresAtUtc > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task GetPendingAsync_ListsOnlyUnacceptedInvitesOfThatWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (workspaceId, _) = await SeedInviteAsync(factory, "pendente@example.com");
        await SeedInviteAsync(factory, "outro-workspace@example.com");

        await using (var db = factory.CreateDbContext())
        {
            db.WorkspaceInvites.Add(new WorkspaceInvite
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Email = "ja-aceitou@example.com",
                Token = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                AcceptedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var pending = await service.GetPendingAsync(workspaceId);

        Assert.Equal("pendente@example.com", Assert.Single(pending).Email);
    }

    [Fact]
    public async Task CancelAsync_RemovesThePendingInvite()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (workspaceId, token) = await SeedInviteAsync(factory, "pendente@example.com");

        Guid inviteId;
        await using (var db = factory.CreateDbContext())
        {
            inviteId = (await db.WorkspaceInvites.SingleAsync(i => i.Token == token, TestContext.Current.CancellationToken)).Id;
        }

        Assert.True(await service.CancelAsync(inviteId, workspaceId));
        Assert.Equal(InviteAcceptResult.NotFound, await service.AcceptAsync(token, "user-1", "pendente@example.com"));
    }

    /// <summary>
    /// An invite id alone is not authority to cancel: the caller's workspace has to match, or an
    /// admin of workspace A could withdraw workspace B's invites.
    /// </summary>
    [Fact]
    public async Task CancelAsync_IgnoresInvitesFromAnotherWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var service = CreateService(factory);
        var (_, token) = await SeedInviteAsync(factory, "pendente@example.com");

        Guid inviteId;
        await using (var db = factory.CreateDbContext())
        {
            inviteId = (await db.WorkspaceInvites.SingleAsync(i => i.Token == token, TestContext.Current.CancellationToken)).Id;
        }

        Assert.False(await service.CancelAsync(inviteId, Guid.NewGuid()));
        Assert.Equal(InviteAcceptResult.Accepted, await service.AcceptAsync(token, "user-1", "pendente@example.com"));
    }
}
