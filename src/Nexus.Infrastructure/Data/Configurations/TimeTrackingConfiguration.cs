using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkspaceContractConfiguration : IEntityTypeConfiguration<WorkspaceContract>
{
    public void Configure(EntityTypeBuilder<WorkspaceContract> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(WorkspaceContract.MaxNameLength).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(WorkspaceContract.MaxNotesLength);
        builder.Property(c => c.ContractedHours).HasPrecision(9, 2);

        builder.HasIndex(c => new { c.WorkspaceId, c.Status });

        builder.HasOne(c => c.Workspace)
            .WithMany()
            .HasForeignKey(c => c.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TimeActivityConfiguration : IEntityTypeConfiguration<TimeActivity>
{
    public void Configure(EntityTypeBuilder<TimeActivity> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(TimeActivity.MaxNameLength).IsRequired();
        builder.HasIndex(a => new { a.WorkspaceId, a.SortOrder });

        builder.HasOne(a => a.Workspace)
            .WithMany()
            .HasForeignKey(a => a.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.Property(e => e.Hours).HasPrecision(6, 2);
        builder.Property(e => e.Comment).HasMaxLength(TimeEntry.MaxCommentLength);

        // Relatórios por período e por pessoa; totais por tarefa; saldo por contrato.
        builder.HasIndex(e => new { e.WorkspaceId, e.Date });
        builder.HasIndex(e => new { e.WorkspaceId, e.UserId, e.Date });
        builder.HasIndex(e => e.WorkItemId);
        builder.HasIndex(e => e.ContractId);

        builder.HasOne(e => e.Workspace)
            .WithMany()
            .HasForeignKey(e => e.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Apagar a conta não apaga as horas (são do contrato, não da pessoa); a anonimização já
        // tira o nome. Restrict para não abrir um segundo caminho em cascata a partir de AspNetUsers.
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Apagar a tarefa deixa a hora avulsa: o tempo foi trabalhado de qualquer jeito.
        builder.HasOne(e => e.WorkItem)
            .WithMany()
            .HasForeignKey(e => e.WorkItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Contract)
            .WithMany(c => c.TimeEntries)
            .HasForeignKey(e => e.ContractId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Activity)
            .WithMany()
            .HasForeignKey(e => e.ActivityId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class RunningTimerConfiguration : IEntityTypeConfiguration<RunningTimer>
{
    public void Configure(EntityTypeBuilder<RunningTimer> builder)
    {
        builder.Property(t => t.Note).HasMaxLength(RunningTimer.MaxNoteLength);

        // Um timer por pessoa.
        builder.HasIndex(t => t.UserId).IsUnique();

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Workspace)
            .WithMany()
            .HasForeignKey(t => t.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.WorkItem)
            .WithMany()
            .HasForeignKey(t => t.WorkItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
