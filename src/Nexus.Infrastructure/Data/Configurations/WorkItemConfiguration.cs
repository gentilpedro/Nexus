using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.Property(w => w.Title).HasMaxLength(500).IsRequired();

        // Restrict, not cascade: deleting a status must not silently delete the work items in it.
        builder.HasOne(w => w.Status)
            .WithMany()
            .HasForeignKey(w => w.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull: deleting a user shouldn't cascade-delete their assigned work items.
        builder.HasOne(w => w.Assignee)
            .WithMany()
            .HasForeignKey(w => w.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);

        // Restrict, not SetNull: WorkItems already has one action-carrying path from AspNetUsers
        // via Assignee (SetNull). SQL Server rejects a second cascading path between the same
        // pair of tables ("multiple cascade paths"), so CreatedByUserId must be NO ACTION —
        // same reasoning as ParentEpicId below.
        builder.HasOne(w => w.CreatedByUser)
            .WithMany()
            .HasForeignKey(w => w.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing FK: must be Restrict (NO ACTION). SQL Server rejects Cascade or SetNull
        // here because WorkItems already has an incoming cascade path from TaskList, and it disallows
        // multiple cascade paths (any action other than NO ACTION) reaching the same table.
        builder.HasOne(w => w.ParentEpic)
            .WithMany()
            .HasForeignKey(w => w.ParentEpicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
