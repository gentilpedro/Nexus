namespace Nexus.Domain.Entities;

public class ChatMessageAttachment
{
    public Guid Id { get; set; }

    public Guid ChatMessageId { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;

    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    // Relative path under the uploads root — see AttachmentStorageService.
    public string StoragePath { get; set; } = "";
}
