using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.Property(w => w.Title).HasMaxLength(500).IsRequired();

        // Was unbounded `text`. Every other user-authored column in this schema carries a limit;
        // this one did not, so a single user could store arbitrarily large descriptions — and the
        // list views load Description along with the rest of the row.
        builder.Property(w => w.Description).HasMaxLength(WorkItem.MaxDescriptionLength);

        // Optimistic concurrency via Postgres's built-in xmin system column — no new physical
        // column needed (every Postgres row already has one). Without this, two people editing
        // the same WorkItem concurrently would have the second SaveChangesAsync silently
        // overwrite the first's changes with no conflict detected.
        builder.Property<uint>("Version").IsRowVersion();

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

        // Supports DueDateNotificationHostedService, which runs hourly and filters WorkItems on
        // DueDateUtc. There was no index on this column, so every pass scanned the entire
        // WorkItems table — the cost of that grows with the whole product's data, forever.
        //
        // Partial (WHERE "DueDateUtc" IS NOT NULL): the service only ever looks at rows that have
        // a due date, and most work items never get one. A plain btree also indexes every NULL,
        // which made the index nearly as large as the table while being useless for this
        // predicate — EXPLAIN showed the planner ignoring it in favour of a sequential scan.
        builder.HasIndex(w => w.DueDateUtc)
            .HasFilter("\"DueDateUtc\" IS NOT NULL");
    }
}
