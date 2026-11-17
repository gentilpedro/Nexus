using System.Text.Json.Serialization;

namespace Nexus.Licensing;

/// <summary>
/// O conteúdo assinado de uma chave de licença. É o que a Aamora emite e o que a instância lê;
/// nada aqui é segredo, a proteção está na assinatura.
/// </summary>
/// <param name="LicenseId">Identificador da licença, para a Aamora achar o contrato.</param>
/// <param name="Customer">Nome do cliente, mostrado na tela de licença.</param>
/// <param name="Plan">Plano licenciado. Hoje só <c>Business</c>.</param>
/// <param name="MaxActiveUsers">Teto de usuários ativos (entraram nos últimos 30 dias). Null = ilimitado.</param>
/// <param name="IssuedAt">Quando a chave foi emitida.</param>
/// <param name="ExpiresAt">Fim da assinatura. Depois disso a instalação continua, mas avisa.</param>
/// <param name="UpdatesUntil">Até quando uma imagem nova pode subir com esta chave: a build precisa ser anterior a esta data.</param>
public sealed record LicenseClaims(
    [property: JsonPropertyName("licenseId")] string LicenseId,
    [property: JsonPropertyName("customer")] string Customer,
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("maxActiveUsers")] int? MaxActiveUsers,
    [property: JsonPropertyName("issuedAt")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("updatesUntil")] DateTimeOffset UpdatesUntil)
{
    public const string BusinessPlan = "Business";

    /// <summary>Carência padrão entre o vencimento e o bloqueio de atualizações.</summary>
    public static readonly TimeSpan DefaultUpdateGrace = TimeSpan.FromDays(7);

    public bool IsUnlimited => MaxActiveUsers is null;
}
