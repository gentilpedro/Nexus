using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.Webhooks;

// Entrega as filas de webhook (#75). Roda pelo WebhookDeliveryHostedService a cada poucos segundos
// e pega o que está pendente com hora de tentar. Cada entrega é um POST assinado; 2xx conta como
// entregue, qualquer outra coisa agenda a próxima tentativa (1, 5 e 30 minutos) e, na quarta
// falha, encerra. Falhas finais seguidas demais pausam o webhook e avisam os administradores.
public class WebhookSender(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpClientFactory httpClientFactory,
    WebhookService webhooks,
    ILogger<WebhookSender> logger,
    TimeProvider? timeProvider = null)
{
    public const string HttpClientName = "nexus-webhooks";
    public const int BatchSize = 20;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    /// <returns>Quantas entregas foram tentadas nesta rodada.</returns>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.WebhookDeliveries
            .Include(d => d.Webhook)
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAtUtc <= now)
            .OrderBy(d => d.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var delivery in due)
        {
            await SendOneAsync(db, delivery, ct);
        }

        return due.Count;
    }

    private async Task SendOneAsync(AppDbContext db, WebhookDelivery delivery, CancellationToken ct)
    {
        var webhook = delivery.Webhook;
        var now = clock.GetUtcNow().UtcDateTime;
        delivery.Attempts++;
        delivery.LastAttemptAtUtc = now;

        if (!webhook.IsActive)
        {
            // Pausado depois de a entrega entrar na fila: não bate em quem pediu para parar.
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.ResponseSummary = "Webhook pausado antes da entrega.";
            await db.SaveChangesAsync(ct);
            return;
        }

        int? statusCode = null;
        string summary;
        bool succeeded;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
            request.Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("X-Nexus-Event", delivery.EventType);
            request.Headers.TryAddWithoutValidation("X-Nexus-Delivery", delivery.Id.ToString());
            request.Headers.TryAddWithoutValidation("X-Nexus-Signature", Sign(webhooks.Unprotect(webhook.SecretProtected), delivery.Payload));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Nexus-Webhooks", "1.0"));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            statusCode = (int)response.StatusCode;
            succeeded = response.IsSuccessStatusCode;
            var body = await ReadPrefixAsync(response, timeout.Token);
            summary = $"{statusCode} {response.ReasonPhrase}".Trim() + (body.Length > 0 ? $": {body}" : "");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            succeeded = false;
            summary = $"Sem resposta em {RequestTimeout.TotalSeconds:0} segundos.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            succeeded = false;
            summary = ex.Message;
        }

        delivery.ResponseStatusCode = statusCode;
        delivery.ResponseSummary = summary.Length > WebhookDelivery.MaxResponseSummaryLength ? summary[..WebhookDelivery.MaxResponseSummaryLength] : summary;

        if (succeeded)
        {
            delivery.Status = WebhookDeliveryStatus.Delivered;
            delivery.DeliveredAtUtc = now;
            webhook.ConsecutiveFailures = 0;
            webhook.LastDeliveryAtUtc = now;
        }
        else if (WebhookDelivery.DelayAfterFailure(delivery.Attempts) is { } delay)
        {
            delivery.NextAttemptAtUtc = now + delay;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            webhook.ConsecutiveFailures++;
            logger.LogWarning("Webhook {WebhookId} ({Url}) falhou de vez na entrega {DeliveryId}: {Summary}", webhook.Id, webhook.Url, delivery.Id, summary);
            if (webhook.ConsecutiveFailures >= WorkspaceWebhook.ConsecutiveFailuresBeforePause)
            {
                await PauseAsync(db, webhook, now, ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string> ReadPrefixAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[256];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
            return Encoding.UTF8.GetString(buffer, 0, read).ReplaceLineEndings(" ").Trim();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException)
        {
            return "";
        }
    }

    // Pausa e avisa quem administra o workspace: um webhook morto que continua tentando só enche
    // o log e esconde o problema.
    private static async Task PauseAsync(AppDbContext db, WorkspaceWebhook webhook, DateTime now, CancellationToken ct)
    {
        webhook.IsActive = false;
        webhook.PausedAtUtc = now;

        var admins = await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == webhook.WorkspaceId && (m.Role == WorkspaceRole.Owner || m.Role == WorkspaceRole.Admin))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        foreach (var userId in admins)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = NotificationType.WebhookPaused,
                Message = Notification.TruncateMessage($"O webhook \"{webhook.Name}\" foi pausado depois de {WorkspaceWebhook.ConsecutiveFailuresBeforePause} falhas seguidas. Confira a URL em Configurações do workspace, Integrações."),
                WorkspaceId = webhook.WorkspaceId,
                LinkUrl = $"/workspaces/{webhook.WorkspaceId}/settings",
                IsRead = false,
                CreatedAtUtc = now,
            });
        }
    }
}
