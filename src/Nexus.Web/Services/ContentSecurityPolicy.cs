using System.Security.Cryptography;

namespace Nexus.Web.Services;

// Content-Security-Policy. The app renders user-authored HTML on doc pages as a raw MarkupString
// (Doc.razor), so HtmlSanitizer is the primary XSS defense and this is the second line: even if
// something slips past the sanitizer, an injected <script> has no way to execute and no origin it
// is allowed to exfiltrate to.
public static class ContentSecurityPolicy
{
    // HttpContext.Items key holding this request's nonce; App.razor reads it for <ImportMap>.
    public const string NonceItemKey = "csp-nonce";

    // Cloudflare Web Analytics, which the proxy injects into every page: the beacon script and
    // the endpoint it reports to. Without these the browser blocks it and logs an error on every
    // page (#62).
    private const string CloudflareBeaconScript = "https://static.cloudflareinsights.com";
    private const string CloudflareBeaconReport = "https://cloudflareinsights.com";

    /// <summary>128 random bits, base64 — a fresh one per response.</summary>
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <remarks>
    /// script-src has no 'unsafe-inline'. The only inline script left is the import map that
    /// &lt;ImportMap /&gt; writes (it maps module URLs to their fingerprinted files, so a deploy
    /// never leaves the browser on cached JS); it carries this response's nonce, which an
    /// injected script cannot know (#62). style-src does keep 'unsafe-inline': Quill and Chart.js
    /// both set element styles at runtime, and Blazor injects inline styles of its own.
    ///
    /// connect-src must allow ws:/wss: — Blazor Server's entire interactive layer is a SignalR
    /// WebSocket back to this same origin.
    /// </remarks>
    public static string Build(string nonce) => string.Join("; ",
        "default-src 'self'",
        $"script-src 'self' 'nonce-{nonce}' {CloudflareBeaconScript}",
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data:",
        "font-src 'self' data:",
        $"connect-src 'self' ws: wss: {CloudflareBeaconReport}",
        "form-action 'self'",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        "object-src 'none'");
}
