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

                if (!string.IsNullOrEmpty(item.AssigneeEmail))
                {
                    // Task title is user-controlled — encode before it goes into an HTML email body.
                    var safeTitle = System.Net.WebUtility.HtmlEncode(item.Title);
                    await mailer.SendAsync(
                        item.AssigneeEmail,
                        $"Tarefa vence {when} no Nexus",
                        $"<p>A tarefa <strong>{safeTitle}</strong> vence {when}.</p><p><a href='{baseUrl}/lists/{item.TaskListId}/list'>Clique aqui para abrir</a>.</p>");
                }
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missed pass shouldn't crash the app — the next hourly tick retries.
            logger.LogError(ex, "Failed to check due-date notifications.");
        }
    }
}
