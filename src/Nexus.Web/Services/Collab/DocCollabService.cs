using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Collab;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.Collab;

/// <summary>Estado de um documento para quem vai começar a editar.</summary>
/// <param name="Document">Delta JSON do documento na <paramref name="Revision"/>; <c>null</c>
/// quando ainda não há Delta (documento vazio, ou antigo esperando a semeadura).</param>
/// <param name="NeedsSeed">Documento de antes da edição colaborativa: o editor converte o
/// <paramref name="Html"/> e manda como semeadura antes de qualquer outra alteração.</param>
public sealed record DocCollabSnapshot(long Revision, string? Document, string? Html, bool NeedsSeed);

/// <summary>Uma operação do log, como o editor a recebe.</summary>
/// <param name="Change">Delta JSON da alteração.</param>
public sealed record DocOperationDto(long Revision, Guid ClientId, long Seq, string Change);

/// <summary>
/// Resposta a um envio. <see cref="Status"/> é o que o editor no navegador lê:
/// <list type="bullet">
/// <item><c>ack</c> — aceita, na <see cref="Revision"/> (também para um reenvio já aceito);</item>
/// <item><c>behind</c> — o editor está atrás; <see cref="Ops"/> traz o que ele perdeu;</item>
/// <item><c>resync</c> — não dá para alcançar pelo log (podado, ou falta semear): recarregar;</item>
/// <item><c>seeded</c> — outra pessoa semeou primeiro: recarregar e descartar a semeadura;</item>
/// <item><c>rejected</c> — alteração inválida, nunca será aceita;</item>
/// <item><c>busy</c> — concorrência alta demais neste instante: tentar de novo;</item>
/// <item><c>forbidden</c> / <c>ratelimited</c> — definidos por quem chama (a página).</item>
/// </list>
/// </summary>
public sealed record DocSubmitResult(string Status, long? Revision = null, IReadOnlyList<DocOperationDto>? Ops = null, string? Error = null)
{
    public static DocSubmitResult Ack(long revision) => new("ack", revision);
    public static DocSubmitResult Behind(IReadOnlyList<DocOperationDto> ops) => new("behind", Ops: ops);
    public static DocSubmitResult Resync(string reason) => new("resync", Error: reason);
    public static DocSubmitResult Seeded() => new("seeded");
    public static DocSubmitResult Rejected(string reason) => new("rejected", Error: reason);
    public static DocSubmitResult Busy() => new("busy");
    public static DocSubmitResult Forbidden() => new("forbidden", Error: "Você não tem mais acesso a este workspace.");
    public static DocSubmitResult RateLimited() => new("ratelimited", Error: "Muitas alterações em pouco tempo.");
}

/// <summary>Resposta a um pedido de catch-up.</summary>
public sealed record DocCatchUpResult(string Status, IReadOnlyList<DocOperationDto>? Ops = null);

/// <summary>
/// O servidor como sequenciador da edição colaborativa: grava cada operação aceita, na ordem, e
/// mantém o documento composto em <see cref="DocPage.DeltaJson"/>.
/// </summary>
/// <remarks>
/// <para>
/// A decisão (aceitar, atrasado, inválido) é do <see cref="DocSequencer"/>; aqui fica o que
/// depende do banco: reconhecer reenvios, montar a lista do que o editor perdeu e gravar de forma
/// atômica mesmo com várias instâncias. A atomicidade vem de <see cref="DocPage.Revision"/> ser
/// token de concorrência: duas instâncias que aceitem uma operação sobre a mesma revisão não
/// conseguem gravar as duas — a segunda recebe <see cref="DbUpdateConcurrencyException"/>, lê de
/// novo e passa a ver a revisão nova, o que a transforma em "atrasado".
/// </para>
/// <para>
/// Não verifica permissão: quem chama (a página do documento) revalida o acesso ao workspace e
/// aplica o limite de taxa antes de cada envio. O serviço só garante que o documento pertence ao
/// workspace informado.
/// </para>
/// </remarks>
public sealed class DocCollabService(
    IDbContextFactory<AppDbContext> dbFactory,
    DocChangeNotifier notifier,
    ILogger<DocCollabService> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>Quantas operações mais recentes ficam no log para quem reconecta atrasado.</summary>
    public const int KeptOperations = 1000;

    /// <summary>Máximo de operações devolvidas de uma vez; o editor pede o resto em seguida.</summary>
    public const int MaxOperationsPerResponse = 500;

    private const int MaxAttempts = 5;

    public async Task<DocCollabSnapshot?> LoadAsync(Guid workspaceId, Guid docPageId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var doc = await FindTextDoc(db, workspaceId, docPageId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return doc is null ? null : new DocCollabSnapshot(doc.Revision, doc.DeltaJson, doc.ContentHtml, doc.NeedsSeed);
    }

    public async Task<DocSubmitResult> SubmitAsync(
        Guid workspaceId,
        Guid docPageId,
        string? userId,
        Guid clientId,
        long seq,
        long baseRevision,
        string changeJson,
        bool isSeed,
        CancellationToken cancellationToken = default)
    {
        if (changeJson.Length > DocDeltaPolicy.MaxChangeJsonLength)
        {
            return DocSubmitResult.Rejected("Alteração grande demais.");
        }

        Delta change;
        try
        {
            change = DeltaJson.Parse(changeJson);
        }
        catch (DeltaFormatException ex)
        {
            return DocSubmitResult.Rejected(ex.Message);
        }

        if (isSeed)
        {
            // A semeadura chega como documento inteiro (o HTML antigo convertido pelo Quill) e
            // vira a alteração "insere tudo, apaga a quebra de linha do documento vazio".
            if (!change.IsDocument() || baseRevision != 0)
            {
                return DocSubmitResult.Rejected("Semeadura inválida.");
            }
            change = new Delta(change.Ops).Delete(1);
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var doc = await FindTextDoc(db, workspaceId, docPageId).FirstOrDefaultAsync(cancellationToken);
            if (doc is null)
            {
                return DocSubmitResult.Rejected("Documento não encontrado.");
            }

            // Reenvio de algo já aceito (a resposta se perdeu): confirma de novo, sem reaplicar.
            var existing = await db.DocOperations
                .Where(o => o.DocPageId == docPageId && o.ClientId == clientId && o.ClientSeq == seq)
                .Select(o => (long?)o.Revision)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is { } acceptedAt)
            {
                return DocSubmitResult.Ack(acceptedAt);
            }

            if (!isSeed && doc.NeedsSeed)
            {
                return DocSubmitResult.Resync("O documento ainda não foi convertido para edição colaborativa.");
            }

            var current = doc.DeltaJson is null ? DocDeltaPolicy.EmptyDocument() : DeltaJson.Parse(doc.DeltaJson);

            switch (DocSequencer.Decide(current, doc.Revision, baseRevision, change, isSeed))
            {
                case SubmitDecision.Accepted accepted:
                    var documentJson = DeltaJson.Serialize(accepted.Document);
                    if (documentJson.Length > DocPage.MaxDeltaJsonLength)
                    {
                        return DocSubmitResult.Rejected("O documento passaria do tamanho máximo.");
                    }

                    var now = clock.GetUtcNow().UtcDateTime;
                    doc.DeltaJson = documentJson;
                    doc.Revision = accepted.Revision;
                    doc.UpdatedByUserId = userId;
                    doc.UpdatedAtUtc = now;
                    db.DocOperations.Add(new DocOperation
                    {
                        DocPageId = docPageId,
                        Revision = accepted.Revision,
                        ClientId = clientId,
                        ClientSeq = seq,
                        ChangeJson = DeltaJson.Serialize(change),
                        UserId = userId,
                        CreatedAtUtc = now,
                    });

                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex)
                    {
                        // Outra instância gravou antes (token de concorrência ou índice único).
                        // Na próxima volta, a leitura nova mostra se virou reenvio ou atraso.
                        logger.LogDebug(ex, "Conflito ao gravar a revisão {Revision} do documento {DocPageId}; tentando de novo.", accepted.Revision, docPageId);
                        continue;
                    }

                    await PruneAsync(docPageId, accepted.Revision, cancellationToken);
                    await notifier.PublishAsync(new DocChange(docPageId, accepted.Revision, clientId, DocChangeKind.Operation), cancellationToken);
                    return DocSubmitResult.Ack(accepted.Revision);

                case SubmitDecision.Behind:
                    var missing = await OperationsSince(db, docPageId, baseRevision, cancellationToken);
                    return missing is null
                        ? DocSubmitResult.Resync("O histórico que faltava já foi descartado.")
                        : DocSubmitResult.Behind(missing);

                case SubmitDecision.AlreadySeeded:
                    return DocSubmitResult.Seeded();

                case SubmitDecision.Rejected rejected:
                    return DocSubmitResult.Rejected(rejected.Reason);
            }
        }

        return DocSubmitResult.Busy();
    }

    public async Task<DocCatchUpResult> GetOperationsSinceAsync(Guid workspaceId, Guid docPageId, long sinceRevision, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var head = await FindTextDoc(db, workspaceId, docPageId)
            .Select(d => (long?)d.Revision)
            .FirstOrDefaultAsync(cancellationToken);

        if (head is null || sinceRevision < 0 || sinceRevision > head)
        {
            return new DocCatchUpResult("resync");
        }
        if (sinceRevision == head)
        {
            return new DocCatchUpResult("ok", []);
        }

        var ops = await OperationsSince(db, docPageId, sinceRevision, cancellationToken);
        return ops is null ? new DocCatchUpResult("resync") : new DocCatchUpResult("ok", ops);
    }

    /// <summary>
    /// Grava o HTML do modo leitura gerado pelo editor na <paramref name="revision"/>. Só aceita
    /// revisão mais nova que a do HTML atual e que já exista, para que um editor atrasado não
    /// substitua o texto por uma versão velha.
    /// </summary>
    /// <remarks>
    /// O HTML vem do navegador, como sempre veio, e passa pelo mesmo sanitizador antes de ser
    /// gravado. Ele é derivado: o documento de verdade é o Delta.
    /// </remarks>
    public async Task<bool> SaveHtmlAsync(Guid workspaceId, Guid docPageId, long revision, string html, CancellationToken cancellationToken = default)
    {
        var sanitized = HtmlContentSanitizer.Sanitize(html);
        if (sanitized.Length > DocPage.MaxContentHtmlLength)
        {
            return false;
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var doc = await FindTextDoc(db, workspaceId, docPageId).FirstOrDefaultAsync(cancellationToken);
            if (doc is null || revision <= doc.HtmlRevision || revision > doc.Revision)
            {
                return false;
            }

            doc.ContentHtml = sanitized;
            doc.HtmlRevision = revision;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                continue; // uma operação entrou no meio; relê e confere a revisão de novo
            }

            await notifier.PublishAsync(new DocChange(docPageId, revision, Guid.Empty, DocChangeKind.Content), cancellationToken);
            return true;
        }

        return false;
    }

    private static IQueryable<DocPage> FindTextDoc(AppDbContext db, Guid workspaceId, Guid docPageId) =>
        db.DocPages.Where(d => d.Id == docPageId && d.WorkspaceId == workspaceId && d.Type == DocPageType.Text);

    /// <summary>
    /// Operações depois de <paramref name="sinceRevision"/>, em ordem; <c>null</c> se a primeira
    /// que falta já foi podada (aí não há como alcançar pelo log).
    /// </summary>
    private static async Task<List<DocOperationDto>?> OperationsSince(AppDbContext db, Guid docPageId, long sinceRevision, CancellationToken cancellationToken)
    {
        var ops = await db.DocOperations
            .AsNoTracking()
            .Where(o => o.DocPageId == docPageId && o.Revision > sinceRevision)
            .OrderBy(o => o.Revision)
            .Take(MaxOperationsPerResponse)
            .Select(o => new DocOperationDto(o.Revision, o.ClientId, o.ClientSeq, o.ChangeJson))
            .ToListAsync(cancellationToken);

        if (ops.Count > 0 && ops[0].Revision != sinceRevision + 1)
        {
            return null;
        }
        return ops;
    }

    /// <summary>
    /// A cada 100 revisões, descarta o que passou de <see cref="KeptOperations"/>. O documento não
    /// depende do log; só quem reconecta muito atrasado perde o catch-up e recarrega.
    /// </summary>
    private async Task PruneAsync(Guid docPageId, long revision, CancellationToken cancellationToken)
    {
        if (revision % 100 != 0 || revision <= KeptOperations)
        {
            return;
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var threshold = revision - KeptOperations;
            var old = await db.DocOperations
                .Where(o => o.DocPageId == docPageId && o.Revision <= threshold)
                .ToListAsync(cancellationToken);
            db.DocOperations.RemoveRange(old);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Poda é faxina: a operação do usuário já foi aceita e não pode falhar por causa dela.
            logger.LogWarning(ex, "Falha ao podar o log do documento {DocPageId}.", docPageId);
        }
    }
}
