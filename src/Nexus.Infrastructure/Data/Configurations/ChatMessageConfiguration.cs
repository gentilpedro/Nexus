using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.Property(m => m.Content).HasMaxLength(2000).IsRequired();

        builder.HasOne(m => m.Workspace)
            .WithMany()
            .HasForeignKey(m => m.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull, not Cascade: deleting a user shouldn't erase chat history, same
        // reasoning as WorkItem.AssigneeId.
        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => new { m.WorkspaceId, m.CreatedAtUtc });
    }
}
