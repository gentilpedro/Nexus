using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Nexus.Web.Services;

public class BrevoOptions
{
    // Brevo's transactional-email endpoint. Configurable so a staging environment can point
    // somewhere else without a code change.
    public string ApiUrl { get; set; } = "https://api.brevo.com/v3/smtp/email";

    // Brevo API key (starts with "xkeysib-"). NOT the SMTP key — they are different credentials
    // issued from different pages of the Brevo dashboard.
    public string ApiKey { get; set; } = "";

    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "Nexus";
}

// Transactional e-mail through Brevo's HTTP API, shared by BrevoEmailSender (Identity's
// confirmation/reset emails), WorkspaceInviteService and DueDateNotificationHostedService — one
// place owning the "an email hiccup shouldn't break the page that triggered it" policy.
//
// This used to talk SMTP to smtp-relay.brevo.com:587 via MailKit. It sends over HTTPS instead
// because the production host (MonsterASP, free plan) does not allow outbound SMTP from hosted
// applications — "Outgoing SMTP for sending emails from hosted applications is only available for
// Premium plans". Every send failed there while working from any other machine, so nothing ever
// reached a user: invites, and also the account-confirmation e-mail that registration depends on.
// Port 443 carries no such restriction.
public class BrevoMailer(IOptions<BrevoOptions> options, IHttpClientFactory httpClientFactory, ILogger<BrevoMailer> logger)
{
    public const string HttpClientName = "brevo";

    // Wall-clock ceiling for one send attempt.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    private readonly BrevoOptions options = options.Value;

    // The request body Brevo expects. Property names are lowerCamelCase on the wire.
    private sealed record Sender([property: JsonPropertyName("name")] string Name,
                                 [property: JsonPropertyName("email")] string Email);

    private sealed record Recipient([property: JsonPropertyName("email")] string Email);

    private sealed record SendRequest(
        [property: JsonPropertyName("sender")] Sender Sender,
        [property: JsonPropertyName("to")] Recipient[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent);

    // virtual so tests can substitute a recording double: the real implementation makes a network
    // call, which no unit test should be doing.
    public virtual async Task<bool> SendAsync(string toEmail, string subject, string htmlBody)
    {
        // Not configured (local dev, tests): fail fast instead of spending the full timeout on a
        // call we know will be rejected. Callers already treat false as "recorded but not
        // delivered" and tell the user to reach the person another way.
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.SenderEmail))
        {
            logger.LogWarning("Skipped sending email to {ToEmail}: Brevo is not configured.", toEmail);
            return false;
        }

        var payload = new SendRequest(
            new Sender(options.SenderName, options.SenderEmail),
            [new Recipient(toEmail)],
            subject,
            htmlBody);

        try
        {
            using var cts = new CancellationTokenSource(SendTimeout);
            var client = httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Post, options.ApiUrl)
            {
                Content = JsonContent.Create(payload),
            };
            // Brevo authenticates on its own header, not Authorization.
            request.Headers.Add("api-key", options.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // Brevo answers a rejection with a JSON body naming the reason (unverified sender,
            // quota exhausted, bad key). Worth logging verbatim — it is the difference between
            // "fix the account" and "fix the code" — but truncated, and it never echoes the key.
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            logger.LogError(
                "Brevo rejected the email to {ToEmail}: HTTP {StatusCode}. {Body}",
                toEmail,
                (int)response.StatusCode,
                body.Length > 500 ? body[..500] : body);
            return false;
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
