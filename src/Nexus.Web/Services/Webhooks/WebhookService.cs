using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.Webhooks;

public sealed class WebhookOptions
{
    // Só para desenvolvimento: aceita http:// e endereços locais (localhost, 10.x, 192.168.x).
    // Em produção fica false, para um webhook não virar uma porta para a rede interna da VPS.
    public bool AllowInsecureUrls { get; set; }
}

public enum WebhookCreateStatus
{
    Created,
    Invalid,
    LimitReached,
    PlanDoesNotIncludeApi,
}

/// <param name="Secret">O segredo que assina as entregas. Só existe aqui: o banco guarda cifrado.</param>
public sealed record WebhookCreateResult(WebhookCreateStatus Status, WorkspaceWebhook? Webhook = null, string? Secret = null, string? Error = null);

// Webhooks de saída (#75): cadastro, fila de entregas e o segredo de assinatura. O segredo é
// "whsec_" + 32 bytes aleatórios, cifrado pelo Data Protection porque o job de entrega precisa
// dele em texto para assinar; a chave de API, que só é comparada, fica em hash.
public class WebhookService(
    IDbContextFactory<AppDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    AuditLogService audit,
    WorkspacePlanService plans,
    IOptions<WebhookOptions>? options = null,
    TimeProvider? timeProvider = null)
{
    public const string SecretPrefix = "whsec_";
    public const int MaxWebhooksPerWorkspace = 10;
    public const int DeliveriesShown = 20;

    private readonly IDataProtector protector = dataProtection.CreateProtector("Nexus.Webhooks.Secret.v1");
    private readonly bool allowInsecureUrls = options?.Value.AllowInsecureUrls ?? false;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public static string NewSecret() => SecretPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public string Unprotect(string protectedSecret) => protector.Unprotect(protectedSecret);

    /// <returns>Null quando a URL serve; senão a mensagem para a tela.</returns>
    public string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > WorkspaceWebhook.MaxUrlLength
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return "Informe uma URL completa, como https://erp.suaempresa.com.br/nexus/webhook.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "A URL não pode ter usuário e senha. Use o segredo do webhook para autenticar.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(allowInsecureUrls && uri.Scheme == Uri.UriSchemeHttp))
        {
            return "A URL precisa começar com https://.";
        }

        if (!allowInsecureUrls && IsPrivateHost(uri))
        {
            return "A URL precisa ser um endereço público na internet, não um endereço local ou de rede interna.";
        }

        return null;
    }

    // Defesa contra SSRF: um Admin de workspace não pode usar o Nexus para bater em serviços da
    // própria VPS ou da rede interna. Cobre os casos óbvios; DNS que resolve para IP interno não.
    private static bool IsPrivateHost(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        if (host is "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal"))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            return false;
        }

        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 127);
    }

    public async Task<WebhookCreateResult> CreateAsync(Guid workspaceId, string? name, string? url, WebhookEvents events, string? createdByUserId, CancellationToken ct = default)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > WorkspaceWebhook.MaxNameLength)
        {
            return new WebhookCreateResult(WebhookCreateStatus.Invalid, Error: "Dê um nome ao webhook, como o sistema que vai recebê-lo.");
        }

        if (ValidateUrl(url) is { } urlError)
        {
            return new WebhookCreateResult(WebhookCreateStatus.Invalid, Error: urlError);
        }

        if ((events & WebhookEvents.All) == WebhookEvents.None)
        {
            return new WebhookCreateResult(WebhookCreateStatus.Invalid, Error: "Escolha pelo menos um evento.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await plans.HasApiAccessAsync(db, workspaceId, ct))
        {
            return new WebhookCreateResult(WebhookCreateStatus.PlanDoesNotIncludeApi);
        }

        if (await db.WorkspaceWebhooks.CountAsync(w => w.WorkspaceId == workspaceId, ct) >= MaxWebhooksPerWorkspace)
        {
            return new WebhookCreateResult(WebhookCreateStatus.LimitReached);
        }

        var secret = NewSecret();
        var webhook = new WorkspaceWebhook
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name,
            Url = url!.Trim(),
            SecretProtected = protector.Protect(secret),
            Events = events & WebhookEvents.All,
            IsActive = true,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.WorkspaceWebhooks.Add(webhook);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(createdByUserId, AuditAction.WorkspaceWebhookCreated, webhook.Id.ToString(), $"{name} ({webhook.Url}) no workspace {workspaceId}", ct);
        return new WebhookCreateResult(WebhookCreateStatus.Created, webhook, secret);
    }

    public async Task<List<WorkspaceWebhook>> ListAsync(Guid workspaceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceWebhooks
            .AsNoTracking()
            .Where(w => w.WorkspaceId == workspaceId)
            .OrderBy(w => w.CreatedAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>Pausar ou reativar. Reativar zera as falhas seguidas, senão a próxima pausaria de novo.</summary>
    public async Task<bool> SetActiveAsync(Guid workspaceId, Guid webhookId, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var webhook = await db.WorkspaceWebhooks.FirstOrDefaultAsync(w => w.Id == webhookId && w.WorkspaceId == workspaceId, ct);
        if (webhook is null)
        {
            return false;
        }

        webhook.IsActive = active;
        webhook.PausedAtUtc = active ? null : clock.GetUtcNow().UtcDateTime;
        if (active)
        {
            webhook.ConsecutiveFailures = 0;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid workspaceId, Guid webhookId, string? actorUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var webhook = await db.WorkspaceWebhooks.FirstOrDefaultAsync(w => w.Id == webhookId && w.WorkspaceId == workspaceId, ct);
        if (webhook is null)
        {
            return false;
        }

        db.WorkspaceWebhooks.Remove(webhook);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(actorUserId, AuditAction.WorkspaceWebhookDeleted, webhook.Id.ToString(), $"{webhook.Name} ({webhook.Url}) no workspace {workspaceId}", ct);
        return true;
    }

    public async Task<List<WebhookDelivery>> ListDeliveriesAsync(Guid workspaceId, Guid webhookId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.WebhookId == webhookId && d.Webhook.WorkspaceId == workspaceId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(DeliveriesShown)
            .ToListAsync(ct);
    }

    /// <summary>Dispara um evento "ping" só para este webhook, para o cliente conferir URL e assinatura.</summary>
    public async Task<bool> SendTestAsync(Guid workspaceId, Guid webhookId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var webhook = await db.WorkspaceWebhooks.FirstOrDefaultAsync(w => w.Id == webhookId && w.WorkspaceId == workspaceId, ct);
        if (webhook is null)
        {
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var eventId = Guid.NewGuid();
        var payload = WebhookPayloads.Serialize(new
        {
            id = eventId,
            type = WebhookEventNames.Ping,
            occurredAtUtc = now,
            workspaceId,
            webhook = new { id = webhook.Id, name = webhook.Name },
        });
        db.WebhookDeliveries.Add(NewDelivery(webhook.Id, WebhookEventNames.Ping, eventId, payload, now));
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Repete uma entrega como uma entrega nova, com o mesmo evento e corpo.</summary>
    public async Task<bool> ResendAsync(Guid workspaceId, Guid deliveryId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var delivery = await db.WebhookDeliveries
            .Include(d => d.Webhook)
            .FirstOrDefaultAsync(d => d.Id == deliveryId && d.Webhook.WorkspaceId == workspaceId, ct);
        if (delivery is null)
        {
            return false;
        }

        db.WebhookDeliveries.Add(NewDelivery(delivery.WebhookId, delivery.EventType, delivery.EventId, delivery.Payload, clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Alguém no workspace assina este evento? Barato, para quem publica não montar o corpo à toa.</summary>
    public Task<bool> AnySubscriberAsync(AppDbContext db, Guid workspaceId, WebhookEvents single, CancellationToken ct = default) =>
        db.WorkspaceWebhooks.AnyAsync(w => w.WorkspaceId == workspaceId && w.IsActive && (w.Events & single) == single, ct);

    /// <summary>
    /// Enfileira o evento para todo webhook ativo do workspace que o assina. Só adiciona ao
    /// contexto: quem chama salva, junto com a mudança que originou o evento.
    /// </summary>
    public async Task EnqueueAsync(AppDbContext db, Guid workspaceId, WebhookEvents single, Guid eventId, string payload, CancellationToken ct = default)
    {
        var webhookIds = await db.WorkspaceWebhooks
            .Where(w => w.WorkspaceId == workspaceId && w.IsActive && (w.Events & single) == single)
            .Select(w => w.Id)
            .ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var webhookId in webhookIds)
        {
            db.WebhookDeliveries.Add(NewDelivery(webhookId, WebhookEventNames.Of(single), eventId, payload, now));
        }
    }

    private static WebhookDelivery NewDelivery(Guid webhookId, string eventType, Guid eventId, string payload, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        WebhookId = webhookId,
        EventId = eventId,
        EventType = eventType,
        Payload = payload,
        Status = WebhookDeliveryStatus.Pending,
        Attempts = 0,
        CreatedAtUtc = now,
        NextAttemptAtUtc = now,
    };
}
