using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Message).HasMaxLength(500).IsRequired();

        // Two FKs from different root tables (AspNetUsers, WorkItems) into Notification —
        // not the "two FKs into the same target from the same root" shape that trips SQL
        // Server's multiple-cascade-paths check, so both can safely cascade.
        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.WorkItem)
            .WithMany()
            .HasForeignKey(n => n.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: Workspace already reaches Notification via a cascading chain
        // (Workspace -> Space -> TaskList -> WorkItem -> Notification), so a second cascade path
        // straight from Workspace would hit SQL Server's multiple-cascade-paths error.
        builder.HasOne(n => n.Workspace)
            .WithMany()
            .HasForeignKey(n => n.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => new { n.UserId, n.IsRead });
    }
}
