using Microsoft.Extensions.Options;

namespace Nexus.Web.Services;

/// <summary>
/// Bound from the "Support" configuration section. Either value may be empty; the channel it
/// feeds is simply not shown.
/// </summary>
public sealed class SupportOptions
{
    /// <summary>WhatsApp number in international format, e.g. "+55 53 99999-0000".</summary>
    public string? WhatsAppNumber { get; set; }

    public string? Email { get; set; }
}

/// <summary>
/// What the person is asking about, sent along with the support message so the first reply does
/// not have to be "which workspace? which version?". Only things the person already sees on screen.
/// </summary>
public sealed record SupportContext(string? WorkspaceName = null, string? PlanName = null, string? ErrorCode = null);

/// <summary>
/// Builds the "talk to the developer" links (#55): WhatsApp with a prefilled message, and e-mail.
/// </summary>
public class SupportLinks(IOptions<SupportOptions> options)
{
    private readonly string? whatsAppDigits = Digits(options.Value.WhatsAppNumber);
    private readonly string? email = string.IsNullOrWhiteSpace(options.Value.Email) ? null : options.Value.Email.Trim();

    public bool IsConfigured => whatsAppDigits is not null || email is not null;

    public string? Email => email;

    public string? WhatsAppUrl(SupportContext context) =>
        whatsAppDigits is null ? null : $"https://wa.me/{whatsAppDigits}?text={Uri.EscapeDataString(Message(context))}";

    public string? MailtoUrl(SupportContext context) =>
        email is null
            ? null
            : $"mailto:{email}?subject={Uri.EscapeDataString("Suporte Nexus")}&body={Uri.EscapeDataString(Message(context))}";

    internal static string Message(SupportContext context)
    {
        var lines = new List<string> { "Olá! Preciso de ajuda com o Nexus.", "" };
        if (!string.IsNullOrWhiteSpace(context.WorkspaceName))
        {
            lines.Add(context.PlanName is null
                ? $"Workspace: {context.WorkspaceName}"
                : $"Workspace: {context.WorkspaceName} (plano {context.PlanName})");
        }
        if (!string.IsNullOrWhiteSpace(context.ErrorCode))
        {
            lines.Add($"Código do erro: {context.ErrorCode}");
        }
        lines.Add($"Versão: {AppVersion.Current.Version}");
        return string.Join('\n', lines);
    }

    // wa.me only takes the bare international number: no "+", spaces or punctuation.
    private static string? Digits(string? number)
    {
        var digits = new string((number ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
