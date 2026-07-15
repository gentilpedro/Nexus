using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class DocPageConfiguration : IEntityTypeConfiguration<DocPage>
{
    public void Configure(EntityTypeBuilder<DocPage> builder)
    {
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();

        // Page belongs to the workspace's lifecycle — deleting the workspace deletes its pages.
        builder.HasOne(d => d.Workspace)
            .WithMany()
            .HasForeignKey(d => d.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade/SetNull: avoids a second cascading path from AspNetUsers
        // alongside Workspace->DocPage above — same reasoning as WorkItem.CreatedByUserId.
        builder.HasOne(d => d.CreatedByUser)
            .WithMany()
            .HasForeignKey(d => d.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.UpdatedByUser)
            .WithMany()
            .HasForeignKey(d => d.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
