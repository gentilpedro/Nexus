using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Nexus.Web.Services;

public class BrevoOptions
{
    public string Host { get; set; } = "smtp-relay.brevo.com";
    public int Port { get; set; } = 587;
    public string Login { get; set; } = "";
    public string SmtpKey { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "Nexus";
}

// Raw SMTP send through Brevo's relay, shared by BrevoEmailSender (Identity's confirmation/reset
// emails) and WorkspaceInviteService (invite/added-to-workspace emails) — one place owning the
// SmtpClient lifecycle and the "an email hiccup shouldn't break the page that triggered it" policy.
public class BrevoMailer(IOptions<BrevoOptions> options, ILogger<BrevoMailer> logger)
{
    private readonly BrevoOptions options = options.Value;

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.SenderName, options.SenderEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(options.Login, options.SmtpKey);
            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {ToEmail} via Brevo.", toEmail);
        }
    }
}
