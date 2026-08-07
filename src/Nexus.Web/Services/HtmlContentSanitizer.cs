namespace Nexus.Web.Services;

// Centralizes sanitization of user-authored HTML (doc pages) before it is persisted —
// the same content is later rendered as raw MarkupString for every workspace member who
// opens the page, so this is the layer that doesn't depend on trusting the Quill client.
public static class HtmlContentSanitizer
{
    // A fresh instance per call.
    //
    // Ganss.Xss.HtmlSanitizer is not documented as thread-safe, and this runs on Blazor circuits
    // that genuinely execute concurrently. A data race here is a security bug rather than merely a
    // correctness one: the failure mode is unsanitized HTML reaching the database and, from there,
    // every workspace member's browser through MarkupString.
    //
    // A shared static instance had that race. A ThreadLocal removed it but retained one sanitizer
    // per thread-pool thread for the process lifetime. Constructing one is just building the
    // default allow-list hash sets, and the only caller is a document save that already performs a
    // database round trip — so the allocation is irrelevant next to the work surrounding it, and
    // this version has no shared state to reason about at all.
    public static string Sanitize(string html) => new Ganss.Xss.HtmlSanitizer().Sanitize(html);
}
