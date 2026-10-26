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
/// Vários aceites de convite ao mesmo tempo num workspace Starter com quatro membros, cada um na
/// própria conexão. Sem a trava da linha do workspace, todos veriam quatro membros e todos
/// entrariam; com ela, só um ocupa a última vaga (#52).
/// </summary>
/// <remarks>
/// Roda só com <c>NEXUS_TEST_POSTGRES</c> apontando para um banco já migrado, como
/// <see cref="DocCollabPostgresTests"/>; sem a variável, é pulado. Cria um workspace próprio e o
/// apaga no fim.
/// </remarks>
public class StarterLimitsPostgresTests
{
    private const string ConnectionVariable = "NEXUS_TEST_POSTGRES";

    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager() => Initialize("https://nexus.test/", "https://nexus.test/");
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("A test must not make HTTP calls.");
    }

    private sealed class SilentMailer()
        : BrevoMailer(Options.Create(new BrevoOptions()), new UnusedHttpClientFactory(), NullLogger<BrevoMailer>.Instance)
    {
        public override Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null) => Task.FromResult(true);
    }

    private sealed class NpgsqlFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task ConcurrentAccepts_FillOnlyTheLastStarterSeat()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Defina {ConnectionVariable} para rodar contra um PostgreSQL real.");

        var factory = new NpgsqlFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = "https://nexus.test" })
            .Build();
        var service = new WorkspaceInviteService(factory, new SilentMailer(), new PublicUrlBuilder(config, new FakeNavigationManager()),
            new WorkspacePlanService(factory, options: Options.Create(new PlanOptions { EnforceLimits = true })));

        var workspaceId = Guid.NewGuid();
        var run = workspaceId.ToString("N")[..8];
        const int contenders = 6;
        var users = new List<ApplicationUser>();
        var tokens = new List<string>();
        var ct = TestContext.Current.CancellationToken;

        await using (var db = factory.CreateDbContext())
        {
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                Name = "Teste de limite",
                Slug = "limite-" + run,
                CreatedAtUtc = DateTime.UtcNow,
                Subscription = WorkspaceSubscription.StartStarter(workspaceId, DateTime.UtcNow)
            });

            for (var i = 0; i < 4 + contenders; i++)
            {
                var email = $"limite-{run}-{i}@example.com";
                var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(), DisplayName = email };
                users.Add(user);
                db.Users.Add(user);
            }

            // Four members already: one seat left.
            for (var i = 0; i < 4; i++)
            {
                db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = users[i].Id, Role = i == 0 ? WorkspaceRole.Owner : WorkspaceRole.Member, JoinedAtUtc = DateTime.UtcNow });
            }

            for (var i = 4; i < users.Count; i++)
            {
                var token = Guid.NewGuid().ToString("N");
                tokens.Add(token);
                db.WorkspaceInvites.Add(new WorkspaceInvite { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Email = users[i].Email!, Role = WorkspaceRole.Member, Token = token, CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(7) });
            }

            await db.SaveChangesAsync(ct);
        }

        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, contenders).Select(i =>
                Task.Run(() => service.AcceptAsync(tokens[i], users[4 + i].Id, users[4 + i].Email!), ct)));

            Assert.Equal(1, results.Count(r => r == InviteAcceptResult.Accepted));
            Assert.Equal(contenders - 1, results.Count(r => r == InviteAcceptResult.PlanLimitReached));

            await using var check = factory.CreateDbContext();
            Assert.Equal(PlanCapabilities.StarterMaxMembers, await check.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId, ct));
        }
        finally
        {
            await using var cleanup = factory.CreateDbContext();
            await cleanup.Workspaces.Where(w => w.Id == workspaceId).ExecuteDeleteAsync(CancellationToken.None);
            var ids = users.Select(u => u.Id).ToList();
            await cleanup.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync(CancellationToken.None);
        }
    }
}
