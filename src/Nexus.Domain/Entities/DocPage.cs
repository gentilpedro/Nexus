namespace Nexus.Domain.Entities;

public class DocPage
{
    // Storage ceilings for the two user-authored payloads on this entity. Both columns were
    // unbounded text; these values are enforced by the EF configuration and re-checked before
    // saving, so the limit does not depend on the client behaving.
    public const int MaxContentHtmlLength = 1_000_000;
    public const int MaxGridDataJsonLength = 500_000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Title { get; set; } = "";
    public DocPageType Type { get; set; }

    // Text pages use ContentHtml; Spreadsheet pages use GridDataJson; File pages use the
    // File* properties below — only the set matching Type is populated, same idea as
    // WorkItem.Type discriminating one entity.
    public string? ContentHtml { get; set; }
    public string? GridDataJson { get; set; }

    // Set only when Type == File — an imported file stored the same way as a chat
    // attachment (see AttachmentStorageService), just owned by a DocPage instead of a
    // ChatMessage.
    public string? FileName { get; set; }
    public string? FileContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? FileStoragePath { get; set; }

    // Who created this page — set once at creation, never overwritten on edit.
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
    public ApplicationUser? UpdatedByUser { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Edição colaborativa (Type == Text). O documento de verdade passa a ser DeltaJson, no formato
    // do Quill, e Revision conta as operações aceitas (ver DocSequencer). ContentHtml vira dado
    // derivado para o modo leitura, o sumário e a busca, e HtmlRevision diz de qual revisão ele é.
    //
    // DeltaJson nulo com Revision 0 é um documento de antes da edição colaborativa: o primeiro
    // editor que abrir converte o ContentHtml e semeia o Delta.
    public const int MaxDeltaJsonLength = 4_000_000;

    public string? DeltaJson { get; set; }
    public long Revision { get; set; }
    public long HtmlRevision { get; set; }

    /// <summary>Ainda não semeado: tem conteúdo em HTML antigo e nenhuma operação.</summary>
    public bool NeedsSeed => Revision == 0 && DeltaJson is null && !string.IsNullOrWhiteSpace(ContentHtml);
}
