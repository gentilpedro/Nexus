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
}
