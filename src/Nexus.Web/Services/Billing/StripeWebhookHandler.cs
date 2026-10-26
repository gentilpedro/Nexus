using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Stripe;
using Stripe.Checkout;

namespace Nexus.Web.Services.Billing;

public enum StripeWebhookOutcome
{
    Processed,

    /// <summary>Already applied before: Stripe delivers each event at least once.</summary>
    Duplicate,

    /// <summary>Valid, but not something billing reacts to (or about no known workspace).</summary>
    Ignored,

    /// <summary>The signature does not match: not from Stripe, or tampered with. Nothing changes.</summary>
    InvalidSignature,

    /// <summary>Billing is off (<see cref="StripeOptions.IsConfigured"/>).</summary>
    NotConfigured,
}

/// <summary>
/// Applies Stripe's word on a subscription to the workspace (#50). This is the only path that
/// turns a workspace Pro, renews it, marks it past due or cancels it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every event is verified against the endpoint's signing secret before anything is read.</item>
/// <item>Idempotent: the event id is stored with the change, in the same save, so a redelivery is a no-op.</item>
/// <item>Order-safe: Stripe does not guarantee delivery order, so an event older than the last one
/// applied to the workspace does not overwrite it.</item>
/// <item>A subscription finds its workspace by id, or by the <c>workspace_id</c> metadata set at
/// checkout — whichever event arrives first.</item>
/// </list>
/// </remarks>
public class StripeWebhookHandler(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<StripeOptions> options,
    ILogger<StripeWebhookHandler> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // Stripe's own default: an event signed more than five minutes ago is refused (replay).
    private const long SignatureToleranceSeconds = 300;

    public async Task<StripeWebhookOutcome> HandleAsync(string payload, string? signatureHeader, CancellationToken cancellationToken = default)
    {
        if (!options.Value.IsConfigured)
        {
            return StripeWebhookOutcome.NotConfigured;
        }

        Event stripeEvent;
        try
        {
            // The API version of the account and of this library may differ; the fields read
            // below exist in both, so a mismatch is not a reason to drop the event.
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader ?? "", options.Value.WebhookSecret,
                SignatureToleranceSeconds, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Webhook do Stripe recusado: assinatura inválida.");
            return StripeWebhookOutcome.InvalidSignature;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.StripeWebhookEvents.AnyAsync(e => e.Id == stripeEvent.Id, cancellationToken))
        {
            return StripeWebhookOutcome.Duplicate;
        }

        var applied = stripeEvent.Data.Object switch
        {
            Session session when stripeEvent.Type == EventTypes.CheckoutSessionCompleted
                => await ApplyCheckoutAsync(db, session, stripeEvent.Created, cancellationToken),
            Subscription subscription when stripeEvent.Type is EventTypes.CustomerSubscriptionCreated
                    or EventTypes.CustomerSubscriptionUpdated or EventTypes.CustomerSubscriptionDeleted
                => await ApplySubscriptionAsync(db, subscription, stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted, stripeEvent.Created, cancellationToken),
            _ => false,
        };

        db.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            Id = stripeEvent.Id,
            Type = stripeEvent.Type,
            ProcessedAtUtc = clock.GetUtcNow().UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same event delivered twice at once: the other delivery won and applied it.
            if (await IsRecordedAsync(stripeEvent.Id, cancellationToken))
            {
                return StripeWebhookOutcome.Duplicate;
            }
            throw;
        }

        return applied ? StripeWebhookOutcome.Processed : StripeWebhookOutcome.Ignored;
    }

    private async Task<bool> ApplyCheckoutAsync(AppDbContext db, Session session, DateTime eventCreated, CancellationToken cancellationToken)
    {
        if (session.Mode != "subscription" || !Guid.TryParse(session.ClientReferenceId, out var workspaceId))
        {
            return false;
        }

        var subscription = await LoadOrStartAsync(db, workspaceId, cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        subscription.StripeCustomerId = session.CustomerId;
        subscription.StripeSubscriptionId = session.SubscriptionId;

        // A subscription event may already have told the full story (status, period, seats).
        if (subscription.StripeEventAtUtc is not { } last || last <= eventCreated)
        {
            subscription.Plan = WorkspacePlan.Pro;
            subscription.Status = SubscriptionStatus.Active;
            subscription.StripeEventAtUtc = eventCreated;
        }

        subscription.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        return true;
    }

    private async Task<bool> ApplySubscriptionAsync(AppDbContext db, Subscription stripe, bool deleted, DateTime eventCreated, CancellationToken cancellationToken)
    {
        var subscription = await db.WorkspaceSubscriptions.FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripe.Id, cancellationToken);
        if (subscription is null
            && stripe.Metadata?.GetValueOrDefault(StripeGateway.WorkspaceMetadataKey) is { } metadataId
            && Guid.TryParse(metadataId, out var workspaceId))
        {
            subscription = await LoadOrStartAsync(db, workspaceId, cancellationToken);
        }
        if (subscription is null)
        {
            logger.LogWarning("Evento do Stripe sobre a assinatura {SubscriptionId}, que não pertence a nenhum workspace.", stripe.Id);
            return false;
        }

        if (subscription.StripeEventAtUtc is { } last && last > eventCreated)
        {
            return false; // a newer event already applied wins
        }

        subscription.StripeSubscriptionId = stripe.Id;
        subscription.StripeCustomerId = stripe.CustomerId ?? subscription.StripeCustomerId;
        subscription.Plan = WorkspacePlan.Pro;
        subscription.StripeEventAtUtc = eventCreated;
        subscription.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;

        if (stripe.Items?.Data is [var item, ..])
        {
            subscription.Seats = (int)item.Quantity;
            subscription.CurrentPeriodStartUtc = item.CurrentPeriodStart;
            subscription.CurrentPeriodEndUtc = item.CurrentPeriodEnd;
        }

        if (deleted || stripe.Status is "canceled" or "incomplete_expired")
        {
            // Over: the paid period ends now, so the workspace is Starter from here on.
            subscription.Status = SubscriptionStatus.Canceled;
            subscription.CurrentPeriodEndUtc = stripe.EndedAt ?? eventCreated;
        }
        else if (stripe.Status is "past_due" or "unpaid")
        {
            subscription.Status = SubscriptionStatus.PastDue;
        }
        else if (stripe.CancelAtPeriodEnd)
        {
            // Canceled by the owner: Pro until the end of what was paid (WorkspaceSubscription.EffectivePlan).
            subscription.Status = SubscriptionStatus.Canceled;
        }
        else if (stripe.Status is "active" or "trialing")
        {
            subscription.Status = SubscriptionStatus.Active;
        }
        else
        {
            // "incomplete": the first payment has not gone through yet. Not Pro until it does.
            subscription.Plan = WorkspacePlan.Starter;
            subscription.Status = SubscriptionStatus.Active;
        }

        return true;
    }

    private static async Task<WorkspaceSubscription?> LoadOrStartAsync(AppDbContext db, Guid workspaceId, CancellationToken cancellationToken)
    {
        var subscription = await db.WorkspaceSubscriptions.FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, cancellationToken);
        if (subscription is not null)
        {
            return subscription;
        }
        if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken))
        {
            return null;
        }

        subscription = WorkspaceSubscription.StartStarter(workspaceId, DateTime.UtcNow);
        db.WorkspaceSubscriptions.Add(subscription);
        return subscription;
    }

    private async Task<bool> IsRecordedAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var check = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await check.StripeWebhookEvents.AnyAsync(e => e.Id == eventId, cancellationToken);
    }
}
