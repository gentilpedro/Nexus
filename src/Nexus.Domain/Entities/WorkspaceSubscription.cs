namespace Nexus.Domain.Entities;

// One per workspace: the plan is bought for a workspace, not for a user account, so two
// workspaces of the same owner can be on different plans.
public class WorkspaceSubscription
{
    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public WorkspacePlan Plan { get; set; }
    public SubscriptionStatus Status { get; set; }

    // Billed seats (members). Starter is free, but the count is kept anyway so that upgrading
    // starts from the real team size.
    public int Seats { get; set; }

    // Null while the workspace has never paid for a period (Starter).
    public DateTime? CurrentPeriodStartUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Billing (#50): set once the workspace goes through Stripe Checkout. Null on Starter.
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    // When the newest Stripe event applied here was created. Stripe does not guarantee delivery
    // order, so an older "updated" arriving late must not undo a newer one.
    public DateTime? StripeEventAtUtc { get; set; }

    public static WorkspaceSubscription StartStarter(Guid workspaceId, DateTime nowUtc) => new()
    {
        WorkspaceId = workspaceId,
        Plan = WorkspacePlan.Starter,
        Status = SubscriptionStatus.Active,
        Seats = 1,
        CreatedAtUtc = nowUtc,
        UpdatedAtUtc = nowUtc
    };

    // The plan whose limits and features apply right now. A canceled paid plan keeps working
    // until the end of the period already paid for, then the workspace falls back to Starter —
    // the same rule the Terms of Use promise.
    public WorkspacePlan EffectivePlan(DateTime nowUtc) => Status switch
    {
        SubscriptionStatus.Canceled when CurrentPeriodEndUtc is not { } end || nowUtc >= end => WorkspacePlan.Starter,
        _ => Plan
    };
}
