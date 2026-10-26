using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Collab;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services.Collab;

namespace Nexus.Web.Tests;

/// <summary>
/// Vários editores mudando células diferentes da mesma planilha ao mesmo tempo, cada um na própria
/// conexão — o que várias abas e várias instâncias do app fazem (#57). Só um banco real prova que o
/// token de concorrência em <c>DocPage.Revision</c> impede uma gravação de apagar a outra.
/// </summary>
/// <remarks>
/// Roda só com <c>NEXUS_TEST_POSTGRES</c> apontando para um banco já migrado, como
/// <see cref="DocCollabPostgresTests"/>; sem a variável, é pulado. Cria um workspace próprio e o
/// apaga no fim.
/// </remarks>
public class SheetCollabPostgresTests
{
    private const string ConnectionVariable = "NEXUS_TEST_POSTGRES";

    private sealed class NpgsqlFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task ConcurrentEditorsOnDifferentCells_AllLand_OneRevisionEach()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Defina {ConnectionVariable} para rodar contra um PostgreSQL real.");

        var factory = new NpgsqlFactory(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        var notifier = new DocChangeNotifier(new NullDocChangePublisher(), NullLogger<DocChangeNotifier>.Instance);
        var service = new SheetCollabService(factory, notifier, NullLogger<SheetCollabService>.Instance);
        var ct = TestContext.Current.CancellationToken;

        var workspaceId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        const int editors = 8;
        const int cellsPerEditor = 5;

        // Uma linha por editor, cinco colunas: ninguém disputa célula com ninguém.
        var sheet = new SheetDocument(
            Enumerable.Range(0, editors).Select(i => $"r{i}").ToList(),
            Enumerable.Range(0, cellsPerEditor).Select(i => $"c{i}").ToList());

        await using (var db = factory.CreateDbContext())
        {
            db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Teste de planilha", Slug = "planilha-" + workspaceId.ToString("N"), CreatedAtUtc = DateTime.UtcNow });
            db.DocPages.Add(new DocPage { Id = docId, WorkspaceId = workspaceId, Title = "Concorrência", Type = DocPageType.Spreadsheet, GridDataJson = SheetJson.Serialize(sheet), CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        try
        {
            async Task Edit(int editor)
            {
                var client = Guid.NewGuid();
                for (var c = 0; c < cellsPerEditor; c++)
                {
                    while (true)
                    {
                        var result = await service.SubmitAsync(workspaceId, docId, null, client,
                            new SheetOperation.SetCell($"r{editor}", $"c{c}", "", $"{editor}.{c}"), ct);
                        if (result.IsAck)
                        {
                            break;
                        }
                        Assert.Equal("busy", result.Status); // sob disputa pesada o serviço pode pedir nova tentativa
                    }
                }
            }

            await Task.WhenAll(Enumerable.Range(0, editors).Select(e => Task.Run(() => Edit(e), ct)));

            var final = await service.LoadAsync(workspaceId, docId, ct);
            Assert.Equal(editors * cellsPerEditor, final!.Revision);
            for (var e = 0; e < editors; e++)
            {
                for (var c = 0; c < cellsPerEditor; c++)
                {
                    Assert.Equal($"{e}.{c}", final.Document.Get($"r{e}", $"c{c}"));
                }
            }

            Assert.True(await service.RenameAsync(workspaceId, docId, null, "Concorrência (renomeada)", ct));
            Assert.Equal("Concorrência (renomeada)", (await service.LoadAsync(workspaceId, docId, ct))!.Title);
        }
        finally
        {
            await using var cleanup = factory.CreateDbContext();
            await cleanup.Workspaces.Where(w => w.Id == workspaceId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }
}
