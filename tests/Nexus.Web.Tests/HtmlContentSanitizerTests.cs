using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Doc page content is stored sanitized and later rendered with
/// <c>@((MarkupString)doc.ContentHtml)</c> for every workspace member, so this class is the only
/// thing standing between a malicious doc author and stored XSS against their whole workspace.
/// It had no tests (H3); L3 additionally changed the shared sanitizer instance to a
/// thread-local one, which these also exercise.
/// </summary>
public class HtmlContentSanitizerTests
{
    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<img src=x onerror=alert('xss')>")]
    [InlineData("<svg/onload=alert('xss')>")]
    [InlineData("<iframe src='https://evil.com'></iframe>")]
    [InlineData("<body onload=alert('xss')>")]
    [InlineData("<a href=\"javascript:alert('xss')\">click</a>")]
    [InlineData("<object data='evil.swf'></object>")]
    [InlineData("<embed src='evil.swf'>")]
    [InlineData("<form action='https://evil.com'><input name='x'></form>")]
    public void ActiveContent_IsStripped(string hostile)
    {
        var sanitized = HtmlContentSanitizer.Sanitize(hostile);

        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<object", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<embed", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    // The editor is a rich-text surface; stripping everything would break the feature.
    [Fact]
    public void LegitimateFormatting_IsPreserved()
    {
        const string html = "<h1>Título</h1><p><strong>negrito</strong> e <em>itálico</em></p><ul><li>um</li></ul>";

        var sanitized = HtmlContentSanitizer.Sanitize(html);

        Assert.Contains("<h1>", sanitized, StringComparison.Ordinal);
        Assert.Contains("<strong>", sanitized, StringComparison.Ordinal);
        Assert.Contains("<em>", sanitized, StringComparison.Ordinal);
        Assert.Contains("<li>", sanitized, StringComparison.Ordinal);
        Assert.Contains("Título", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeLinks_ArePreserved()
    {
        var sanitized = HtmlContentSanitizer.Sanitize("<a href=\"https://example.com\">link</a>");

        Assert.Contains("https://example.com", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyInput_IsHandled()
    {
        Assert.Equal("", HtmlContentSanitizer.Sanitize(""));
    }

    /// <summary>
    /// L3: the sanitizer was a single shared instance across all Blazor circuits. Ganss.Xss is
    /// not documented as thread-safe, and a race here fails open — unsanitized HTML reaching the
    /// database. Exercises the thread-local implementation under real parallelism.
    /// </summary>
    [Fact]
    public async Task ConcurrentSanitization_IsCorrectOnEveryThread()
    {
        const string hostile = "<p>ok</p><script>alert('xss')</script>";

        var results = await Task.WhenAll(
            Enumerable.Range(0, 200).Select(_ => Task.Run(() => HtmlContentSanitizer.Sanitize(hostile))));

        Assert.All(results, r =>
        {
            Assert.DoesNotContain("<script", r, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<p>ok</p>", r, StringComparison.Ordinal);
        });
    }
}
