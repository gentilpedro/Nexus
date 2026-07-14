using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class StatusTransitionConfiguration : IEntityTypeConfiguration<StatusTransition>
{
    public void Configure(EntityTypeBuilder<StatusTransition> builder)
    {
        // Both FKs must be Restrict: TaskList already cascades directly into
        // TaskStatusDefinition, and both From/To point at that same table — unlike
        // CustomFieldValue's "one Cascade, one Restrict" pattern, neither side here can
        // cascade without hitting "multiple cascade paths" (same root, same target table,
        // via two different columns of this one new table). Callers that bulk-delete a
        // TaskList's statuses (e.g. SpaceDetail.HandleDelete) must explicitly clean up
        // StatusTransitions first.
        builder.HasOne(t => t.FromStatus)
            .WithMany()
            .HasForeignKey(t => t.FromStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ToStatus)
            .WithMany()
            .HasForeignKey(t => t.ToStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.FromStatusId, t.ToStatusId }).IsUnique();
    }
}
