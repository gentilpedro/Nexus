using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.BackgroundServices;

namespace Nexus.Web.Tests;

/// <summary>
/// A política de privacidade promete apagar o contato comercial em 12 meses (#56). A limpeza usa
/// <c>ExecuteDeleteAsync</c>, que o provider em memória não suporta, então só um PostgreSQL real
/// prova que ela apaga o antigo e preserva o recente.
/// </summary>
/// <remarks>
/// Roda só com <c>NEXUS_TEST_POSTGRES</c> apontando para um banco já migrado, como
/// <see cref="DocCollabPostgresTests"/>; sem a variável, é pulado.
/// </remarks>
public class CommercialContactRetentionPostgresTests
{
    private const string ConnectionVariable = "NEXUS_TEST_POSTGRES";

    private sealed class NpgsqlFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task RetentionSweep_DeletesContactsOlderThanTheWindow_AndKeepsRecentOnes()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Defina {ConnectionVariable} para rodar contra um PostgreSQL real.");

        var factory = new NpgsqlFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        var ct = TestContext.Current.CancellationToken;
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.CommercialContacts.AddRange(
                new CommercialContact { Id = old, Name = "Antigo", Company = "A", Email = "a@example.com", TeamSize = "Até 10 pessoas", CreatedAtUtc = DateTime.UtcNow.AddDays(-366) },
                new CommercialContact { Id = recent, Name = "Recente", Company = "B", Email = "b@example.com", TeamSize = "Até 10 pessoas", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) });
            await db.SaveChangesAsync(ct);
        }

        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            await new DataRetentionHostedService(factory, configuration, NullLogger<DataRetentionHostedService>.Instance)
                .PurgeAsync(ct);

            await using var check = factory.CreateDbContext();
            Assert.False(await check.CommercialContacts.AnyAsync(c => c.Id == old, ct));
            Assert.True(await check.CommercialContacts.AnyAsync(c => c.Id == recent, ct));
        }
        finally
        {
            await using var cleanup = factory.CreateDbContext();
            await cleanup.CommercialContacts.Where(c => c.Id == old || c.Id == recent).ExecuteDeleteAsync(CancellationToken.None);
        }
    }
}
