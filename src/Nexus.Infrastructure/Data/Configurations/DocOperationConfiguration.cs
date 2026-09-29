using Nexus.Domain.Collab;
using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class DocOperationConfiguration : IEntityTypeConfiguration<DocOperation>
{
    public void Configure(EntityTypeBuilder<DocOperation> builder)
    {
        builder.Property(o => o.ChangeJson).HasMaxLength(DocDeltaPolicy.MaxChangeJsonLength).IsRequired();

        // Uma revisão por documento: duas instâncias que tentassem gravar a mesma revisão ao
        // mesmo tempo esbarram aqui, além do token de concorrência em DocPage.Revision.
        builder.HasIndex(o => new { o.DocPageId, o.Revision }).IsUnique();

        // Reenvio da mesma alteração (resposta perdida, reconexão) não pode aplicá-la de novo.
        builder.HasIndex(o => new { o.DocPageId, o.ClientId, o.ClientSeq }).IsUnique();

        // O log pertence ao documento: excluir o documento apaga o log.
        builder.HasOne(o => o.DocPage)
            .WithMany()
            .HasForeignKey(o => o.DocPageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict pelo mesmo motivo de DocPage.CreatedByUserId: a exclusão de conta anonimiza o
        // usuário em vez de apagar a linha, então a referência nunca fica órfã.
        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
