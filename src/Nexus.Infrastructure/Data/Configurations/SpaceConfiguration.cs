using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class SpaceConfiguration : IEntityTypeConfiguration<Space>
{
    public void Configure(EntityTypeBuilder<Space> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Color).HasMaxLength(20);
        builder.Property(s => s.Icon).HasMaxLength(20);

        builder.HasMany(s => s.Lists)
            .WithOne(l => l.Space)
            .HasForeignKey(l => l.SpaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
