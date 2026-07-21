using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class TaskStatusDefinitionConfiguration : IEntityTypeConfiguration<TaskStatusDefinition>
{
    public void Configure(EntityTypeBuilder<TaskStatusDefinition> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Color).HasMaxLength(20);
    }
}
