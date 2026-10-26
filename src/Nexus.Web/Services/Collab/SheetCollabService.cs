using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Collab;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.Collab;

/// <summary>Estado de uma planilha para quem vai exibir ou editar.</summary>
public sealed record SheetSnapshot(long Revision, string Title, SheetDocument Document);

/// <summary>
/// Resposta a uma alteração na planilha:
/// <list type="bullet">
/// <item><c>ack</c> — gravada (ou já estava assim), na <see cref="Revision"/>;</item>
/// <item><c>conflict</c> — a célula mudou desde que a pessoa a viu; <see cref="Current"/> traz o valor atual;</item>
/// <item><c>rejected</c> — inválida, nunca será aceita;</item>
/// <item><c>busy</c> — concorrência alta demais neste instante: tentar de novo;</item>
/// <item><c>notfound</c> — a planilha não existe mais neste workspace.</item>
/// </list>
/// </summary>
public sealed record SheetSubmitResult(string Status, long? Revision = null, string? Error = null, string? Current = null)
{
    public bool IsAck => Status == "ack";
}

/// <summary>
/// O servidor como sequenciador das planilhas (#57): aplica cada alteração sobre a versão atual,
/// grava e avisa quem está com a planilha aberta.
/// </summary>
/// <remarks>
/// A decisão é do <see cref="SheetSequencer"/>; aqui fica a gravação atômica entre instâncias. Ela
/// vem de <see cref="DocPage.Revision"/> ser token de concorrência, como nos Docs: se duas
/// instâncias aplicam alterações sobre a mesma revisão, só a primeira grava; a segunda lê de novo e
/// reaplica a sua sobre a versão nova — o que, para células diferentes, sempre dá certo.
/// Não verifica permissão: quem chama (a página) revalida o acesso ao workspace antes de cada envio.
/// </remarks>
public sealed class SheetCollabService(
    IDbContextFactory<AppDbContext> dbFactory,
    DocChangeNotifier notifier,
    ILogger<SheetCollabService> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private const int MaxAttempts = 8;

    public async Task<SheetSnapshot?> LoadAsync(Guid workspaceId, Guid docPageId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var doc = await FindSheet(db, workspaceId, docPageId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return doc is null ? null : new SheetSnapshot(doc.Revision, doc.Title, SheetJson.Parse(doc.GridDataJson));
    }

    public async Task<SheetSubmitResult> SubmitAsync(
        Guid workspaceId,
        Guid docPageId,
        string? userId,
        Guid clientId,
        SheetOperation operation,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var doc = await FindSheet(db, workspaceId, docPageId).FirstOrDefaultAsync(cancellationToken);
            if (doc is null)
            {
                return new SheetSubmitResult("notfound", Error: "A planilha não existe mais.");
            }

            switch (SheetSequencer.Apply(SheetJson.Parse(doc.GridDataJson), operation))
            {
                case SheetApplyResult.Applied applied:
                    var json = SheetJson.Serialize(applied.Document);
                    if (json.Length > DocPage.MaxGridDataJsonLength)
                    {
                        return new SheetSubmitResult("rejected", Error: "A planilha passaria do tamanho máximo.");
                    }

                    doc.GridDataJson = json;
                    doc.Revision++;
                    doc.UpdatedByUserId = userId;
                    doc.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;

                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        // Outra alteração gravou antes; relê e reaplica sobre a versão nova.
                        logger.LogDebug(ex, "Conflito ao gravar a revisão {Revision} da planilha {DocPageId}; tentando de novo.", doc.Revision, docPageId);
                        continue;
                    }

                    await notifier.PublishAsync(new DocChange(docPageId, doc.Revision, clientId, DocChangeKind.Operation), cancellationToken);
                    return new SheetSubmitResult("ack", doc.Revision);

                case SheetApplyResult.Unchanged:
                    return new SheetSubmitResult("ack", doc.Revision);

                case SheetApplyResult.Conflict conflict:
                    return new SheetSubmitResult("conflict", doc.Revision, conflict.Reason, conflict.Current);

                case SheetApplyResult.Rejected rejected:
                    return new SheetSubmitResult("rejected", Error: rejected.Reason);
            }
        }

        return new SheetSubmitResult("busy", Error: "Muitas alterações ao mesmo tempo. Tente de novo.");
    }

    /// <summary>
    /// Troca o título. Continua "vence o último": é um campo curto, editado raramente, e não
    /// compete com as células.
    /// </summary>
    public async Task<bool> RenameAsync(Guid workspaceId, Guid docPageId, string? userId, string title, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var updated = await FindSheet(db, workspaceId, docPageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Title, title)
                .SetProperty(d => d.UpdatedByUserId, userId)
                .SetProperty(d => d.UpdatedAtUtc, clock.GetUtcNow().UtcDateTime), cancellationToken);
        if (updated > 0)
        {
            await notifier.PublishAsync(new DocChange(docPageId, 0, Guid.Empty, DocChangeKind.Content), cancellationToken);
        }
        return updated > 0;
    }

    private static IQueryable<DocPage> FindSheet(AppDbContext db, Guid workspaceId, Guid docPageId) =>
        db.DocPages.Where(d => d.Id == docPageId && d.WorkspaceId == workspaceId && d.Type == DocPageType.Spreadsheet);
}
