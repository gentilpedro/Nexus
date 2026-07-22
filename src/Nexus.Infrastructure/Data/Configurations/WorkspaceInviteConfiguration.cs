using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkspaceInviteConfiguration : IEntityTypeConfiguration<WorkspaceInvite>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvite> builder)
    {
        builder.Property(i => i.Email).HasMaxLength(256);
        builder.Property(i => i.Token).HasMaxLength(64);

        builder.HasIndex(i => i.Token).IsUnique();
        builder.HasIndex(i => new { i.WorkspaceId, i.Email });

        builder.HasOne(i => i.Workspace)
            .WithMany()
            .HasForeignKey(i => i.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull, not Restrict: no other FK path reaches WorkspaceInvites from AspNetUsers,
        // so this doesn't hit the "multiple cascade paths" issue documented on WorkItem etc.
        builder.HasOne(i => i.InvitedByUser)
            .WithMany()
            .HasForeignKey(i => i.InvitedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
