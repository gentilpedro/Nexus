using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services.Billing;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the Stripe webhook (#50) with events signed exactly as Stripe signs them
/// (<c>t=…,v1=HMAC-SHA256(secret, "t.payload")</c>): only a valid signature changes anything,
/// each event applies once, a late older event cannot undo a newer one, and the subscription
/// status maps onto the plan the workspace gets.
/// </summary>
public class StripeWebhookHandlerTests
{
    private const string Secret = "whsec_test_secret";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_761_480_000);

    private static StripeWebhookHandler Handler(IDbContextFactory<AppDbContext> factory, bool configured = true) =>
        new(factory, Options.Create(configured
                ? new StripeOptions { SecretKey = "sk_test_x", WebhookSecret = Secret, ProPriceId = "price_pro" }
                : new StripeOptions()),
            NullLogger<StripeWebhookHandler>.Instance, new FakeTimeProvider(Now));

    // Stripe's signature scheme. The timestamp is "now" because EventUtility refuses signatures
    // older than the tolerance window against the real clock.
    private static string Sign(string payload, string secret = Secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }

    private static string Event(string id, string type, long created, string dataObject) =>
        "{\"id\":\"" + id + "\",\"object\":\"event\",\"api_version\":\"2025-01-27.acacia\",\"created\":" + created +
        ",\"type\":\"" + type + "\",\"livemode\":false,\"pending_webhooks\":1,\"data\":{\"object\":" + dataObject + "}}";

    private static string Checkout(Guid workspaceId) => $$"""
        {"id":"cs_1","object":"checkout.session","mode":"subscription","client_reference_id":"{{workspaceId}}",
         "customer":"cus_1","subscription":"sub_1"}
        """;

    private static string Subscription(Guid? metadataWorkspace, string status, bool cancelAtPeriodEnd = false, long? endedAt = null, int quantity = 7)
    {
        var metadata = metadataWorkspace is { } w ? "{\"workspace_id\":\"" + w + "\"}" : "{}";
        return "{\"id\":\"sub_1\",\"object\":\"subscription\",\"customer\":\"cus_1\",\"status\":\"" + status + "\"," +
            "\"cancel_at_period_end\":" + (cancelAtPeriodEnd ? "true" : "false") + ",\"ended_at\":" + (endedAt?.ToString() ?? "null") + "," +
            "\"metadata\":" + metadata + "," +
            "\"items\":{\"object\":\"list\",\"data\":[{\"id\":\"si_1\",\"object\":\"subscription_item\",\"quantity\":" + quantity + "," +
            "\"current_period_start\":1761480000,\"current_period_end\":1764158400}]}}";
    }

    private static async Task<Guid> SeedWorkspaceAsync(IDbContextFactory<AppDbContext> factory)
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = id, Name = "W", Slug = $"w-{id:N}", Subscription = WorkspaceSubscription.StartStarter(id, Now.UtcDateTime) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private static async Task<WorkspaceSubscription> LoadAsync(IDbContextFactory<AppDbContext> factory, Guid workspaceId)
    {
        await using var db = factory.CreateDbContext();
        return await db.WorkspaceSubscriptions.SingleAsync(s => s.WorkspaceId == workspaceId, TestContext.Current.CancellationToken);
    }

    private static async Task<StripeWebhookOutcome> Send(StripeWebhookHandler handler, string payload) =>
        await handler.HandleAsync(payload, Sign(payload), TestContext.Current.CancellationToken);

    [Fact]
    public async Task CompletedCheckout_MakesTheWorkspacePro_AndLinksTheStripeIds()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);

        var outcome = await Send(Handler(factory), Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(workspaceId)));

        Assert.Equal(StripeWebhookOutcome.Processed, outcome);
        var sub = await LoadAsync(factory, workspaceId);
        Assert.Equal((WorkspacePlan.Pro, SubscriptionStatus.Active), (sub.Plan, sub.Status));
        Assert.Equal(("cus_1", "sub_1"), (sub.StripeCustomerId, sub.StripeSubscriptionId));
    }

    [Fact]
    public async Task BadSignature_ChangesNothing()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var payload = Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(workspaceId));

        var outcome = await Handler(factory).HandleAsync(payload, Sign(payload, "whsec_someone_else"), TestContext.Current.CancellationToken);

        Assert.Equal(StripeWebhookOutcome.InvalidSignature, outcome);
        Assert.Equal(WorkspacePlan.Starter, (await LoadAsync(factory, workspaceId)).Plan);
    }

    [Fact]
    public async Task TamperedPayload_IsRefused()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var payload = Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(Guid.NewGuid()));
        var signature = Sign(payload);

        var outcome = await Handler(factory).HandleAsync(payload.Replace("cus_1", "cus_2"), signature, TestContext.Current.CancellationToken);

        Assert.Equal(StripeWebhookOutcome.InvalidSignature, outcome);
        Assert.Equal(WorkspacePlan.Starter, (await LoadAsync(factory, workspaceId)).Plan);
    }

    [Fact]
    public async Task BillingOff_AnswersNotConfigured()
    {
        var factory = TestDb.CreateFactory();
        var payload = Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(Guid.NewGuid()));

        Assert.Equal(StripeWebhookOutcome.NotConfigured, await Handler(factory, configured: false).HandleAsync(payload, Sign(payload), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SameEventTwice_IsAppliedOnce()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var handler = Handler(factory);
        var payload = Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(workspaceId));

        Assert.Equal(StripeWebhookOutcome.Processed, await Send(handler, payload));
        Assert.Equal(StripeWebhookOutcome.Duplicate, await Send(handler, payload));

        await using var db = factory.CreateDbContext();
        Assert.Single(await db.StripeWebhookEvents.ToListAsync(TestContext.Current.CancellationToken));
    }

    // The subscription can be announced before the checkout session: the metadata finds the workspace.
    [Fact]
    public async Task SubscriptionEventBeforeCheckout_FindsTheWorkspaceByMetadata()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);

        await Send(Handler(factory), Event("evt_1", "customer.subscription.created", 1_761_480_000, Subscription(workspaceId, "active")));

        var sub = await LoadAsync(factory, workspaceId);
        Assert.Equal((WorkspacePlan.Pro, SubscriptionStatus.Active, 7), (sub.Plan, sub.Status, sub.Seats));
        Assert.Equal("sub_1", sub.StripeSubscriptionId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_764_158_400).UtcDateTime, sub.CurrentPeriodEndUtc);
    }

    [Theory]
    [InlineData("past_due", false, SubscriptionStatus.PastDue)]
    [InlineData("unpaid", false, SubscriptionStatus.PastDue)]
    [InlineData("active", true, SubscriptionStatus.Canceled)] // canceled by the owner: Pro until the period ends
    [InlineData("active", false, SubscriptionStatus.Active)]
    public async Task SubscriptionStatus_MapsOntoTheWorkspace(string stripeStatus, bool cancelAtPeriodEnd, SubscriptionStatus expected)
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var handler = Handler(factory);
        await Send(handler, Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(workspaceId)));

        await Send(handler, Event("evt_2", "customer.subscription.updated", 1_761_480_100, Subscription(null, stripeStatus, cancelAtPeriodEnd)));

        var sub = await LoadAsync(factory, workspaceId);
        Assert.Equal(expected, sub.Status);
        // Every one of these still has Pro right now (past due keeps it while Stripe retries).
        Assert.Equal(WorkspacePlan.Pro, sub.EffectivePlan(Now.UtcDateTime));
    }

    [Fact]
    public async Task DeletedSubscription_FallsBackToStarter()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var handler = Handler(factory);
        await Send(handler, Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(workspaceId)));

        await Send(handler, Event("evt_2", "customer.subscription.deleted", 1_761_480_100, Subscription(null, "canceled", endedAt: 1_761_480_100)));

        var sub = await LoadAsync(factory, workspaceId);
        Assert.Equal(SubscriptionStatus.Canceled, sub.Status);
        Assert.Equal(WorkspacePlan.Starter, sub.EffectivePlan(Now.UtcDateTime.AddMinutes(5)));
    }

    // Stripe does not promise delivery order: an "active" created before the "past_due" that
    // already arrived must not bring the workspace back to good standing.
    [Fact]
    public async Task OlderEventArrivingLate_DoesNotUndoANewerOne()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var handler = Handler(factory);
        await Send(handler, Event("evt_1", "checkout.session.completed", 1_761_480_000, Checkout(workspaceId)));
        await Send(handler, Event("evt_3", "customer.subscription.updated", 1_761_480_300, Subscription(null, "past_due")));

        var outcome = await Send(handler, Event("evt_2", "customer.subscription.updated", 1_761_480_200, Subscription(null, "active")));

        Assert.Equal(StripeWebhookOutcome.Ignored, outcome);
        Assert.Equal(SubscriptionStatus.PastDue, (await LoadAsync(factory, workspaceId)).Status);
    }

    [Fact]
    public async Task UnrelatedEvents_AreIgnored()
    {
        var factory = TestDb.CreateFactory();

        var outcome = await Send(Handler(factory), Event("evt_1", "customer.created", 1_761_480_000, """{"id":"cus_1","object":"customer"}"""));

        Assert.Equal(StripeWebhookOutcome.Ignored, outcome);
    }
}
