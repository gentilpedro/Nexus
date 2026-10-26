using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Collab;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services.Collab;

namespace Nexus.Web.Tests;

/// <summary>Covers <see cref="SheetCollabService"/> (#57) against the real model, in memory.</summary>
public class SheetCollabServiceTests
{
    private static (SheetCollabService Service, List<DocChange> Changes) Build(IDbContextFactory<AppDbContext> factory)
    {
        var notifier = new DocChangeNotifier(new NullDocChangePublisher(), NullLogger<DocChangeNotifier>.Instance);
        var changes = new List<DocChange>();
        notifier.Changed += changes.Add;
        return (new SheetCollabService(factory, notifier, NullLogger<SheetCollabService>.Instance), changes);
    }

    private static async Task<(Guid WorkspaceId, Guid DocId)> SeedAsync(IDbContextFactory<AppDbContext> factory, string gridJson, DocPageType type = DocPageType.Spreadsheet)
    {
        var workspaceId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "W", Slug = $"w-{workspaceId:N}" });
        db.DocPages.Add(new DocPage { Id = docId, WorkspaceId = workspaceId, Title = "Orçamento", Type = type, GridDataJson = gridJson });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (workspaceId, docId);
    }

    [Fact]
    public async Task AcceptedEdit_IsStored_BumpsTheRevision_AndNotifies()
    {
        var factory = TestDb.CreateFactory();
        var (service, changes) = Build(factory);
        var (workspaceId, docId) = await SeedAsync(factory, """[["a","b"]]""");
        var client = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        var result = await service.SubmitAsync(workspaceId, docId, "user-1", client, new SheetOperation.SetCell("r0", "c1", "b", "B"), ct);

        Assert.True(result.IsAck);
        Assert.Equal(1, result.Revision);
        var snapshot = await service.LoadAsync(workspaceId, docId, ct);
        Assert.Equal("B", snapshot!.Document.Get("r0", "c1"));
        Assert.Equal(1, snapshot.Revision);
        var change = Assert.Single(changes);
        Assert.Equal((docId, client, DocChangeKind.Operation), (change.DocPageId, change.ClientId, change.Kind));
    }

    // A legacy sheet is rewritten in the new format by its first collaborative edit, and keeps its data.
    [Fact]
    public async Task FirstEditOnALegacySheet_RewritesTheFormat_KeepingEveryValue()
    {
        var factory = TestDb.CreateFactory();
        var (service, _) = Build(factory);
        var (workspaceId, docId) = await SeedAsync(factory, """[["a","b"],["c","d"]]""");
        var ct = TestContext.Current.CancellationToken;

        await service.SubmitAsync(workspaceId, docId, null, Guid.NewGuid(), new SheetOperation.SetCell("r1", "c0", "c", "C"), ct);

        await using var db = factory.CreateDbContext();
        var json = (await db.DocPages.SingleAsync(d => d.Id == docId, ct)).GridDataJson!;
        Assert.StartsWith("{", json);
        Assert.Equal([["a", "b"], ["C", "d"]], SheetJson.Parse(json).ToGrid());
    }

    [Fact]
    public async Task SameCellFromAStaleView_IsAConflict_AndNothingIsOverwritten()
    {
        var factory = TestDb.CreateFactory();
        var (service, changes) = Build(factory);
        var (workspaceId, docId) = await SeedAsync(factory, """[["a"]]""");
        var ct = TestContext.Current.CancellationToken;

        await service.SubmitAsync(workspaceId, docId, "ana", Guid.NewGuid(), new SheetOperation.SetCell("r0", "c0", "a", "da Ana"), ct);
        var second = await service.SubmitAsync(workspaceId, docId, "pedro", Guid.NewGuid(), new SheetOperation.SetCell("r0", "c0", "a", "do Pedro"), ct);

        Assert.Equal("conflict", second.Status);
        Assert.Equal("da Ana", second.Current);
        Assert.Equal("da Ana", (await service.LoadAsync(workspaceId, docId, ct))!.Document.Get("r0", "c0"));
        Assert.Single(changes);
    }

    [Fact]
    public async Task TextDocsAndOtherWorkspaces_AreNotReachable()
    {
        var factory = TestDb.CreateFactory();
        var (service, _) = Build(factory);
        var (workspaceId, textDocId) = await SeedAsync(factory, "", DocPageType.Text);
        var (_, sheetId) = await SeedAsync(factory, """[["a"]]""");
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("notfound", (await service.SubmitAsync(workspaceId, textDocId, null, Guid.NewGuid(), new SheetOperation.AddRow("x"), ct)).Status);
        // The sheet exists, but in another workspace.
        Assert.Equal("notfound", (await service.SubmitAsync(workspaceId, sheetId, null, Guid.NewGuid(), new SheetOperation.AddRow("x"), ct)).Status);
        Assert.Null(await service.LoadAsync(workspaceId, sheetId, ct));
    }
}
