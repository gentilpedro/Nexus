namespace Nexus.Web.Services;

// Centralizes sanitization of user-authored HTML (doc pages) before it is persisted —
// the same content is later rendered as raw MarkupString for every workspace member who
// opens the page, so this is the layer that doesn't depend on trusting the Quill client.
public static class HtmlContentSanitizer
{
    private static readonly Ganss.Xss.HtmlSanitizer Sanitizer = new();

    public static string Sanitize(string html) => Sanitizer.Sanitize(html);
}
