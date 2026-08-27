using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Pins the escaping contract of the shared e-mail layout.
/// </summary>
/// <remarks>
/// Workspace names and task titles are free text typed by users and end up in someone else's
/// inbox. Before this template each call site escaped its own values by hand, so the guarantee
/// was "four places remembered"; now it is one place, and this is the test that holds it there.
/// </remarks>
public class EmailTemplateTests
{
    [Fact]
    public void Escapes_user_text_in_every_slot()
    {
        var evil = "<script>alert('x')</script>";

        var content = EmailTemplate.Render(
            title: evil,
            preview: evil,
            paragraphs: [evil],
            button: new EmailButton(evil, "https://usenexus.runasp.net/convite/abc"),
            details: [new EmailDetail(evil, evil)],
            footnote: evil);

        Assert.DoesNotContain("<script>", content.Html);
        Assert.Contains("&lt;script&gt;", content.Html);
    }

    [Fact]
    public void Escapes_a_url_that_would_break_out_of_the_href_attribute()
    {
        var content = EmailTemplate.Render(
            title: "T",
            preview: "P",
            paragraphs: ["body"],
            button: new EmailButton("Abrir", "https://exemplo.test/\" onclick=\"steal()"));

        Assert.DoesNotContain("onclick=\"steal()\"", content.Html);
        Assert.Contains("&quot; onclick=&quot;steal()", content.Html);
    }

    [Fact]
    public void Keeps_the_link_reachable_without_the_button()
    {
        const string url = "https://usenexus.runasp.net/convite/token-123";

        var content = EmailTemplate.Render(
            title: "Convite",
            preview: "Convite",
            paragraphs: ["texto"],
            button: new EmailButton("Aceitar convite", url));

        // Once in the href, once as copyable text for clients that strip the button.
        Assert.Equal(2, CountOccurrences(content.Html, url));
        Assert.Contains(url, content.Text);
    }

    [Fact]
    public void Plain_text_alternative_carries_the_same_information()
    {
        var content = EmailTemplate.Render(
            title: "Você foi convidado para um workspace",
            preview: "Convite",
            paragraphs: ["Aceite o convite para participar."],
            button: new EmailButton("Aceitar convite", "https://usenexus.runasp.net/convite/abc"),
            details: [new EmailDetail("Workspace", "Time de Produto")],
            footnote: "Este convite expira em 7 dias.");

        Assert.Contains("Você foi convidado para um workspace", content.Text);
        Assert.Contains("Aceite o convite para participar.", content.Text);
        Assert.Contains("Workspace: Time de Produto", content.Text);
        Assert.Contains("Este convite expira em 7 dias.", content.Text);
        Assert.DoesNotContain("<", content.Text);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
