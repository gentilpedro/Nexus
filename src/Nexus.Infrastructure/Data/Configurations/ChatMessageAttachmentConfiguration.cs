using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class ChatMessageAttachmentConfiguration : IEntityTypeConfiguration<ChatMessageAttachment>
{
    public void Configure(EntityTypeBuilder<ChatMessageAttachment> builder)
    {
        builder.Property(a => a.FileName).HasMaxLength(500).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(a => a.StoragePath).HasMaxLength(500).IsRequired();

        // Deleting the message deletes its attachment rows too — single cascade path, no conflict.
        builder.HasOne(a => a.ChatMessage)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.ChatMessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
