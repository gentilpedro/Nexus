using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services.Collab;

namespace Nexus.Web.Tests;

/// <summary>
/// O servidor como sequenciador da edição colaborativa, com o banco no meio: aceitar na ordem,
/// devolver o que o editor perdeu, reconhecer reenvio, exigir a semeadura dos documentos antigos
/// e nunca deixar o HTML do modo leitura voltar para uma versão velha.
/// </summary>
public class DocCollabServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bia = Guid.NewGuid();

    private readonly IDbContextFactory<AppDbContext> factory = TestDb.CreateFactory();
    private readonly List<DocChange> changes = [];
    private readonly DocCollabService service;

    public DocCollabServiceTests()
    {
        var notifier = new DocChangeNotifier(new NullDocChangePublisher(), NullLogger<DocChangeNotifier>.Instance);
        notifier.Changed += changes.Add;
        service = new DocCollabService(factory, notifier, NullLogger<DocCollabService>.Instance);
    }

    private async Task<Guid> CreateDoc(string? html = null, DocPageType type = DocPageType.Text)
    {
        await using var db = await factory.CreateDbContextAsync();
        var doc = new DocPage
        {
            Id = Guid.NewGuid(),
            WorkspaceId = WorkspaceId,
            Title = "Doc",
            Type = type,
            ContentHtml = html,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.DocPages.Add(doc);
        await db.SaveChangesAsync();
        return doc.Id;
    }

    private Task<DocSubmitResult> Submit(Guid docId, Guid client, long seq, long baseRevision, string change, bool isSeed = false) =>
        service.SubmitAsync(WorkspaceId, docId, "user-1", client, seq, baseRevision, change, isSeed);

    private async Task<DocPage> Reload(Guid docId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.DocPages.AsNoTracking().SingleAsync(d => d.Id == docId);
    }

    [Fact]
    public async Task FirstChange_IsAcceptedComposedAndLogged()
    {
        var docId = await CreateDoc();

        var result = await Submit(docId, Ana, 1, 0, """[{"insert":"Olá"}]""");

        Assert.Equal("ack", result.Status);
        Assert.Equal(1, result.Revision);
        var doc = await Reload(docId);
        Assert.Equal(1, doc.Revision);
        Assert.Equal("""[{"insert":"Olá\n"}]""", doc.DeltaJson);
        await using var db = await factory.CreateDbContextAsync();
        var op = await db.DocOperations.SingleAsync();
        Assert.Equal((Ana, 1L, 1L), (op.ClientId, op.ClientSeq, op.Revision));
        Assert.Contains(changes, c => c.DocPageId == docId && c.Revision == 1 && c.Kind == DocChangeKind.Operation);
    }

    [Fact]
    public async Task ChangeFromAnOldRevision_GetsTheMissingOperationsInsteadOfOverwriting()
    {
        var docId = await CreateDoc();
        await Submit(docId, Ana, 1, 0, """[{"insert":"Ana"}]""");
        await Submit(docId, Ana, 2, 1, """[{"retain":3},{"insert":"!"}]""");

        // Bia escreveu sobre a revisão 0 sem ver nada disso: é o caso que o last-write-wins perdia.
        var result = await Submit(docId, Bia, 1, 0, """[{"insert":"Bia"}]""");

        Assert.Equal("behind", result.Status);
        Assert.Equal([1L, 2L], result.Ops!.Select(o => o.Revision));
        Assert.Equal(2, (await Reload(docId)).Revision);
    }

    [Fact]
    public async Task ResendOfAnAcceptedChange_IsAcknowledgedAgainWithoutApplyingTwice()
    {
        var docId = await CreateDoc();
        await Submit(docId, Ana, 1, 0, """[{"insert":"x"}]""");

        // A resposta se perdeu e o editor reenviou a mesma alteração, com o mesmo número.
        var again = await Submit(docId, Ana, 1, 0, """[{"insert":"x"}]""");

        Assert.Equal("ack", again.Status);
        Assert.Equal(1, again.Revision);
        Assert.Equal("""[{"insert":"x\n"}]""", (await Reload(docId)).DeltaJson);
    }

    [Theory]
    [InlineData("""[{"retain":99},{"insert":"x"}]""")]
    [InlineData("""[{"delete":1}]""")]
    [InlineData("""[{"insert":"x","attributes":{"font":"serif"}}]""")]
    [InlineData("""isto não é json""")]
    public async Task InvalidChange_IsRejectedAndNothingChanges(string change)
    {
        var docId = await CreateDoc();

        var result = await Submit(docId, Ana, 1, 0, change);

        Assert.Equal("rejected", result.Status);
        Assert.Equal(0, (await Reload(docId)).Revision);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task LegacyDocument_MustBeSeededBeforeAnyEdit_AndOnlyOnce()
    {
        var docId = await CreateDoc("<p>conteúdo antigo</p>");

        var beforeSeed = await Submit(docId, Ana, 1, 0, """[{"insert":"x"}]""");
        Assert.Equal("resync", beforeSeed.Status);

        var seed = await Submit(docId, Ana, 1, 0, """[{"insert":"conteúdo antigo\n"}]""", isSeed: true);
        Assert.Equal("ack", seed.Status);
        Assert.Equal("""[{"insert":"conteúdo antigo\n"}]""", (await Reload(docId)).DeltaJson);

        // Bia abriu ao mesmo tempo e também converteu o HTML: a semeadura dela é descartada,
        // não rebaseada (o que duplicaria o texto).
        var secondSeed = await Submit(docId, Bia, 1, 0, """[{"insert":"conteúdo antigo\n"}]""", isSeed: true);
        Assert.Equal("seeded", secondSeed.Status);
        Assert.Equal(1, (await Reload(docId)).Revision);
    }

    [Fact]
    public async Task SeedThatIsNotADocument_IsRejected()
    {
        var docId = await CreateDoc("<p>antigo</p>");

        var result = await Submit(docId, Ana, 1, 0, """[{"retain":1},{"insert":"x"}]""", isSeed: true);

        Assert.Equal("rejected", result.Status);
    }

    [Fact]
    public async Task EditorTooFarBehind_IsToldToReload()
    {
        var docId = await CreateDoc();
        await using (var db = await factory.CreateDbContextAsync())
        {
            // O log já foi podado até a revisão 50.
            var doc = await db.DocPages.SingleAsync(d => d.Id == docId);
            doc.Revision = 60;
            doc.DeltaJson = """[{"insert":"texto\n"}]""";
            for (var revision = 51; revision <= 60; revision++)
            {
                db.DocOperations.Add(new DocOperation { DocPageId = docId, Revision = revision, ClientId = Bia, ClientSeq = revision, ChangeJson = """[{"retain":1}]""" });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal("resync", (await Submit(docId, Ana, 1, 10, """[{"insert":"x"}]""")).Status);
        Assert.Equal("resync", (await service.GetOperationsSinceAsync(WorkspaceId, docId, 10)).Status);

        var reachable = await service.GetOperationsSinceAsync(WorkspaceId, docId, 55);
        Assert.Equal("ok", reachable.Status);
        Assert.Equal([56L, 57L, 58L, 59L, 60L], reachable.Ops!.Select(o => o.Revision));
    }

    [Fact]
    public async Task DocumentFromAnotherWorkspaceOrOfAnotherType_IsNotFound()
    {
        var docId = await CreateDoc();
        var sheetId = await CreateDoc(type: DocPageType.Spreadsheet);

        var otherWorkspace = await service.SubmitAsync(Guid.NewGuid(), docId, "user-1", Ana, 1, 0, """[{"insert":"x"}]""", false);
        var spreadsheet = await Submit(sheetId, Ana, 1, 0, """[{"insert":"x"}]""");

        Assert.Equal("rejected", otherWorkspace.Status);
        Assert.Equal("rejected", spreadsheet.Status);
        Assert.Null(await service.LoadAsync(Guid.NewGuid(), docId));
    }

    [Fact]
    public async Task ReadModeHtml_OnlyMovesForward_AndIsSanitized()
    {
        var docId = await CreateDoc();
        await Submit(docId, Ana, 1, 0, """[{"insert":"a"}]""");
        await Submit(docId, Ana, 2, 1, """[{"insert":"b"}]""");

        Assert.True(await service.SaveHtmlAsync(WorkspaceId, docId, 2, "<p>ba</p><script>alert(1)</script>"));
        Assert.Equal("<p>ba</p>", (await Reload(docId)).ContentHtml);

        // Um editor atrasado mandando o HTML da revisão 1 não pode desfazer o texto.
        Assert.False(await service.SaveHtmlAsync(WorkspaceId, docId, 1, "<p>a</p>"));
        // Nem de uma revisão que ainda não existe.
        Assert.False(await service.SaveHtmlAsync(WorkspaceId, docId, 9, "<p>futuro</p>"));

        var doc = await Reload(docId);
        Assert.Equal("<p>ba</p>", doc.ContentHtml);
        Assert.Equal(2, doc.HtmlRevision);
    }

    [Fact]
    public async Task Revision_IsAConcurrencyToken()
    {
        // O que impede duas instâncias de aceitarem, cada uma, uma operação diferente como a
        // mesma revisão: a segunda gravação falha em vez de sobrescrever a primeira.
        var docId = await CreateDoc();
        await using var first = await factory.CreateDbContextAsync();
        await using var second = await factory.CreateDbContextAsync();
        var a = await first.DocPages.SingleAsync(d => d.Id == docId);
        var b = await second.DocPages.SingleAsync(d => d.Id == docId);

        a.Revision = 1;
        await first.SaveChangesAsync();
        b.Revision = 1;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task OldOperations_ArePruned_KeepingTheRecentWindow()
    {
        var docId = await CreateDoc();
        for (var revision = 0; revision < DocCollabService.KeptOperations + 100; revision++)
        {
            var result = await Submit(docId, Ana, revision + 1, revision, """[{"insert":"x"}]""");
            Assert.Equal("ack", result.Status);
        }

        await using var db = await factory.CreateDbContextAsync();
        var revisions = await db.DocOperations.Where(o => o.DocPageId == docId).Select(o => o.Revision).ToListAsync();
        Assert.Equal(DocCollabService.KeptOperations, revisions.Count);
        Assert.Equal(101, revisions.Min());
    }
}
