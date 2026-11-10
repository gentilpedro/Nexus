using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Authorization;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Isolation of private conversations against a real database: who can open which conversation,
/// and that two people never end up with two direct conversations between them.
/// </summary>
/// <remarks>
/// Roda só com <c>NEXUS_TEST_POSTGRES</c> apontando para um banco já migrado, como
/// <see cref="DocCollabPostgresTests"/>; sem a variável, é pulado. Cria um workspace próprio e o
/// apaga no fim.
/// </remarks>
public class ChatConversationPostgresTests
{
    private const string ConnectionVariable = "NEXUS_TEST_POSTGRES";

    private sealed class NpgsqlFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public NpgsqlFactory Factory { get; }
        public Guid WorkspaceId { get; } = Guid.NewGuid();
        public ApplicationUser Owner { get; }
        public ApplicationUser Member { get; }
        public ApplicationUser Bystander { get; }
        public ApplicationUser Outsider { get; }

        private Fixture(string connection)
        {
            Factory = new NpgsqlFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
            var run = WorkspaceId.ToString("N")[..8];
            Owner = NewUser($"conv-{run}-owner");
            Member = NewUser($"conv-{run}-member");
            Bystander = NewUser($"conv-{run}-bystander");
            Outsider = NewUser($"conv-{run}-outsider");
        }

        public static async Task<Fixture> CreateAsync(string connection, CancellationToken ct)
        {
            var fixture = new Fixture(connection);
            await using var db = fixture.Factory.CreateDbContext();

            db.Workspaces.Add(new Workspace
            {
                Id = fixture.WorkspaceId,
                Name = "Teste de conversas",
                Slug = "conversas-" + fixture.WorkspaceId.ToString("N")[..8],
                CreatedAtUtc = DateTime.UtcNow
            });
            db.ChatConversations.Add(ChatConversationService.NewWorkspaceConversation(fixture.WorkspaceId, DateTime.UtcNow));
            db.Users.AddRange(fixture.Owner, fixture.Member, fixture.Bystander, fixture.Outsider);

            foreach (var (user, role) in new[] { (fixture.Owner, WorkspaceRole.Owner), (fixture.Member, WorkspaceRole.Member), (fixture.Bystander, WorkspaceRole.Member) })
            {
                db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = fixture.WorkspaceId, UserId = user.Id, Role = role, JoinedAtUtc = DateTime.UtcNow });
            }

            await db.SaveChangesAsync(ct);
            return fixture;
        }

        private static ApplicationUser NewUser(string handle)
        {
            var email = $"{handle}@example.com";
            return new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(), DisplayName = handle };
        }

        public async ValueTask DisposeAsync()
        {
            await using var cleanup = Factory.CreateDbContext();
            await cleanup.Workspaces.Where(w => w.Id == WorkspaceId).ExecuteDeleteAsync(CancellationToken.None);
            var ids = new[] { Owner.Id, Member.Id, Bystander.Id, Outsider.Id };
            await cleanup.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    private static string? Connection()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Defina {ConnectionVariable} para rodar contra um PostgreSQL real.");
        return connection;
    }

    private static async Task<bool> CanOpenAsync(NpgsqlFactory factory, ApplicationUser user, Guid conversationId)
    {
        var handler = new ConversationAuthorizationHandler(factory);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
        var context = new AuthorizationHandlerContext([ConversationAccessRequirement.Instance], principal, conversationId);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task DirectConversation_IsVisibleOnlyToItsTwoParticipants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(Connection()!, ct);
        var service = new ChatConversationService(fixture.Factory);

        var direct = await service.GetOrCreateDirectAsync(fixture.WorkspaceId, fixture.Owner.Id, fixture.Member.Id);
        Assert.NotNull(direct);

        Assert.True(await CanOpenAsync(fixture.Factory, fixture.Owner, direct.Value));
        Assert.True(await CanOpenAsync(fixture.Factory, fixture.Member, direct.Value));

        // Same workspace, not in the conversation — this is the whole point of the feature.
        Assert.False(await CanOpenAsync(fixture.Factory, fixture.Bystander, direct.Value));
        Assert.False(await CanOpenAsync(fixture.Factory, fixture.Outsider, direct.Value));
    }

    [Fact]
    public async Task WorkspaceConversation_IsVisibleToEveryMemberAndNobodyElse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(Connection()!, ct);
        var service = new ChatConversationService(fixture.Factory);

        var shared = await service.EnsureWorkspaceConversationAsync(fixture.WorkspaceId);

        Assert.True(await CanOpenAsync(fixture.Factory, fixture.Bystander, shared));
        Assert.False(await CanOpenAsync(fixture.Factory, fixture.Outsider, shared));
    }

    [Fact]
    public async Task DirectConversation_IsReusedWhicheverSideOpensIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(Connection()!, ct);
        var service = new ChatConversationService(fixture.Factory);

        var first = await service.GetOrCreateDirectAsync(fixture.WorkspaceId, fixture.Owner.Id, fixture.Member.Id);
        var second = await service.GetOrCreateDirectAsync(fixture.WorkspaceId, fixture.Member.Id, fixture.Owner.Id);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task DirectConversation_RefusesSomeoneOutsideTheWorkspace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(Connection()!, ct);
        var service = new ChatConversationService(fixture.Factory);

        Assert.Null(await service.GetOrCreateDirectAsync(fixture.WorkspaceId, fixture.Owner.Id, fixture.Outsider.Id));
    }

    [Fact]
    public async Task Group_LeavingRevokesAccessAndKeepsItForTheOthers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(Connection()!, ct);
        var service = new ChatConversationService(fixture.Factory);

        var group = await service.CreateGroupAsync(fixture.WorkspaceId, fixture.Owner.Id, "Squad", [fixture.Member.Id]);
        Assert.NotNull(group);
        Assert.False(await CanOpenAsync(fixture.Factory, fixture.Bystander, group.Value));

        Assert.True(await service.AddMembersAsync(group.Value, fixture.Member.Id, [fixture.Bystander.Id]));
        Assert.True(await CanOpenAsync(fixture.Factory, fixture.Bystander, group.Value));

        Assert.True(await service.LeaveGroupAsync(group.Value, fixture.Member.Id));
        Assert.False(await CanOpenAsync(fixture.Factory, fixture.Member, group.Value));
        Assert.True(await CanOpenAsync(fixture.Factory, fixture.Owner, group.Value));
    }

    [Fact]
    public async Task Group_CannotBeExtendedByANonMember()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await Fixture.CreateAsync(Connection()!, ct);
        var service = new ChatConversationService(fixture.Factory);

        var group = await service.CreateGroupAsync(fixture.WorkspaceId, fixture.Owner.Id, "Squad", [fixture.Member.Id]);

        Assert.False(await service.AddMembersAsync(group!.Value, fixture.Bystander.Id, [fixture.Bystander.Id]));
        Assert.False(await service.AddMembersAsync(group.Value, fixture.Owner.Id, [fixture.Outsider.Id]));
    }
}
