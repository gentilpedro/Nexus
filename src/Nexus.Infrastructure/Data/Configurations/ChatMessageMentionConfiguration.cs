using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class ChatMessageMentionConfiguration : IEntityTypeConfiguration<ChatMessageMention>
{
    public void Configure(EntityTypeBuilder<ChatMessageMention> builder)
    {
        // Deleting the message deletes its mention rows too — single cascade path, no conflict.
        builder.HasOne(m => m.ChatMessage)
            .WithMany(cm => cm.Mentions)
            .HasForeignKey(m => m.ChatMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade, not SetNull like ChatMessage.UserId — a mention row has no meaning without
        // the mentioned user, so deleting the user should just remove the mention, not force
        // preserving a dangling reference the way we do for the message's own author.
        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
