using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Web.BackgroundServices;

/// <summary>
/// Notifies an assignee once when their task's due date is today or tomorrow. Runs a pass
/// immediately on startup and then re-checks hourly; each (WorkItemId, AssigneeId) pair gets
/// at most one DueDateApproaching notification ever (not per day), so repeated checks are
/// harmless — same "write once, safe to re-run" shape as SprintSnapshotHostedService.
/// </summary>
/// <remarks>
/// DEPLOYMENT LIMITATION: this runs in-process. On the current IIS shared host the application
/// pool is recycled and idled out when there is no traffic, which stops this service — due-date
/// notifications are only sent while someone happens to be using the app, and the hourly cadence
/// is not guaranteed. The same applies to <see cref="DataRetentionHostedService"/>, where a
/// missed pass means LGPD retention limits are not enforced on schedule. Making this reliable
/// means either an always-on host (`Application Initialization` / `AlwaysRunning` start mode) or
/// moving these passes to an external scheduler that calls in. Tracked as M13 in the
/// pre-production audit.
/// </remarks>
public class DueDateNotificationHostedService(
    IDbContextFactory<AppDbContext> dbFactory,
    BrevoMailer mailer,
    IConfiguration configuration,
    ILogger<DueDateNotificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CheckDueDatesAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CheckDueDatesAsync(stoppingToken);
        }
    }

    private async Task CheckDueDatesAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var dueSoon = await db.WorkItems
                .Where(w => w.AssigneeId != null
                    && w.DueDateUtc != null
                    && (w.DueDateUtc.Value.Date == today || w.DueDateUtc.Value.Date == tomorrow)
                    && w.Status.Category != StatusCategory.Done)
                .Select(w => new { w.Id, w.Title, w.AssigneeId, w.TaskListId, AssigneeEmail = w.Assignee!.Email, DueDate = w.DueDateUtc!.Value.Date })
                .ToListAsync(ct);

            var dueSoonIds = dueSoon.Select(w => w.Id).ToList();
            var alreadyNotifiedPairs = (await db.Notifications
                    .Where(n => dueSoonIds.Contains(n.WorkItemId!.Value) && n.Type == NotificationType.DueDateApproaching)
                    .Select(n => new { n.WorkItemId, n.UserId })
                    .ToListAsync(ct))
                .Select(n => (n.WorkItemId!.Value, n.UserId))
                .ToHashSet();

            var baseUrl = configuration.GetValue("PUBLIC_BASE_URL", "http://localhost:5289")!.TrimEnd('/');

            foreach (var item in dueSoon)
            {
                if (alreadyNotifiedPairs.Contains((item.Id, item.AssigneeId!)))
                {
                    continue;
                }

                var when = item.DueDate == today ? "hoje" : "amanhã";
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = item.AssigneeId!,
                    Type = NotificationType.DueDateApproaching,
                    Message = Notification.TruncateMessage($"\"{item.Title}\" vence {when}."),
                    WorkItemId = item.Id,
                    IsRead = false,
                    CreatedAtUtc = DateTime.UtcNow
                });

                // Persist the "already notified" row before sending the email, not after — if the
                // process dies mid-pass, we'd rather miss an email than resend a duplicate once
                // the row never made it to disk but the email already went out.
                await db.SaveChangesAsync(ct);

                if (!string.IsNullOrEmpty(item.AssigneeEmail))
                {
                    var content = EmailTemplate.Render(
                        title: $"Uma tarefa sua vence {when}",
                        preview: $"{item.Title} vence {when}.",
                        paragraphs: ["Esta tarefa está atribuída a você e o prazo está chegando."],
                        button: new EmailButton("Abrir a lista", $"{baseUrl}/lists/{item.TaskListId}/list"),
                        details:
                        [
                            new EmailDetail("Tarefa", item.Title),
                            new EmailDetail("Prazo", when),
                        ]);

                    await mailer.SendAsync(
                        item.AssigneeEmail,
                        $"Tarefa vence {when} no Nexus",
                        content.Html,
                        content.Text);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missed pass shouldn't crash the app — the next hourly tick retries.
            logger.LogError(ex, "Failed to check due-date notifications.");
        }
    }
}
