namespace Nexus.Domain.Entities;

// Persisted as an integer: the declaration order is part of the stored data.
public enum SubscriptionStatus
{
    Active,

    // A renewal charge failed and the payment provider is still retrying. The paid plan stays in
    // effect meanwhile; suspending it is a billing decision, not something this model infers.
    PastDue,

    // The owner canceled. The paid plan lasts until the end of the period already paid for.
    Canceled
}
