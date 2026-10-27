using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkspaceSubscriptionConfiguration : IEntityTypeConfiguration<WorkspaceSubscription>
{
    public void Configure(EntityTypeBuilder<WorkspaceSubscription> builder)
    {
        // Keyed by the workspace itself: a workspace has exactly one subscription, and the
        // database enforces it instead of a unique index on a separate id.
        builder.HasKey(s => s.WorkspaceId);

        builder.HasOne(s => s.Workspace)
            .WithOne(w => w.Subscription)
            .HasForeignKey<WorkspaceSubscription>(s => s.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.StripeCustomerId).HasMaxLength(100);
        builder.Property(s => s.StripeSubscriptionId).HasMaxLength(100);

        // Webhooks find the workspace by the Stripe subscription; one subscription pays for one
        // workspace.
        builder.HasIndex(s => s.StripeSubscriptionId)
            .IsUnique()
            .HasFilter("\"StripeSubscriptionId\" IS NOT NULL");
    }
}
