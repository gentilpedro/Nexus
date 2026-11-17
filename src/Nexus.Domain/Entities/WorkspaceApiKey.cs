namespace Nexus.Domain.Entities;

// Chave que um sistema externo (ERP, script) usa na API de integração. Só o hash fica no banco:
// o valor completo aparece uma única vez, na criação. O prefixo serve para a pessoa reconhecer a
// chave na lista sem revelar nada dela.
public class WorkspaceApiKey
{
    public const int MaxNameLength = 80;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Name { get; set; } = "";

    public ApiKeyScope Scope { get; set; } = ApiKeyScope.ReadWrite;

    public string KeyPrefix { get; set; } = "";
    public string KeyHash { get; set; } = "";

    // SetNull: a chave continua válida mesmo se a conta de quem a criou for anonimizada.
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null;
}
