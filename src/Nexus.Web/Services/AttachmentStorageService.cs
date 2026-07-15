namespace Nexus.Web.Services;

// Centralizes attachment path resolution so every uploader (TaskDetailPanel.razor,
// WorkspaceChat.razor) and every download endpoint (Program.cs) never drift apart on
// where a file physically lives. ownerId is just a folder name — it can be a WorkItemId,
// a ChatMessageId, or any other server-generated Guid; this service doesn't care which.
public class AttachmentStorageService(IWebHostEnvironment env, IConfiguration configuration)
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    private string RootPath => Path.Combine(env.ContentRootPath, configuration["UPLOADS_PATH"] ?? "App_Data/uploads");

    public async Task<string> SaveAsync(Guid ownerId, Guid attachmentId, string originalFileName, Stream content, CancellationToken ct = default)
    {
        var ext = SanitizeExtension(Path.GetExtension(originalFileName));
        var relativePath = Path.Combine(ownerId.ToString(), attachmentId + ext);
        var fullPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);

        return relativePath;
    }

    public string GetFullPath(string relativePath) => Path.Combine(RootPath, relativePath);

    public void Delete(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    // The original filename never becomes part of the physical path — only a filtered
    // extension does — so a malicious filename (e.g. containing "..\\..\\") can't escape
    // the uploads root. The real path is built from server-generated GUIDs only.
    private static string SanitizeExtension(string ext)
    {
        if (string.IsNullOrEmpty(ext) || ext.Length > 10)
        {
            return "";
        }

        var clean = new string(ext.Where(c => char.IsLetterOrDigit(c) || c == '.').ToArray());
        if (clean.Length == 0)
        {
            return "";
        }

        return clean.StartsWith('.') ? clean : "." + clean;
    }
}
