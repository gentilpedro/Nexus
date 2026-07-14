using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class CustomFieldOptionConfiguration : IEntityTypeConfiguration<CustomFieldOption>
{
    public void Configure(EntityTypeBuilder<CustomFieldOption> builder)
    {
        builder.Property(o => o.Label).HasMaxLength(200).IsRequired();
    }
}
