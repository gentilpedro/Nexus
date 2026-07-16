namespace Nexus.Domain.Entities;

public class DocPage
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Title { get; set; } = "";
    public DocPageType Type { get; set; }

    // Text pages use ContentHtml; Spreadsheet pages use GridDataJson — only one is
    // populated depending on Type, same idea as WorkItem.Type discriminating one entity.
    public string? ContentHtml { get; set; }
    public string? GridDataJson { get; set; }

    // Who created this page — set once at creation, never overwritten on edit.
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
    public ApplicationUser? UpdatedByUser { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
