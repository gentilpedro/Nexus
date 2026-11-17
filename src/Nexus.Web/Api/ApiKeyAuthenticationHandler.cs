using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nexus.Domain.Entities;
using Nexus.Web.Services;

namespace Nexus.Web.Api;

// Claims que a chave de API coloca no usuário da requisição. Os endpoints leem o workspace daqui,
// nunca da URL: uma chave só enxerga o workspace em que foi criada.
public static class ApiKeyClaims
{
    public const string KeyId = "nexus:api_key_id";
    public const string WorkspaceId = "nexus:workspace_id";
    public const string KeyName = "nexus:api_key_name";
    public const string CreatedByUserId = "nexus:api_key_created_by";
    public const string WorkspaceArchived = "nexus:workspace_archived";
    public const string Scope = "nexus:api_key_scope";
    public const string ApiAccessAllowed = "nexus:api_access_allowed";

    public static Guid GetWorkspaceId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(WorkspaceId) ?? throw new InvalidOperationException("Requisição sem chave de API autenticada."));

    public static string GetKeyName(this ClaimsPrincipal user) => user.FindFirstValue(KeyName) ?? "API";

    public static string? GetKeyCreatorUserId(this ClaimsPrincipal user) => user.FindFirstValue(CreatedByUserId);

    public static bool CanWrite(this ClaimsPrincipal user) => user.HasClaim(Scope, nameof(ApiKeyScope.ReadWrite));

    public static bool HasApiAccess(this ClaimsPrincipal user) => user.HasClaim(ApiAccessAllowed, "true");
}

// Esquema "ApiKey" da API de integração (#66), no padrão AuthenticationHandler do ASP.NET Core:
// lê "Authorization: Bearer nxk_...", valida pelo WorkspaceApiKeyService e monta o principal com
// os claims acima. Só os endpoints de /api/v1 pedem este esquema; o resto do app continua no
// cookie do Identity. Challenge e forbid respondem JSON (problem+json), nunca redirecionam para a
// tela de login como o cookie faria.
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    WorkspaceApiKeyService keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await keys.AuthenticateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Chave de API inválida ou revogada.");
        }

        var claims = new List<Claim>
        {
            new(ApiKeyClaims.KeyId, identity.KeyId.ToString()),
            new(ApiKeyClaims.WorkspaceId, identity.WorkspaceId.ToString()),
            new(ApiKeyClaims.KeyName, identity.Name),
            new(ClaimTypes.Name, identity.Name),
            new(ApiKeyClaims.WorkspaceArchived, identity.WorkspaceArchived ? "true" : "false"),
            new(ApiKeyClaims.Scope, identity.Scope.ToString()),
            new(ApiKeyClaims.ApiAccessAllowed, identity.ApiAccessAllowed ? "true" : "false"),
        };
        if (identity.CreatedByUserId is not null)
        {
            claims.Add(new Claim(ApiKeyClaims.CreatedByUserId, identity.CreatedByUserId));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer realm=\"Nexus API\"";
        return WriteProblemAsync(StatusCodes.Status401Unauthorized, "Não autenticado",
            "Envie uma chave de API válida em \"Authorization: Bearer <chave>\". Chaves são geradas nas configurações do workspace, em Integrações.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(StatusCodes.Status403Forbidden, "Sem permissão", "Esta chave não pode fazer essa operação.");

    private Task WriteProblemAsync(int statusCode, string title, string detail)
    {
        Response.StatusCode = statusCode;
        return Response.WriteAsJsonAsync(
            new ProblemDetails { Status = statusCode, Title = title, Detail = detail },
            options: null,
            contentType: "application/problem+json");
    }
}
