using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class CommercialContactConfiguration : IEntityTypeConfiguration<CommercialContact>
{
    public void Configure(EntityTypeBuilder<CommercialContact> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(CommercialContact.MaxNameLength).IsRequired();
        builder.Property(c => c.Company).HasMaxLength(CommercialContact.MaxCompanyLength).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(CommercialContact.MaxEmailLength).IsRequired();
        builder.Property(c => c.TeamSize).HasMaxLength(CommercialContact.MaxTeamSizeLength).IsRequired();
        builder.Property(c => c.Message).HasMaxLength(CommercialContact.MaxMessageLength);

        // The retention sweep deletes by age.
        builder.HasIndex(c => c.CreatedAtUtc);
    }
}
