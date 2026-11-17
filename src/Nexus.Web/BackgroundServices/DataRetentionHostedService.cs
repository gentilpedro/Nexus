using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.BackgroundServices;

/// <summary>
/// LGPD data-minimization sweep (art. 6, III / art. 16): purges records that have outlived
/// their purpose instead of keeping them indefinitely. Runs a pass immediately on startup and
/// then once every 24h — daily is enough since retention windows are measured in months, not
/// hours; each pass is a plain age-based delete, so re-running it is always safe.
/// </summary>
public class DataRetentionHostedService(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<DataRetentionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PurgeAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PurgeAsync(stoppingToken);
        }
    }

    internal async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            // AuditLogEntry exists to defend against a dispute over a data-subject request or
            // an access-control change (art. 46) — it has no purpose once that window has
            // reasonably passed. Default mirrors common practice for this kind of security/audit
            // trail; overridable via config since "reasonable" depends on the controller's own
            // risk assessment, not something to hardcode permanently.
            var auditLogRetentionDays = configuration.GetValue("AUDIT_LOG_RETENTION_DAYS", 730);
            var notificationRetentionDays = configuration.GetValue("NOTIFICATION_RETENTION_DAYS", 180);
            var commercialContactRetentionDays = configuration.GetValue("COMMERCIAL_CONTACT_RETENTION_DAYS", 365);

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var auditLogCutoff = DateTime.UtcNow.AddDays(-auditLogRetentionDays);
            var deletedAuditLogs = await db.AuditLogEntries
                .Where(a => a.CreatedAtUtc < auditLogCutoff)
                .ExecuteDeleteAsync(ct);

            // Notifications are transient UX state, not a record anyone needs to defend a
            // decision later — read or unread, once stale they're just accumulated PII
            // (message text references work item titles/actors) with no remaining purpose.
            var notificationCutoff = DateTime.UtcNow.AddDays(-notificationRetentionDays);
            var deletedNotifications = await db.Notifications
                .Where(n => n.CreatedAtUtc < notificationCutoff)
                .ExecuteDeleteAsync(ct);

            // A Business-plan contact request exists to answer one sales conversation. A year is
            // long enough for that conversation to have happened, and the privacy policy promises it.
            var commercialContactCutoff = DateTime.UtcNow.AddDays(-commercialContactRetentionDays);
            var deletedContacts = await db.CommercialContacts
                .Where(c => c.CreatedAtUtc < commercialContactCutoff)
                .ExecuteDeleteAsync(ct);

            // Processed Stripe event ids only guard against redelivery, which Stripe stops after
            // three days of retries; 90 days is ample and keeps the table small. No personal data.
            var stripeEventCutoff = DateTime.UtcNow.AddDays(-90);
            await db.StripeWebhookEvents
                .Where(e => e.ProcessedAtUtc < stripeEventCutoff)
                .ExecuteDeleteAsync(ct);

            // Entregas de webhook (#75) existem para a aba Integrações mostrar o que foi avisado ao
            // cliente e para reenviar; um mês cobre qualquer investigação. O corpo carrega título e
            // descrição da tarefa, então também é dado que não precisa viver para sempre.
            var webhookDeliveryRetentionDays = configuration.GetValue("WEBHOOK_DELIVERY_RETENTION_DAYS", 30);
            var webhookDeliveryCutoff = DateTime.UtcNow.AddDays(-webhookDeliveryRetentionDays);
            await db.WebhookDeliveries
                .Where(d => d.Status != WebhookDeliveryStatus.Pending && d.CreatedAtUtc < webhookDeliveryCutoff)
                .ExecuteDeleteAsync(ct);

            if (deletedAuditLogs > 0 || deletedNotifications > 0 || deletedContacts > 0)
            {
                logger.LogInformation(
                    "Data retention sweep purged {AuditLogCount} audit log entries, {NotificationCount} notifications and {ContactCount} commercial contacts.",
                    deletedAuditLogs, deletedNotifications, deletedContacts);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missed pass shouldn't crash the app — the next daily tick retries.
            logger.LogError(ex, "Failed to run data retention sweep.");
        }
    }
}
