namespace Nexus.Domain.Entities;

// Webhook de saída (#75): uma URL do cliente que o Nexus chama quando algo muda no workspace.
// O segredo assina cada entrega (HMAC), então precisa ser lido de volta: fica cifrado pelo Data
// Protection, não em hash como a chave de API. É mostrado uma vez, na criação.
public class WorkspaceWebhook
{
    public const int MaxUrlLength = 500;
    public const int MaxNameLength = 80;

    // Entregas que falharam de vez, em sequência, antes de o Nexus pausar o webhook sozinho.
    public const int ConsecutiveFailuresBeforePause = 50;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string SecretProtected { get; set; } = "";
    public WebhookEvents Events { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? PausedAtUtc { get; set; }
    public int ConsecutiveFailures { get; set; }

    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastDeliveryAtUtc { get; set; }

    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();

    public bool Subscribes(WebhookEvents single) => (Events & single) == single;
}
