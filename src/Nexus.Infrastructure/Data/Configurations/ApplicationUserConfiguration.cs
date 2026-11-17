using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.AvatarStoragePath).HasMaxLength(500);
        builder.Property(u => u.AvatarContentType).HasMaxLength(200);

        // Toda requisição do feed de calendário começa por esta busca.
        builder.Property(u => u.CalendarFeedTokenHash).HasMaxLength(64);
        builder.Property(u => u.CalendarFeedTokenProtected).HasMaxLength(1024);
        builder.HasIndex(u => u.CalendarFeedTokenHash).IsUnique().HasFilter("\"CalendarFeedTokenHash\" IS NOT NULL");
    }
}
