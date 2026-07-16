using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.Property(l => l.Name).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Color).HasMaxLength(9).IsRequired();

        // Deleting the list deletes its labels — single cascade path, no conflict.
        builder.HasOne(l => l.TaskList)
            .WithMany()
            .HasForeignKey(l => l.TaskListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
