using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class DocPageConfiguration : IEntityTypeConfiguration<DocPage>
{
    public void Configure(EntityTypeBuilder<DocPage> builder)
    {
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();

        // Both were unbounded `text`. The SignalR hub accepts messages up to 11 MB (raised for
        // file uploads), so without a limit here an authenticated user could persist multi-megabyte
        // documents repeatedly — unbounded database growth, and every reader loads the whole value
        // into the circuit and renders it as a MarkupString.
        //
        // Generous on purpose: these are rich-text and spreadsheet payloads, not short fields.
        builder.Property(d => d.ContentHtml).HasMaxLength(DocPage.MaxContentHtmlLength);
        builder.Property(d => d.GridDataJson).HasMaxLength(DocPage.MaxGridDataJsonLength);

        builder.Property(d => d.DeltaJson).HasMaxLength(DocPage.MaxDeltaJsonLength);

        // A revisão é o que serializa a edição colaborativa: uma operação só é gravada se a
        // revisão no banco ainda for a que ela leu. Com duas instâncias aceitando operações do
        // mesmo documento, a segunda a gravar recebe DbUpdateConcurrencyException e trata o
        // pedido de novo, já vendo a revisão nova.
        builder.Property(d => d.Revision).IsConcurrencyToken();

        builder.Property(d => d.FileName).HasMaxLength(500);
        builder.Property(d => d.FileContentType).HasMaxLength(200);
        builder.Property(d => d.FileStoragePath).HasMaxLength(500);

        // Page belongs to the workspace's lifecycle — deleting the workspace deletes its pages.
        builder.HasOne(d => d.Workspace)
            .WithMany()
            .HasForeignKey(d => d.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade/SetNull: avoids a second cascading path from AspNetUsers
        // alongside Workspace->DocPage above — same reasoning as WorkItem.CreatedByUserId.
        builder.HasOne(d => d.CreatedByUser)
            .WithMany()
            .HasForeignKey(d => d.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.UpdatedByUser)
            .WithMany()
            .HasForeignKey(d => d.UpdatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
