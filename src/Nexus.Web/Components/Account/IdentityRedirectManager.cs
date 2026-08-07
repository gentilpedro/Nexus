using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nexus.Domain.Entities;

namespace Nexus.Web.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Prevent open redirects.
        //
        // Uri.IsWellFormedUriString(_, UriKind.Relative) alone is NOT enough: a protocol-relative
        // URL like "//evil.com" (or "///evil.com") is a perfectly well-formed *relative* URI, so it
        // passed this check untouched and NavigateTo resolved it against the base URI as
        // https://evil.com/ — an open redirect an attacker reaches via
        // /Account/Login?ReturnUrl=//evil.com, which is a credible phishing springboard because the
        // link the victim clicks is genuinely on the Nexus domain.
        //
        // IsLocalUrl below keeps accepting the relative forms this app actually passes in
        // ("Account/Login", "/workspaces", ""), while rejecting the scheme-relative ones. Callers
        // also pass genuine absolute URLs on our own origin (RedirectToCurrentPage), so those are
        // still folded back to a base-relative path; anything else falls back to the app root
        // instead of letting ToBaseRelativePath throw on an off-origin URL.
        if (!IsLocalUrl(uri))
        {
            uri = Uri.TryCreate(uri, UriKind.Absolute, out var absolute)
                && string.Equals(absolute.GetLeftPart(UriPartial.Authority), new Uri(navigationManager.BaseUri).GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
                    ? navigationManager.ToBaseRelativePath(uri)
                    : "";
        }

        navigationManager.NavigateTo(uri);
    }

    /// <summary>
    /// True for URLs that unambiguously stay on this host — the relative forms this app uses
    /// ("", "Account/Login", "/workspaces?tab=1") — and false for anything that could leave it.
    /// </summary>
    /// <remarks>
    /// The critical rejections are the scheme-relative forms "//host" and "/\host": both are
    /// well-formed *relative* URIs as far as <see cref="Uri.IsWellFormedUriString"/> is concerned,
    /// yet a browser resolves them against the current scheme and lands on another origin.
    /// </remarks>
    internal static bool IsLocalUrl(string? uri)
    {
        if (uri is null)
        {
            return false;
        }

        if (uri.Length == 0)
        {
            return true;
        }

        // Scheme-relative: "//evil.com", "/\evil.com" (browsers normalize the backslash to '/').
        if (uri.Length >= 2 && (uri[0] == '/' || uri[0] == '\\') && (uri[1] == '/' || uri[1] == '\\'))
        {
            return false;
        }

        // A leading backslash alone is also treated as a path separator by some browsers.
        if (uri[0] == '\\')
        {
            return false;
        }

        // Anything carrying a scheme ("https://…", "javascript:…") is not relative.
        return Uri.IsWellFormedUriString(uri, UriKind.Relative);
    }

    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);

    public void RedirectToInvalidUser(UserManager<ApplicationUser> userManager, HttpContext context)
        => RedirectToWithStatus("Account/InvalidUser", $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.", context);
}
