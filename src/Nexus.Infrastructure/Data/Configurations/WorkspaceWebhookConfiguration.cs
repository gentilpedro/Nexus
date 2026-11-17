using Nexus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nexus.Infrastructure.Data.Configurations;

public class WorkspaceWebhookConfiguration : IEntityTypeConfiguration<WorkspaceWebhook>
{
    public void Configure(EntityTypeBuilder<WorkspaceWebhook> builder)
    {
        builder.Property(w => w.Name).HasMaxLength(WorkspaceWebhook.MaxNameLength).IsRequired();
        builder.Property(w => w.Url).HasMaxLength(WorkspaceWebhook.MaxUrlLength).IsRequired();

        // Segredo cifrado pelo Data Protection: o texto cifrado é maior que o segredo.
        builder.Property(w => w.SecretProtected).HasMaxLength(1024).IsRequired();

        builder.HasIndex(w => w.WorkspaceId);

        builder.HasOne(w => w.Workspace)
            .WithMany()
            .HasForeignKey(w => w.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.CreatedByUser)
            .WithMany()
            .HasForeignKey(w => w.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.Property(d => d.EventType).HasMaxLength(64).IsRequired();
        builder.Property(d => d.ResponseSummary).HasMaxLength(WebhookDelivery.MaxResponseSummaryLength);

        // O job de entrega procura "pendentes com hora de tentar"; a tela lista por webhook.
        builder.HasIndex(d => new { d.Status, d.NextAttemptAtUtc });
        builder.HasIndex(d => new { d.WebhookId, d.CreatedAtUtc });

        builder.HasOne(d => d.Webhook)
            .WithMany(w => w.Deliveries)
            .HasForeignKey(d => d.WebhookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
