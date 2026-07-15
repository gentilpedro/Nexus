using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkItemAttachmentConfiguration : IEntityTypeConfiguration<WorkItemAttachment>
{
    public void Configure(EntityTypeBuilder<WorkItemAttachment> builder)
    {
        builder.Property(a => a.FileName).HasMaxLength(500).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(a => a.StoragePath).HasMaxLength(500).IsRequired();

        // Deleting the task deletes its attachment rows too — single cascade path, no conflict.
        builder.HasOne(a => a.WorkItem)
            .WithMany(w => w.Attachments)
            .HasForeignKey(a => a.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: avoids a second cascading path from AspNetUsers alongside
        // WorkItem->WorkItemAttachment above — same reasoning as WorkItem.CreatedByUserId.
        builder.HasOne(a => a.UploadedByUser)
            .WithMany()
            .HasForeignKey(a => a.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
