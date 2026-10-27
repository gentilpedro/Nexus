using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Busca sem diferenciar maiúsculas e acentos (#60) num PostgreSQL de verdade: é lá que
/// <c>string.Contains</c> diferenciava, e é lá que roda o <c>unaccent(...) ILIKE</c>.
/// </summary>
/// <remarks>
/// Roda só com <c>NEXUS_TEST_POSTGRES</c> apontando para um banco já migrado, como
/// <see cref="StarterLimitsPostgresTests"/>; sem a variável, é pulado. Cria um workspace próprio e
/// o apaga no fim.
/// </remarks>
public class SearchTextPostgresTests
{
    private const string ConnectionVariable = "NEXUS_TEST_POSTGRES";

    private sealed class NpgsqlFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task TasksDocsAndPeople_IgnoreCaseAndAccents()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Defina {ConnectionVariable} para rodar contra um PostgreSQL real.");

        var factory = new NpgsqlFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        var workspaceId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var (service, userId) = await SearchTextTests.SeedTasksAsync(factory, workspaceId,
            "Pagamento por Pix com QR Code", "Especificação do checkout", "Cobertura de 100% dos testes");

        try
        {
            Assert.Equal(["Pagamento por Pix com QR Code"], await SearchTextTests.SearchAsync(service, userId, workspaceId, "pagamento"));
            Assert.Equal(["Pagamento por Pix com QR Code"], await SearchTextTests.SearchAsync(service, userId, workspaceId, "PIX"));
            Assert.Equal(["Especificação do checkout"], await SearchTextTests.SearchAsync(service, userId, workspaceId, "especificacao"));
            Assert.Equal(["Especificação do checkout"], await SearchTextTests.SearchAsync(service, userId, workspaceId, "ESPECIFICAÇÃO"));
            Assert.Equal(["Cobertura de 100% dos testes"], await SearchTextTests.SearchAsync(service, userId, workspaceId, "100%"));
            Assert.Empty(await SearchTextTests.SearchAsync(service, userId, workspaceId, "_"));

            await using var db = factory.CreateDbContext();
            db.DocPages.Add(new DocPage { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Title = "Orçamento do Lançamento", Type = DocPageType.Spreadsheet, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);

            var docs = await db.DocPages.Where(d => d.WorkspaceId == workspaceId)
                .Where(SearchText.Matches<DocPage>(db, "orcamento do lancamento", null, d => d.Title))
                .Select(d => d.Title).ToListAsync(ct);
            Assert.Equal(["Orçamento do Lançamento"], docs);

            var people = await db.Users.Where(u => u.Id == userId)
                .Where(SearchText.Matches<ApplicationUser>(db, userId.ToUpperInvariant() + "@EXAMPLE", null, u => u.DisplayName, u => u.Email))
                .CountAsync(ct);
            Assert.Equal(1, people);
        }
        finally
        {
            await using var cleanup = factory.CreateDbContext();
            await cleanup.Workspaces.Where(w => w.Id == workspaceId).ExecuteDeleteAsync(CancellationToken.None);
            await cleanup.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }
}
