using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class TaskListConfiguration : IEntityTypeConfiguration<TaskList>
{
    public void Configure(EntityTypeBuilder<TaskList> builder)
    {
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();

        builder.HasMany(l => l.Statuses)
            .WithOne(s => s.TaskList)
            .HasForeignKey(s => s.TaskListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.WorkItems)
            .WithOne(w => w.TaskList)
            .HasForeignKey(w => w.TaskListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.Sprints)
            .WithOne(s => s.TaskList)
            .HasForeignKey(s => s.TaskListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(l => l.CustomFieldDefinitions)
            .WithOne(f => f.TaskList)
            .HasForeignKey(f => f.TaskListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
