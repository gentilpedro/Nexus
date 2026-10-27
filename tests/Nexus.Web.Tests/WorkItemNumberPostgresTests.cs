using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Várias tarefas criadas ao mesmo tempo no mesmo workspace, cada uma na própria conexão: cada
/// uma tem que receber um número diferente (#63).
/// </summary>
/// <remarks>
/// Roda só com <c>NEXUS_TEST_POSTGRES</c> apontando para um banco já migrado, como
/// <see cref="StarterLimitsPostgresTests"/>; sem a variável, é pulado. Cria um workspace próprio e
/// o apaga no fim.
/// </remarks>
public class WorkItemNumberPostgresTests
{
    private const string ConnectionVariable = "NEXUS_TEST_POSTGRES";

    [Fact]
    public async Task ConcurrentCreations_GetDistinctConsecutiveNumbers()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Defina {ConnectionVariable} para rodar contra um PostgreSQL real.");

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
        var workspaceId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        const int contenders = 20;

        await using (var db = new AppDbContext(options))
        {
            db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Numeração", Slug = "numeracao-" + workspaceId.ToString("N")[..8], CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        try
        {
            var numbers = await Task.WhenAll(Enumerable.Range(0, contenders).Select(_ => Task.Run(async () =>
            {
                await using var db = new AppDbContext(options);
                return await WorkItemNumbers.NextAsync(db, workspaceId, ct);
            }, ct)));

            Assert.Equal(Enumerable.Range(1, contenders), numbers.Order());

            await using var check = new AppDbContext(options);
            Assert.Equal(contenders, await check.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.LastWorkItemNumber).SingleAsync(ct));
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            await cleanup.Workspaces.Where(w => w.Id == workspaceId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }
}
