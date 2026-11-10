using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class ChatConversationMemberConfiguration : IEntityTypeConfiguration<ChatConversationMember>
{
    public void Configure(EntityTypeBuilder<ChatConversationMember> builder)
    {
        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ConversationId, m.UserId }).IsUnique();

        // "Which conversations am I in?" is the sidebar's main query.
        builder.HasIndex(m => m.UserId);
    }
}
