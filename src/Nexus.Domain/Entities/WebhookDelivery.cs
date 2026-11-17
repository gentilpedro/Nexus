namespace Nexus.Domain.Entities;

public enum WebhookDeliveryStatus
{
    Pending = 0,
    Delivered = 1,
    Failed = 2,
}

// Uma tentativa de avisar o cliente (#75). Nasce Pending na mesma transação que gravou a mudança,
// e o WebhookDeliveryHostedService a envia depois: nenhuma tela ou chamada da API espera o cliente
// responder. Guardada por 30 dias para a aba Integrações mostrar o que foi entregue.
public class WebhookDelivery
{
    public const int MaxAttempts = 4;
    public const int MaxResponseSummaryLength = 500;

    public Guid Id { get; set; }

    public Guid WebhookId { get; set; }
    public WorkspaceWebhook Webhook { get; set; } = null!;

    // O mesmo evento pode ir para vários webhooks; EventId é o que o cliente usa para deduplicar.
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";

    public WebhookDeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseSummary { get; set; }

    // 1, 5 e 30 minutos depois de cada falha; a quarta falha encerra.
    public static TimeSpan? DelayAfterFailure(int attemptsSoFar) => attemptsSoFar switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(30),
        _ => null,
    };
}
