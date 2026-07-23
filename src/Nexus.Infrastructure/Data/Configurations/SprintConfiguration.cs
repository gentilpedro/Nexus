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

        // Enforces "at most one Active sprint per list" at the database level — the
        // application-level check in WorkItemQueryService.StartSprintAsync is only a
        // pre-check; without this, two concurrent StartSprintAsync calls for the same list
        // could both pass that check before either commits.
        builder.HasIndex(s => s.TaskListId)
            .IsUnique()
            .HasFilter("\"Status\" = 1")
            .HasDatabaseName("IX_Sprints_TaskListId_ActiveOnly");

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
