using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nexus.Web.Services;

namespace Nexus.Web.Api;

// Limite de chamadas da API de integração (#66, #74): 120 por minuto por chave. É o GlobalLimiter
// do app porque o endpoint precisa da mesma instância para responder X-RateLimit-Remaining; fora
// de /api/v1 a partição é "sem limite" e não custa nada. Por chave, não por IP: um ERP inteiro
// costuma sair por um único endereço. O limitador roda antes da autenticação do endpoint, então a
// partição usa o hash do cabeçalho Authorization (nunca o valor em si) e cai para o IP sem ele.
public sealed class IntegrationApiRateLimiter : IDisposable
{
    public const int PermitLimit = 120;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public PartitionedRateLimiter<HttpContext> Limiter { get; } = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        IsIntegrationApi(http)
            ? RateLimitPartition.GetFixedWindowLimiter(PartitionKey(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter("none"));

    /// <summary>Quantas chamadas ainda cabem na janela atual desta chave, depois da chamada corrente.</summary>
    public long Remaining(HttpContext http) => Limiter.GetStatistics(http)?.CurrentAvailablePermits ?? PermitLimit;

    public const string McpRoute = "/mcp";

    // O servidor MCP (#67) usa a mesma chave e entra no mesmo limite: uma chamada de ferramenta
    // custa o mesmo que uma chamada da API.
    public static bool IsIntegrationApi(HttpContext http) =>
        http.Request.Path.StartsWithSegments(IntegrationApiEndpoints.RoutePrefix, StringComparison.OrdinalIgnoreCase)
        || http.Request.Path.StartsWithSegments(McpRoute, StringComparison.OrdinalIgnoreCase);

    private static string PartitionKey(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.Length > 0
            ? "key:" + WorkspaceApiKeyService.Hash(header)
            : "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    // Vale para todo 429 do app: Retry-After é útil em qualquer rota. O corpo em problem+json só
    // vai para a API; as páginas de login continuam com a resposta vazia de antes.
    public static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        if (!IsIntegrationApi(context.HttpContext))
        {
            return;
        }

        response.Headers["X-RateLimit-Limit"] = PermitLimit.ToString();
        response.Headers["X-RateLimit-Remaining"] = "0";
        await response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Limite de chamadas atingido",
            Detail = $"Esta chave pode fazer {PermitLimit} chamadas por minuto. Espere os segundos indicados em Retry-After e tente de novo.",
        }, options: null, contentType: "application/problem+json", ct);
    }

    public void Dispose() => Limiter.Dispose();
}
