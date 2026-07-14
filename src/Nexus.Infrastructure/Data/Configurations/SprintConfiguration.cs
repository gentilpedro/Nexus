using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Goal).HasMaxLength(1000);

        builder.HasMany(s => s.WorkItems)
            .WithOne(w => w.Sprint)
            .HasForeignKey(w => w.SprintId)
            // Sprints are never deleted in this app (Planned -> Active -> Completed only);
            // Restrict avoids the multiple-cascade-paths conflict with TaskList -> WorkItem's
            // existing direct cascade (same lesson as WorkItem.ParentEpicId).
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Snapshots)
            .WithOne(sn => sn.Sprint)
            .HasForeignKey(sn => sn.SprintId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
