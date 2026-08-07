namespace Nexus.Web.Services;

// Centralizes attachment path resolution so every uploader (TaskDetailPanel.razor,
// WorkspaceChat.razor) and every download endpoint (Program.cs) never drift apart on
// where a file physically lives. ownerId is just a folder name — it can be a WorkItemId,
// a ChatMessageId, or any other server-generated Guid; this service doesn't care which.
public class AttachmentStorageService(IWebHostEnvironment env, IConfiguration configuration)
{
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    // Browser-supplied Content-Type is client input, not verified against the actual file
    // bytes — this allowlist only narrows what gets persisted and later served back with
    // that same Content-Type header; it isn't a substitute for the sandboxing already done
    // in AttachmentStorageService.SaveAsync (GUID-only physical paths) and Program.cs
    // (download responses always set Content-Disposition: attachment, so nothing here can
    // render inline in a browser regardless of the stored type).
    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif",
        "application/pdf",
        "text/plain", "text/csv",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/zip",
    };

    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && AllowedContentTypes.Contains(contentType);

    // Magic-number prefixes for the binary formats in the allowlist above. The browser-reported
    // Content-Type is client input and was previously the only thing checked, so a file could be
    // stored and later served back under a type that has nothing to do with its actual bytes.
    // Serving is already hardened (Content-Disposition: attachment plus X-Content-Type-Options:
    // nosniff), so this is defense in depth rather than the sole control — but it is what stops
    // an executable or HTML payload from being persisted while wearing an "image/png" label.
    //
    // The text/* and Office-XML types are intentionally absent: plain text and CSV have no
    // signature at all, and .docx/.xlsx/.zip share the same PK.. ZIP header, which is covered by
    // the ZIP entry.
    private static readonly (string ContentType, byte[][] Signatures)[] KnownSignatures =
    [
        ("image/png",  [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
        ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
        ("image/gif",  [[0x47, 0x49, 0x46, 0x38, 0x37, 0x61], [0x47, 0x49, 0x46, 0x38, 0x39, 0x61]]),
        ("image/webp", [[0x52, 0x49, 0x46, 0x46]]), // RIFF....WEBP — container header checked below
        ("application/pdf", [[0x25, 0x50, 0x44, 0x46, 0x2D]]),
        ("application/zip", [[0x50, 0x4B, 0x03, 0x04], [0x50, 0x4B, 0x05, 0x06], [0x50, 0x4B, 0x07, 0x08]]),
        ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [[0x50, 0x4B, 0x03, 0x04]]),
        ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [[0x50, 0x4B, 0x03, 0x04]]),
        ("application/msword", [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]]),
        ("application/vnd.ms-excel", [[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]]),
    ];

    /// <summary>
    /// Longest signature this class inspects; callers only need to buffer this many bytes.
    /// </summary>
    public const int SignatureBytesNeeded = 12;

    /// <summary>
    /// Checks the leading bytes of an upload against the declared content type. Returns true for
    /// types that have no reliable signature (plain text, CSV), which are harmless to store and
    /// are never served inline.
    /// </summary>
    public static bool MatchesDeclaredContentType(string? contentType, ReadOnlySpan<byte> header)
    {
        if (contentType is null)
        {
            return false;
        }

        var entry = KnownSignatures.FirstOrDefault(s => string.Equals(s.ContentType, contentType, StringComparison.OrdinalIgnoreCase));
        if (entry.Signatures is null)
        {
            // No signature defined for this (already allowlisted) type — e.g. text/plain, text/csv.
            return true;
        }

        foreach (var signature in entry.Signatures)
        {
            if (header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature))
            {
                // WebP is a RIFF container; the bare "RIFF" prefix is also used by .wav and .avi,
                // so require the format tag at offset 8 too.
                if (string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase))
                {
                    return header.Length >= 12
                        && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;
                }

                return true;
            }
        }

        return false;
    }

    private string RootPath => Path.Combine(env.ContentRootPath, configuration["UPLOADS_PATH"] ?? "App_Data/uploads");

    /// <summary>
    /// Streams an upload to disk under a server-generated path.
    /// </summary>
    /// <param name="declaredContentType">
    /// The browser-reported content type. Verified against the file's actual leading bytes before
    /// anything is written; pass null to skip that check only where no type was declared.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The content does not match <paramref name="declaredContentType"/>. Thrown before the file
    /// is created, so a rejected upload leaves nothing behind.
    /// </exception>
    public async Task<string> SaveAsync(
        Guid ownerId,
        Guid attachmentId,
        string originalFileName,
        Stream content,
        string? declaredContentType,
        CancellationToken ct = default)
    {
        // Peek the header before creating the file so a mismatch cannot leave a partial artifact
        // on disk. Buffered and replayed afterwards because IBrowserFile streams are forward-only.
        var header = new byte[SignatureBytesNeeded];
        var headerLength = await content.ReadAtLeastAsync(header, SignatureBytesNeeded, throwOnEndOfStream: false, ct);

        if (declaredContentType is not null
            && !MatchesDeclaredContentType(declaredContentType, header.AsSpan(0, headerLength)))
        {
            throw new InvalidDataException(
                $"Upload content does not match the declared content type '{declaredContentType}'.");
        }

        var ext = SanitizeExtension(Path.GetExtension(originalFileName));
        var relativePath = Path.Combine(ownerId.ToString(), attachmentId + ext);
        var fullPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await fileStream.WriteAsync(header.AsMemory(0, headerLength), ct);
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
