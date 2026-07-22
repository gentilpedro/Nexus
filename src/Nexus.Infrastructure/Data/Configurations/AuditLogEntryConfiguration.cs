using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.Property(a => a.TargetId).HasMaxLength(200);
        builder.Property(a => a.Detail).HasMaxLength(1000);

        // SetNull, not Restrict: the audit trail must remain readable even if the actor's
        // account is later anonymized/removed. No other cascading path reaches AuditLogEntries
        // from AspNetUsers, so this doesn't hit the "multiple cascade paths" issue seen on
        // WorkItem.CreatedByUserId etc.
        builder.HasOne(a => a.ActorUser)
            .WithMany()
            .HasForeignKey(a => a.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => a.CreatedAtUtc);
        builder.HasIndex(a => new { a.Action, a.CreatedAtUtc });
    }
}
