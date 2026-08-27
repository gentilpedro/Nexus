using System.Net;
using System.Text;

namespace Nexus.Web.Services;

public sealed record EmailButton(string Label, string Url);

public sealed record EmailDetail(string Label, string Value);

/// <summary>The two bodies of one message: HTML for clients that render it, plain text for the rest.</summary>
public sealed record EmailContent(string Html, string Text);

// One layout for every transactional e-mail Nexus sends (invite, account confirmation, password
// reset, due-date warning), so they look like they come from the same product.
//
// Everything here is deliberately old-fashioned HTML: nested tables, inline styles, no external
// CSS, no images. Mail clients are not browsers — Gmail drops <style> blocks in several contexts,
// Outlook renders through Word, and remote images stay blocked until the reader opts in. A layout
// that survives all of that is a table with its styles written on the elements.
//
// Callers pass PLAIN TEXT and this class escapes it. That is the point: the previous bodies were
// concatenated at four call sites, each responsible for remembering HtmlEncode on user-controlled
// values like a workspace name or a task title. One call site forgetting is an injection into
// someone else's inbox.
public static class EmailTemplate
{
    // Same tokens as wwwroot/app.css, hard-coded because an e-mail cannot read a stylesheet.
    private const string ColorPrimary = "#2563EB";
    private const string ColorBg = "#F7F8FC";
    private const string ColorSurface = "#FFFFFF";
    private const string ColorText = "#111827";
    private const string ColorTextSecondary = "#4B5563";
    private const string ColorTextMuted = "#6B7280";
    private const string ColorTextFaint = "#9CA3AF";
    private const string ColorBorder = "#E5E7EB";
    private const string ColorDivider = "#EEF2F7";

    private const string FontStack =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <param name="preview">
    /// The line the inbox list shows next to the subject. Without one, clients fall back to
    /// whatever text comes first in the body — which here would be the wordmark.
    /// </param>
    public static EmailContent Render(
        string title,
        string preview,
        IReadOnlyList<string> paragraphs,
        EmailButton? button = null,
        IReadOnlyList<EmailDetail>? details = null,
        string? footnote = null) =>
        new(
            RenderHtml(title, preview, paragraphs, button, details, footnote),
            RenderText(title, paragraphs, button, details, footnote));

    private static string RenderHtml(
        string title,
        string preview,
        IReadOnlyList<string> paragraphs,
        EmailButton? button,
        IReadOnlyList<EmailDetail>? details,
        string? footnote)
    {
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        // Asks Apple Mail and Outlook not to auto-invert the palette. Gmail inverts regardless,
        // which is why every colour below is stated explicitly instead of left to inherit.
        html.Append("<meta name=\"color-scheme\" content=\"light\">");
        html.Append("<meta name=\"supported-color-schemes\" content=\"light\">");
        html.Append($"<title>{Enc(title)}</title></head>");
        html.Append($"<body style=\"margin:0;padding:0;background-color:{ColorBg};\">");

        // Hidden preheader. The padding characters that follow stop the client from pulling more
        // body text in after it — they are zero-width and invisible in every client.
        html.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;height:0;width:0;\">");
        html.Append(Enc(preview));
        html.Append(string.Concat(Enumerable.Repeat("&#8199;&#65279;&#847;", 30)));
        html.Append("</div>");

        html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:{ColorBg};margin:0;padding:32px 12px;\"><tr><td align=\"center\">");
        html.Append($"<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;background-color:{ColorSurface};border:1px solid {ColorBorder};border-radius:12px;\">");

        // Accent rule across the top of the card.
        html.Append($"<tr><td style=\"height:4px;background-color:{ColorPrimary};font-size:0;line-height:0;border-radius:12px 12px 0 0;\">&nbsp;</td></tr>");

        html.Append($"<tr><td style=\"padding:28px 32px 0 32px;font-family:{FontStack};font-size:18px;font-weight:700;color:{ColorPrimary};\">Nexus</td></tr>");

        html.Append($"<tr><td style=\"padding:18px 32px 0 32px;font-family:{FontStack};font-size:22px;line-height:30px;font-weight:700;color:{ColorText};\">{Enc(title)}</td></tr>");

        foreach (var paragraph in paragraphs)
        {
            html.Append($"<tr><td style=\"padding:14px 32px 0 32px;font-family:{FontStack};font-size:15px;line-height:24px;color:{ColorTextSecondary};\">{Enc(paragraph)}</td></tr>");
        }

        if (details is { Count: > 0 })
        {
            html.Append("<tr><td style=\"padding:20px 32px 0 32px;\">");
            html.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:{ColorBg};border:1px solid {ColorBorder};border-radius:10px;\">");
            for (var i = 0; i < details.Count; i++)
            {
                var paddingTop = i == 0 ? 14 : 0;
                html.Append($"<tr><td style=\"padding:{paddingTop}px 16px 14px 16px;font-family:{FontStack};\">");
                html.Append($"<div style=\"font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:{ColorTextMuted};\">{Enc(details[i].Label)}</div>");
                html.Append($"<div style=\"margin-top:3px;font-size:15px;font-weight:600;color:{ColorText};\">{Enc(details[i].Value)}</div>");
                html.Append("</td></tr>");
            }
            html.Append("</table></td></tr>");
        }

        if (button is not null)
        {
            html.Append("<tr><td style=\"padding:26px 32px 0 32px;\">");
            html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>");
            html.Append($"<td style=\"background-color:{ColorPrimary};border-radius:8px;\">");
            html.Append($"<a href=\"{Enc(button.Url)}\" style=\"display:inline-block;padding:13px 26px;font-family:{FontStack};font-size:15px;font-weight:600;color:#FFFFFF;text-decoration:none;\">{Enc(button.Label)}</a>");
            html.Append("</td></tr></table></td></tr>");

            // Buttons get stripped, rewritten by scanners and mis-clicked often enough that the
            // address has to be readable on its own.
            html.Append($"<tr><td style=\"padding:16px 32px 0 32px;font-family:{FontStack};font-size:13px;line-height:20px;color:{ColorTextMuted};\">");
            html.Append("Se o botão não abrir, copie e cole este endereço no navegador:<br>");
            html.Append($"<span style=\"color:{ColorPrimary};word-break:break-all;\">{Enc(button.Url)}</span>");
            html.Append("</td></tr>");
        }

        if (!string.IsNullOrWhiteSpace(footnote))
        {
            html.Append($"<tr><td style=\"padding:20px 32px 0 32px;font-family:{FontStack};font-size:13px;line-height:20px;color:{ColorTextMuted};\">{Enc(footnote)}</td></tr>");
        }

        html.Append("<tr><td style=\"padding:26px 32px 28px 32px;\">");
        html.Append($"<div style=\"border-top:1px solid {ColorDivider};padding-top:16px;font-family:{FontStack};font-size:12px;line-height:18px;color:{ColorTextFaint};\">");
        html.Append("Você recebeu este e-mail porque alguém usa o Nexus com este endereço. Se não reconhece esta mensagem, pode ignorá-la.");
        html.Append("</div></td></tr>");

        html.Append("</table></td></tr></table></body></html>");

        return html.ToString();
    }

    private static string RenderText(
        string title,
        IReadOnlyList<string> paragraphs,
        EmailButton? button,
        IReadOnlyList<EmailDetail>? details,
        string? footnote)
    {
        var text = new StringBuilder();
        text.AppendLine(title).AppendLine();

        foreach (var paragraph in paragraphs)
        {
            text.AppendLine(paragraph).AppendLine();
        }

        if (details is { Count: > 0 })
        {
            foreach (var detail in details)
            {
                text.AppendLine($"{detail.Label}: {detail.Value}");
            }

            text.AppendLine();
        }

        if (button is not null)
        {
            text.AppendLine($"{button.Label}: {button.Url}").AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(footnote))
        {
            text.AppendLine(footnote).AppendLine();
        }

        text.AppendLine("--");
        text.AppendLine("Nexus. Você recebeu este e-mail porque alguém usa o Nexus com este endereço.");

        return text.ToString();
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
