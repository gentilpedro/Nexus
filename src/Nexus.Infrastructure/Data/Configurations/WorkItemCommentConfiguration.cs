using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkItemCommentConfiguration : IEntityTypeConfiguration<WorkItemComment>
{
    public void Configure(EntityTypeBuilder<WorkItemComment> builder)
    {
        builder.Property(c => c.Content).HasMaxLength(4000).IsRequired();

        // Deleting the task deletes its comments too — single cascade path, no conflict.
        builder.HasOne(c => c.WorkItem)
            .WithMany(w => w.Comments)
            .HasForeignKey(c => c.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull, not Cascade: deleting a user shouldn't erase comment history, same
        // reasoning as ChatMessage.UserId.
        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => new { c.WorkItemId, c.CreatedAtUtc });
    }
}
