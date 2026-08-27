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

    // Wall-clock ceiling for one send attempt, covering connect + authenticate + send + quit.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    // virtual so tests can substitute a recording double: the real implementation opens an SMTP
    // connection, which no unit test should be doing.
    public virtual async Task<bool> SendAsync(string toEmail, string subject, string htmlBody)
    {
        // No relay configured (local dev, tests): fail fast instead of spending the full 15s
        // timeout dialling a host we have no credentials for. Callers already treat false as
        // "recorded but not delivered" and tell the user to reach the person another way.
        if (string.IsNullOrWhiteSpace(options.SmtpKey) || string.IsNullOrWhiteSpace(options.SenderEmail))
        {
            logger.LogWarning("Skipped sending email to {ToEmail}: Brevo SMTP is not configured.", toEmail);
            return false;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.SenderName, options.SenderEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            // Bounded end to end. MailKit's default Timeout is 2 minutes and applies per socket
            // operation, so an unresponsive relay could stall a caller far longer than that —
            // and several callers await this inline on a user-facing path (sending an invite,
            // the chat/notification flows), meaning a hung SMTP connection blocks the user's
            // interaction, not just the e-mail. The CancellationTokenSource covers the whole
            // connect/authenticate/send sequence, which the per-operation timeout does not.
            client.Timeout = (int)SendTimeout.TotalMilliseconds;
            using var cts = new CancellationTokenSource(SendTimeout);

            await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTls, cts.Token);
            await client.AuthenticateAsync(options.Login, options.SmtpKey, cts.Token);
            await client.SendAsync(message, cts.Token);
            await client.DisconnectAsync(quit: true, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Timed out rather than failed outright. Same contract as any other send failure:
            // return false and let the caller carry on — e-mail is never the critical path here.
            logger.LogError("Timed out sending email to {ToEmail} via Brevo after {Timeout}s.", toEmail, SendTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {ToEmail} via Brevo.", toEmail);
            return false;
        }
    }
}
