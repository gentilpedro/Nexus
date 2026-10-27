using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Nexus.Web.Services.Billing;

/// <summary>
/// Bound from the "Stripe" configuration section. Billing stays off — no subscribe button, the
/// webhook answers 404 — until all three values are set. Secrets come from the environment
/// (Stripe__SecretKey, Stripe__WebhookSecret), never from a file in the repository.
/// </summary>
public sealed class StripeOptions
{
    /// <summary>Secret API key (<c>sk_test_…</c> or <c>sk_live_…</c>).</summary>
    public string? SecretKey { get; set; }

    /// <summary>Signing secret of the webhook endpoint (<c>whsec_…</c>).</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Id of the Pro price (<c>price_…</c>): recurring monthly in BRL, graduated tiers — 1 to 10
    /// seats as one flat amount, every seat after that charged per unit. The app only reports how
    /// many members the workspace has; Stripe does the arithmetic.
    /// </summary>
    public string? ProPriceId { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(WebhookSecret) && !string.IsNullOrWhiteSpace(ProPriceId);
}

/// <param name="CustomerId">Stripe customer of a workspace that paid before; otherwise Stripe creates one from <paramref name="CustomerEmail"/>.</param>
public sealed record StripeCheckoutRequest(
    Guid WorkspaceId,
    int Seats,
    string? CustomerId,
    string? CustomerEmail,
    string SuccessUrl,
    string CancelUrl);

/// <summary>The calls Nexus makes to Stripe. Faked in tests: nothing here is reachable offline.</summary>
public interface IStripeGateway
{
    /// <summary>Creates a hosted Checkout session and returns where to send the person.</summary>
    Task<string> CreateCheckoutUrlAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>Changes the billed seats of a subscription, prorating the current period.</summary>
    Task UpdateSeatsAsync(string subscriptionId, int seats, CancellationToken cancellationToken = default);
}

public sealed class StripeGateway(IOptions<StripeOptions> options) : IStripeGateway
{
    /// <summary>Metadata key that ties a Stripe subscription back to its workspace.</summary>
    public const string WorkspaceMetadataKey = "workspace_id";

    private StripeClient Client() => new(options.Value.SecretKey);

    public async Task<string> CreateCheckoutUrlAsync(StripeCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var workspaceId = request.WorkspaceId.ToString();
        var session = await Client().V1.Checkout.Sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            Locale = "pt-BR",
            ClientReferenceId = workspaceId,
            Customer = request.CustomerId,
            CustomerEmail = request.CustomerId is null ? request.CustomerEmail : null,
            LineItems = [new SessionLineItemOptions { Price = options.Value.ProPriceId, Quantity = request.Seats }],
            // Also on the subscription, so its own events can find the workspace even if they
            // arrive before checkout.session.completed.
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = new() { [WorkspaceMetadataKey] = workspaceId } },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
        }, cancellationToken: cancellationToken);

        return session.Url;
    }

    public async Task UpdateSeatsAsync(string subscriptionId, int seats, CancellationToken cancellationToken = default)
    {
        var client = Client();
        var subscription = await client.V1.Subscriptions.GetAsync(subscriptionId, cancellationToken: cancellationToken);
        var item = subscription.Items.Data[0];
        await client.V1.SubscriptionItems.UpdateAsync(item.Id, new SubscriptionItemUpdateOptions
        {
            Quantity = seats,
            ProrationBehavior = "create_prorations",
        }, cancellationToken: cancellationToken);
    }
}
