using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers M12 (uploads validated against their declared content type) and pins the existing
/// path-traversal defense so a future change to the filename handling cannot quietly remove it.
/// </summary>
public class AttachmentStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "nexus-attachment-tests", Guid.NewGuid().ToString("N"));

    private sealed class FakeEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Nexus.Web.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
    }

    private AttachmentStorageService CreateService()
    {
        Directory.CreateDirectory(root);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["UPLOADS_PATH"] = "uploads" })
            .Build();

        return new AttachmentStorageService(new FakeEnvironment(root), config);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x25, 0x00, 0x00];
    private static readonly byte[] HtmlBytes = "<html><script>alert(1)</script>"u8.ToArray();

    // ---- content-type verification -------------------------------------------------------

    [Fact]
    public void DeclaredTypeMatchingBytes_IsAccepted()
    {
        Assert.True(AttachmentStorageService.MatchesDeclaredContentType("image/png", PngBytes));
        Assert.True(AttachmentStorageService.MatchesDeclaredContentType("application/pdf", PdfBytes));
    }

    /// <summary>
    /// The actual attack this closes: HTML (or any other payload) uploaded while claiming to be
    /// an image. Avatars are served back inline with their stored content type.
    /// </summary>
    [Fact]
    public void HtmlDisguisedAsAnImage_IsRejected()
    {
        Assert.False(AttachmentStorageService.MatchesDeclaredContentType("image/png", HtmlBytes));
        Assert.False(AttachmentStorageService.MatchesDeclaredContentType("image/jpeg", HtmlBytes));
        Assert.False(AttachmentStorageService.MatchesDeclaredContentType("image/webp", HtmlBytes));
    }

    [Fact]
    public void PdfDisguisedAsAnImage_IsRejected()
    {
        Assert.False(AttachmentStorageService.MatchesDeclaredContentType("image/png", PdfBytes));
    }

    // RIFF alone is shared with .wav/.avi; the WEBP tag at offset 8 is what disambiguates.
    [Fact]
    public void RiffContainerThatIsNotWebp_IsRejected()
    {
        byte[] wav = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45];
        byte[] webp = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];

        Assert.False(AttachmentStorageService.MatchesDeclaredContentType("image/webp", wav));
        Assert.True(AttachmentStorageService.MatchesDeclaredContentType("image/webp", webp));
    }

    // Signature-less types stay accepted — rejecting them would break legitimate .txt/.csv uploads.
    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/csv")]
    public void SignaturelessTextTypes_AreAccepted(string contentType)
    {
        Assert.True(AttachmentStorageService.MatchesDeclaredContentType(contentType, "col1,col2\n1,2"u8));
    }

    [Fact]
    public void EmptyFileClaimingToBeAnImage_IsRejected()
    {
        Assert.False(AttachmentStorageService.MatchesDeclaredContentType("image/png", ReadOnlySpan<byte>.Empty));
    }

    // ---- end-to-end through SaveAsync ----------------------------------------------------

    [Fact]
    public async Task SaveAsync_WritesTheCompleteFileWhenTheTypeMatches()
    {
        var service = CreateService();
        var owner = Guid.NewGuid();
        var attachment = Guid.NewGuid();

        // Deliberately longer than the peeked header, to prove the buffered prefix is replayed
        // and the file is not truncated or duplicated.
        var content = PngBytes.Concat(Enumerable.Range(0, 5000).Select(i => (byte)(i % 251))).ToArray();

        using var stream = new MemoryStream(content);
        var relative = await service.SaveAsync(owner, attachment, "photo.png", stream, "image/png", TestContext.Current.CancellationToken);

        var written = await File.ReadAllBytesAsync(service.GetFullPath(relative), TestContext.Current.CancellationToken);
        Assert.Equal(content, written);
    }

    [Fact]
    public async Task SaveAsync_RejectsMismatchedContentAndLeavesNoFileBehind()
    {
        var service = CreateService();
        var owner = Guid.NewGuid();
        var attachment = Guid.NewGuid();

        using var stream = new MemoryStream(HtmlBytes);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.SaveAsync(owner, attachment, "evil.png", stream, "image/png", TestContext.Current.CancellationToken));

        // Nothing may be persisted for a rejected upload.
        var ownerDir = Path.Combine(root, "uploads", owner.ToString());
        Assert.False(Directory.Exists(ownerDir) && Directory.EnumerateFiles(ownerDir).Any());
    }

    [Fact]
    public async Task SaveAsync_SkipsVerificationWhenNoTypeIsDeclared()
    {
        var service = CreateService();
        using var stream = new MemoryStream(HtmlBytes);

        var relative = await service.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), "notes.txt", stream, null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(service.GetFullPath(relative)));
    }

    // ---- path traversal (pre-existing defense, pinned) ------------------------------------

    /// <summary>
    /// The stored path is built from server-generated GUIDs plus a filtered extension, so a
    /// hostile filename cannot escape the uploads root. Asserted on the resolved absolute path
    /// rather than the string, so any future change that reintroduces the original filename fails.
    /// </summary>
    [Theory]
    [InlineData(@"..\..\..\windows\system32\evil.exe")]
    [InlineData("../../../etc/passwd")]
    [InlineData("....//....//evil.png")]
    [InlineData("normal.png")]
    public async Task SaveAsync_NeverEscapesTheUploadsRoot(string hostileFileName)
    {
        var service = CreateService();
        using var stream = new MemoryStream(PngBytes);

        var relative = await service.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), hostileFileName, stream, "image/png", TestContext.Current.CancellationToken);
        var resolved = Path.GetFullPath(service.GetFullPath(relative));
        var uploadsRoot = Path.GetFullPath(Path.Combine(root, "uploads"));

        Assert.StartsWith(uploadsRoot, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContentTypeAllowlist_ExcludesDangerousTypes()
    {
        Assert.False(AttachmentStorageService.IsAllowedContentType("text/html"));
        Assert.False(AttachmentStorageService.IsAllowedContentType("image/svg+xml"));
        Assert.False(AttachmentStorageService.IsAllowedContentType("application/x-msdownload"));
        Assert.False(AttachmentStorageService.IsAllowedContentType(null));
        Assert.True(AttachmentStorageService.IsAllowedContentType("image/png"));
    }
}
