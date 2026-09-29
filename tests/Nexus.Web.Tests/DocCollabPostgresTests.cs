using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Collab;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services.Collab;

namespace Nexus.Web.Tests;

/// <summary>
/// Vários editores enviando ao mesmo tempo contra um PostgreSQL de verdade, cada um com a própria
/// conexão — o que duas instâncias do app fazem. O provider em memória dos outros testes não tem
/// transação nem concorrência entre conexões, então só um banco real prova que o token de
/// concorrência em <c>DocPage.Revision</c> impede duas operações de virarem a mesma revisão.
/// </summary>
/// <remarks>
/// Roda só com <c>NEXUS_TEST_POSTGRES</c> apontando para um banco já migrado (por exemplo o do
/// docker compose); sem a variável, é pulado — a CI não tem banco. Cria um workspace próprio e o
/// apaga no fim.
/// </remarks>
public class DocCollabPostgresTests
{
    private const string ConnectionVariable = "NEXUS_TEST_POSTGRES";

    [Fact]
    public async Task ConcurrentEditorsOnSeparateConnections_ProduceOneRevisionPerChange_AndConverge()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Defina {ConnectionVariable} para rodar contra um PostgreSQL real.");

        var factory = new NpgsqlFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        var notifier = new DocChangeNotifier(new NullDocChangePublisher(), NullLogger<DocChangeNotifier>.Instance);
        var service = new DocCollabService(factory, notifier, NullLogger<DocCollabService>.Instance);

        var workspaceId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Teste de concorrência", Slug = "teste-" + workspaceId.ToString("N"), CreatedAtUtc = DateTime.UtcNow });
            db.DocPages.Add(new DocPage { Id = docId, WorkspaceId = workspaceId, Title = "Concorrência", Type = DocPageType.Text, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        try
        {
            const int editors = 8;
            const int changesPerEditor = 15;
            var contended = 0;

            // Cada editor segue o protocolo do navegador: envia sobre a revisão que conhece; se
            // estiver atrás, transforma a própria alteração sobre o que perdeu e reenvia.
            async Task Edit(int editor)
            {
                var clientId = Guid.NewGuid();
                var known = 0L;
                for (var seq = 1; seq <= changesPerEditor; seq++)
                {
                    var change = new Delta().Insert($"[{editor}.{seq}]");
                    while (true)
                    {
                        var result = await service.SubmitAsync(workspaceId, docId, null, clientId, seq, known, DeltaJson.Serialize(change), isSeed: false);
                        if (result.Status == "ack")
                        {
                            known = result.Revision!.Value;
                            break;
                        }
                        Assert.True(result.Status is "behind" or "busy", $"status inesperado: {result.Status} {result.Error}");
                        Interlocked.Increment(ref contended);
                        foreach (var op in result.Ops ?? [])
                        {
                            if (op.Revision != known + 1)
                            {
                                continue;
                            }
                            change = DeltaJson.Parse(op.Change).Transform(change, priority: true);
                            known = op.Revision;
                        }
                    }
                }
            }

            await Task.WhenAll(Enumerable.Range(1, editors).Select(e => Task.Run(() => Edit(e))));

            await using var check = factory.CreateDbContext();
            var doc = await check.DocPages.AsNoTracking().SingleAsync(d => d.Id == docId);
            var revisions = await check.DocOperations.Where(o => o.DocPageId == docId).OrderBy(o => o.Revision).Select(o => o.Revision).ToListAsync();

            // Houve disputa de verdade; sem isso o teste passaria sem provar nada.
            Assert.True(contended > 0, "nenhum envio concorrente aconteceu");

            // Uma revisão por alteração, sem buraco e sem repetição.
            Assert.Equal(editors * changesPerEditor, doc.Revision);
            Assert.Equal(Enumerable.Range(1, editors * changesPerEditor).Select(r => (long)r), revisions);

            // Nenhuma alteração se perdeu.
            var text = string.Concat(DeltaJson.Parse(doc.DeltaJson!).Ops.Select(o => o.Text));
            for (var editor = 1; editor <= editors; editor++)
            {
                for (var seq = 1; seq <= changesPerEditor; seq++)
                {
                    Assert.Contains($"[{editor}.{seq}]", text);
                }
            }
        }
        finally
        {
            await using var cleanup = factory.CreateDbContext();
            await cleanup.Workspaces.Where(w => w.Id == workspaceId).ExecuteDeleteAsync();
        }
    }

    private sealed class NpgsqlFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
