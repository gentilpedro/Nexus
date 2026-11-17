using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkspaceCheckinConfiguration : IEntityTypeConfiguration<WorkspaceCheckin>
{
    public void Configure(EntityTypeBuilder<WorkspaceCheckin> builder)
    {
        builder.Property(c => c.Finished).HasMaxLength(WorkspaceCheckin.MaxTextLength);
        builder.Property(c => c.InProgress).HasMaxLength(WorkspaceCheckin.MaxTextLength);
        builder.Property(c => c.NextSteps).HasMaxLength(WorkspaceCheckin.MaxTextLength);
        builder.Property(c => c.Observations).HasMaxLength(WorkspaceCheckin.MaxTextLength);

        // Um check-in por semana por workspace; a lista é lida do mais recente para o mais antigo.
        builder.HasIndex(c => new { c.WorkspaceId, c.WeekStartUtc }).IsUnique();

        builder.HasOne(c => c.Workspace)
            .WithMany()
            .HasForeignKey(c => c.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
