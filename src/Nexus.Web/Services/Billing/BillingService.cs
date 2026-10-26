using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.Billing;

/// <param name="Url">Where to send the person (Stripe Checkout), when it worked.</param>
/// <param name="Error">What to tell the person, when it did not.</param>
public sealed record CheckoutStart(string? Url, string? Error);

/// <summary>
/// Paying for the Pro plan (#50): starts Stripe Checkout for a workspace and keeps the billed
/// seats in step with its members. The plan itself only changes when Stripe confirms, through
/// <see cref="StripeWebhookHandler"/> — never because someone came back from the checkout page.
/// </summary>
public class BillingService(
    IDbContextFactory<AppDbContext> dbFactory,
    IStripeGateway gateway,
    IOptions<StripeOptions> options,
    WorkspacePlanService plans,
    PublicUrlBuilder publicUrl,
    ILogger<BillingService> logger)
{
    // What the Pro plan costs, as the pages show it. The amounts charged live in the Stripe price
    // (StripeOptions.ProPriceId); keep both in step when either changes.
    public const string ProBasePrice = "R$ 149";
    public const int ProIncludedSeats = 10;
    public const string ProExtraSeatPrice = "R$ 14,90";

    public bool IsEnabled => options.Value.IsConfigured;

    public async Task<CheckoutStart> StartCheckoutAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new CheckoutStart(null, "A assinatura do plano Pro ainda não está disponível.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Only the Owner commits the workspace to a recurring charge.
        var isOwner = await db.WorkspaceMembers.AnyAsync(
            m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Owner, cancellationToken);
        if (!isOwner)
        {
            return new CheckoutStart(null, "Só o dono do workspace pode assinar o plano Pro.");
        }

        var plan = await plans.GetAsync(db, workspaceId, cancellationToken);
        if (plan.EffectivePlan != WorkspacePlan.Starter || plan.OwnedByUnlimitedAccount)
        {
            return new CheckoutStart(null, "Este workspace já tem os recursos do plano Pro.");
        }

        var seats = Math.Max(1, await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId, cancellationToken));
        var email = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);

        var settings = $"workspaces/{workspaceId}/settings";
        var request = new StripeCheckoutRequest(
            workspaceId,
            seats,
            plan.Subscription?.StripeCustomerId,
            email,
            // PublicUrlBuilder, not the request's Host header: see PublicUrlBuilder.
            SuccessUrl: publicUrl.BuildUrl(settings, new Dictionary<string, object?> { ["assinatura"] = "ok" }),
            CancelUrl: publicUrl.BuildUrl(settings, new Dictionary<string, object?>()));

        try
        {
            return new CheckoutStart(await gateway.CreateCheckoutUrlAsync(request, cancellationToken), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao criar a sessão de checkout do workspace {WorkspaceId}.", workspaceId);
            return new CheckoutStart(null, "Não foi possível abrir o pagamento agora. Tente de novo em instantes.");
        }
    }

    /// <summary>
    /// Brings the billed seats of a paying workspace to its current member count. Called after
    /// someone joins or leaves; best effort — the membership change already happened and must not
    /// fail because Stripe is slow, and the next change (or the webhook) corrects the count.
    /// </summary>
    public async Task SyncSeatsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var subscription = await db.WorkspaceSubscriptions.FirstOrDefaultAsync(s => s.WorkspaceId == workspaceId, cancellationToken);
            if (subscription?.StripeSubscriptionId is not { } stripeSubscriptionId || subscription.Status == SubscriptionStatus.Canceled)
            {
                return;
            }

            var members = Math.Max(1, await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId, cancellationToken));
            if (members == subscription.Seats)
            {
                return;
            }

            await gateway.UpdateSeatsAsync(stripeSubscriptionId, members, cancellationToken);
            subscription.Seats = members;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao atualizar os assentos cobrados do workspace {WorkspaceId}.", workspaceId);
        }
    }
}
