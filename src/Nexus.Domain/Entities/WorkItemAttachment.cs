namespace Nexus.Domain.Entities;

public class WorkItemAttachment
{
    public Guid Id { get; set; }

    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;

    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    // Relative path under the uploads root — never derived from FileName, to avoid
    // path traversal via a malicious original filename. See AttachmentStorageService.
    public string StoragePath { get; set; } = "";

    public string? UploadedByUserId { get; set; }
    public ApplicationUser? UploadedByUser { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}
