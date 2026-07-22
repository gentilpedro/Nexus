using Microsoft.EntityFrameworkCore;
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

    private async Task PurgeAsync(CancellationToken ct)
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

            if (deletedAuditLogs > 0 || deletedNotifications > 0)
            {
                logger.LogInformation(
                    "Data retention sweep purged {AuditLogCount} audit log entries and {NotificationCount} notifications.",
                    deletedAuditLogs, deletedNotifications);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missed pass shouldn't crash the app — the next daily tick retries.
            logger.LogError(ex, "Failed to run data retention sweep.");
        }
    }
}
