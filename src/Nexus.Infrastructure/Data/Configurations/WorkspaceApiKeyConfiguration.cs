using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkspaceApiKeyConfiguration : IEntityTypeConfiguration<WorkspaceApiKey>
{
    public void Configure(EntityTypeBuilder<WorkspaceApiKey> builder)
    {
        builder.Property(k => k.Name).HasMaxLength(WorkspaceApiKey.MaxNameLength).IsRequired();
        builder.Property(k => k.KeyPrefix).HasMaxLength(16).IsRequired();

        // Chaves criadas antes do escopo existir (1.16.0) continuam lendo e escrevendo.
        builder.Property(k => k.Scope).HasDefaultValue(ApiKeyScope.ReadWrite);

        // SHA-256 em hexadecimal: sempre 64 caracteres.
        builder.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();

        // Toda requisição da API começa por esta busca.
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasIndex(k => k.WorkspaceId);

        builder.HasOne(k => k.Workspace)
            .WithMany()
            .HasForeignKey(k => k.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(k => k.CreatedByUser)
            .WithMany()
            .HasForeignKey(k => k.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
