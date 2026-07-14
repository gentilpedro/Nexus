using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class SprintBurndownSnapshotConfiguration : IEntityTypeConfiguration<SprintBurndownSnapshot>
{
    public void Configure(EntityTypeBuilder<SprintBurndownSnapshot> builder)
    {
        // One snapshot per sprint per day — lets the daily job upsert idempotently.
        builder.HasIndex(s => new { s.SprintId, s.SnapshotDateUtc }).IsUnique();
    }
}
