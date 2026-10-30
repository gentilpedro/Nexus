using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Nexus.Web.Services;

public class SmtpOptions
{
    // In production this is the Aamora relay ("smtp" on the shared Docker network "mail"), which
    // forwards through Resend. Empty means "not configured": local dev and tests send nothing.
    public string Host { get; set; } = "";
    public int Port { get; set; } = 25;

    // The relay trusts its Docker network and takes no credentials. Kept so the same code can
    // talk to an authenticated server (a provider's 587) without a code change.
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";

    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "Nexus";
}

// Transactional e-mail over SMTP, shared by IdentityEmailSender (confirmation/reset emails),
// WorkspaceInviteService, CommercialContactService and DueDateNotificationHostedService — one
// place owning the "an email hiccup shouldn't break the page that triggered it" policy.
//
// History: this talked SMTP to Brevo from MonsterASP, which silently blocked outbound SMTP, then
// moved to Brevo's HTTP API. On the VPS the provider blocks port 25 outbound, so mail goes to the
// relay container next to the app instead; the relay reaches Resend on 587, which is not blocked.
public class Mailer(IOptions<SmtpOptions> options, ILogger<Mailer> logger)
{
    // Wall-clock ceiling for one send attempt.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    private readonly SmtpOptions options = options.Value;

    /// <param name="textBody">
    /// Plain-text alternative. Optional, but worth passing: a multipart message reads correctly in
    /// clients with HTML disabled and scores better with spam filters than HTML alone.
    /// </param>
    // virtual so tests can substitute a recording double where a real SMTP exchange is beside the
    // point.
    public virtual async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null)
    {
        // Not configured (local dev, tests): fail fast instead of spending the full timeout on a
        // connection we know will fail. Callers already treat false as "recorded but not
        // delivered" and tell the user to reach the person another way.
        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.SenderEmail))
        {
            logger.LogWarning("Skipped sending email to {ToEmail}: SMTP is not configured.", toEmail);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(options.SenderName, options.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

            using var cts = new CancellationTokenSource(SendTimeout);
            using var client = new SmtpClient();
            // Upgrades to TLS when the server offers it; the relay on the private Docker network
            // does not, and a provider on 587 does.
            await client.ConnectAsync(options.Host, options.Port, SecureSocketOptions.StartTlsWhenAvailable, cts.Token);
            if (!string.IsNullOrEmpty(options.UserName))
            {
                await client.AuthenticateAsync(options.UserName, options.Password, cts.Token);
            }

            await client.SendAsync(message, cts.Token);
            await client.DisconnectAsync(quit: true, cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Timed out rather than failed outright. Same contract as any other send failure:
            // return false and let the caller carry on — e-mail is never the critical path here.
            logger.LogError("Timed out sending email to {ToEmail} via {Host} after {Timeout}s.", toEmail, options.Host, SendTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex)
        {
            // An SMTP rejection (unknown sender domain, relay refused) carries the server's reason
            // in the exception message — the difference between "fix the relay" and "fix the code".
            logger.LogError(ex, "Failed to send email to {ToEmail} via {Host}.", toEmail, options.Host);
            return false;
        }
    }
}
