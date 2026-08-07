using Microsoft.AspNetCore.Components;

namespace Nexus.Web.Services;

/// <summary>
/// Builds absolute URLs for links that leave the application (password reset, e-mail
/// confirmation, invites) from the configured <c>PUBLIC_BASE_URL</c> rather than from the
/// current request.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NavigationManager.ToAbsoluteUri"/> derives its base from the incoming request's
/// <c>Host</c> header. Anything built that way and then e-mailed is attacker-controlled: a
/// request carrying <c>Host: evil.com</c> to /Account/ForgotPassword produces a genuine Nexus
/// e-mail whose reset link points at <c>https://evil.com/Account/ResetPassword?code=...</c>.
/// The victim clicks a link they have every reason to trust and hands the attacker a valid
/// single-use password-reset token — full account takeover, no credentials needed.
/// </para>
/// <para>
/// Restricting <c>AllowedHosts</c> (see appsettings.Production.json) closes the same hole from
/// the other side; both are applied, because host filtering is a deployment-level setting that
/// is easy to loosen back to "*" by accident, and this class makes the link generation itself
/// independent of the request no matter how the host is configured.
/// </para>
/// </remarks>
public class PublicUrlBuilder(IConfiguration configuration, NavigationManager navigationManager)
{
    /// <summary>
    /// Absolute base URL of this deployment, always with a single trailing slash.
    /// Falls back to the current request's base only when PUBLIC_BASE_URL is not configured.
    /// </summary>
    public string BaseUrl
    {
        get
        {
            var configured = configuration["PUBLIC_BASE_URL"];
            if (string.IsNullOrWhiteSpace(configured))
            {
                return navigationManager.BaseUri;
            }

            return configured.TrimEnd('/') + "/";
        }
    }

    /// <summary>
    /// Resolves an application-relative path (e.g. "Account/ResetPassword") against
    /// <see cref="BaseUrl"/>, then appends the supplied query parameters.
    /// </summary>
    public string BuildUrl(string relativePath, IReadOnlyDictionary<string, object?> queryParameters)
    {
        var absolute = new Uri(new Uri(BaseUrl), relativePath.TrimStart('/')).AbsoluteUri;

        // Reuses Blazor's own query-string composition so escaping matches what the rest of the
        // app produces; it accepts an absolute URI here and only rewrites the query portion.
        return navigationManager.GetUriWithQueryParameters(absolute, queryParameters.ToDictionary(p => p.Key, p => p.Value));
    }
}
