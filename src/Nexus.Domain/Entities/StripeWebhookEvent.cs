namespace Nexus.Domain.Entities;

// A Stripe webhook event already applied (#50). Stripe delivers each event at least once, and
// retries on any non-2xx answer, so the same event can arrive more than once: its id is the key
// that makes processing idempotent.
public class StripeWebhookEvent
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime ProcessedAtUtc { get; set; }
}
