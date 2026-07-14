using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();

        builder.HasMany(f => f.Options)
            .WithOne(o => o.CustomFieldDefinition)
            .HasForeignKey(o => o.CustomFieldDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
