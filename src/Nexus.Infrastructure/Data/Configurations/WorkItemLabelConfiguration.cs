using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkItemLabelConfiguration : IEntityTypeConfiguration<WorkItemLabel>
{
    public void Configure(EntityTypeBuilder<WorkItemLabel> builder)
    {
        // Cascade: deleting a WorkItem should delete its label assignments.
        builder.HasOne(l => l.WorkItem)
            .WithMany(w => w.WorkItemLabels)
            .HasForeignKey(l => l.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: TaskList already cascades directly to WorkItem AND to
        // Label — a second cascading path from TaskList into WorkItemLabels (via Label)
        // hits the same "multiple cascade paths" SQL Server error already seen with
        // CustomFieldValue.CustomFieldDefinitionId. Deleting a Label must explicitly
        // ExecuteDeleteAsync its WorkItemLabels first (see LabelsManage.razor).
        builder.HasOne(l => l.Label)
            .WithMany()
            .HasForeignKey(l => l.LabelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.WorkItemId, l.LabelId }).IsUnique();
    }
}
