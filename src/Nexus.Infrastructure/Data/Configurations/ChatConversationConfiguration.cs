using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(ChatConversation.MaxNameLength);

        // Two user ids joined by ':'; Identity ids are GUID strings (36 chars each).
        builder.Property(c => c.DirectKey).HasMaxLength(80);

        builder.HasOne(c => c.Workspace)
            .WithMany()
            .HasForeignKey(c => c.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull, same reasoning as ChatMessage.UserId: the group outlives whoever created it.
        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.WorkspaceId);

        // Exactly one Workspace-type conversation per workspace (Type 0 = Workspace).
        builder.HasIndex(c => c.WorkspaceId)
            .IsUnique()
            .HasDatabaseName("IX_ChatConversations_WorkspaceId_WorkspaceType")
            .HasFilter("\"Type\" = 0");

        // Exactly one direct conversation per pair of people in a workspace. Postgres treats
        // NULLs as distinct, so Group/Workspace rows (DirectKey null) never collide here.
        builder.HasIndex(c => new { c.WorkspaceId, c.DirectKey })
            .IsUnique();
    }
}
