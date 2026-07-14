using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> builder)
    {
        builder.Property(v => v.Value).HasMaxLength(2000).IsRequired();

        // Cascade: deleting a WorkItem should delete its custom field values.
        builder.HasOne(v => v.WorkItem)
            .WithMany(w => w.CustomFieldValues)
            .HasForeignKey(v => v.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: TaskList -> WorkItem already cascades directly, and
        // TaskList -> CustomFieldDefinition also cascades — a second cascading path from
        // TaskList into CustomFieldValues (via CustomFieldDefinition) hits the same
        // "multiple cascade paths" SQL Server error already seen with ParentEpicId/SprintId.
        // Deleting a definition must explicitly ExecuteDeleteAsync its values first.
        builder.HasOne(v => v.CustomFieldDefinition)
            .WithMany()
            .HasForeignKey(v => v.CustomFieldDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.WorkItemId, v.CustomFieldDefinitionId }).IsUnique();
    }
}
