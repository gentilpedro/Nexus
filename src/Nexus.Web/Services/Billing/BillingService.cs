using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.Billing;

/// <param name="Url">Where to send the person (Stripe Checkout), when it worked.</param>
/// <param name="Error">What to tell the person, when it did not.</param>
public sealed record CheckoutStart(string? Url, string? Error);

/// <summary>
/// What the "Plano e cobrança" page shows the Owner (#54). <see cref="Invoices"/> is null when
/// Stripe could not be reached: the rest of the page still works from the local subscription.
/// </summary>
public sealed record BillingOverview(
    WorkspacePlanInfo Plan,
    int Members,
    bool CanManageInStripe,
    IReadOnlyList<StripeInvoice>? Invoices);

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
    private const decimal ProBaseAmount = 149m;
    private const decimal ProExtraSeatAmount = 14.90m;
    private const int InvoicesShown = 12;

    /// <summary>What a month of Pro costs for this many people, before any proration.</summary>
    public static decimal ProMonthlyAmount(int seats) =>
        ProBaseAmount + Math.Max(0, seats - ProIncludedSeats) * ProExtraSeatAmount;

    public bool IsEnabled => options.Value.IsConfigured;

    public async Task<CheckoutStart> StartCheckoutAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new CheckoutStart(null, "A assinatura do plano Pro ainda não está disponível.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // Only the Owner commits the workspace to a recurring charge.
        if (!await IsOwnerAsync(db, workspaceId, userId, cancellationToken))
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

        var billingPage = BillingPath(workspaceId);
        var request = new StripeCheckoutRequest(
            workspaceId,
            seats,
            plan.Subscription?.StripeCustomerId,
            email,
            // PublicUrlBuilder, not the request's Host header: see PublicUrlBuilder.
            SuccessUrl: publicUrl.BuildUrl(billingPage, new Dictionary<string, object?> { ["assinatura"] = "ok" }),
            CancelUrl: publicUrl.BuildUrl(billingPage, new Dictionary<string, object?>()));

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

    /// <summary>Relative path of the "Plano e cobrança" page of a workspace.</summary>
    public static string BillingPath(Guid workspaceId) => $"workspaces/{workspaceId}/billing";

    /// <summary>
    /// Everything the "Plano e cobrança" page shows (#54), or null when the user is not the
    /// Owner — only who pays sees what is paid.
    /// </summary>
    public async Task<BillingOverview?> GetOverviewAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsOwnerAsync(db, workspaceId, userId, cancellationToken))
        {
            return null;
        }

        var plan = await plans.GetAsync(db, workspaceId, cancellationToken);
        var members = await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId, cancellationToken);
        var customerId = IsEnabled ? plan.Subscription?.StripeCustomerId : null;

        IReadOnlyList<StripeInvoice>? invoices = [];
        if (customerId is not null)
        {
            try
            {
                invoices = await gateway.ListInvoicesAsync(customerId, InvoicesShown, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao buscar as faturas do workspace {WorkspaceId}.", workspaceId);
                invoices = null;
            }
        }

        return new BillingOverview(plan, members, CanManageInStripe: customerId is not null, invoices);
    }

    /// <summary>
    /// Sends the Owner to Stripe's customer portal, where they change the card, see invoices and
    /// cancel. Canceling there keeps Pro until the end of the paid period: Stripe reports
    /// <c>cancel_at_period_end</c> and the webhook marks the subscription as canceled, and
    /// <see cref="WorkspaceSubscription.EffectivePlan"/> drops it to Starter once the period ends.
    /// </summary>
    public async Task<CheckoutStart> OpenPortalAsync(Guid workspaceId, string userId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new CheckoutStart(null, "A cobrança do plano Pro ainda não está disponível.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsOwnerAsync(db, workspaceId, userId, cancellationToken))
        {
            return new CheckoutStart(null, "Só o dono do workspace pode gerenciar a assinatura.");
        }

        var customerId = await db.WorkspaceSubscriptions
            .Where(s => s.WorkspaceId == workspaceId)
            .Select(s => s.StripeCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (customerId is null)
        {
            return new CheckoutStart(null, "Este workspace ainda não tem uma assinatura para gerenciar.");
        }

        try
        {
            var returnUrl = publicUrl.BuildUrl(BillingPath(workspaceId), new Dictionary<string, object?>());
            return new CheckoutStart(await gateway.CreatePortalUrlAsync(customerId, returnUrl, cancellationToken), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Falha ao abrir o portal do Stripe do workspace {WorkspaceId}.", workspaceId);
            return new CheckoutStart(null, "Não foi possível abrir a gestão da assinatura agora. Tente de novo em instantes.");
        }
    }

    private static Task<bool> IsOwnerAsync(AppDbContext db, Guid workspaceId, string userId, CancellationToken cancellationToken) =>
        db.WorkspaceMembers.AnyAsync(
            m => m.WorkspaceId == workspaceId && m.UserId == userId && m.Role == WorkspaceRole.Owner, cancellationToken);

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
