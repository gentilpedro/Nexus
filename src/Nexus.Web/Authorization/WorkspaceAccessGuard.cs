using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Nexus.Domain.Entities;

namespace Nexus.Web.Authorization;

/// <summary>
/// Re-checks a user's current role in a workspace at the moment a write happens, rather than
/// relying on the check performed when the page was first rendered.
/// </summary>
/// <remarks>
/// <para>
/// Blazor Server pages here authorize once, in <c>OnParametersSetAsync</c>. That is correct for
/// deciding what to render, but a circuit stays alive for as long as the browser tab is open and
/// the component is never re-initialized while the user simply interacts with the page. So a
/// member who is demoted or removed from a workspace keeps every capability the page granted
/// them at load time — they can still post to the chat, still edit docs, still change work items
/// — until they navigate somewhere that re-runs the check. The auth cookie lasts 8 hours with
/// sliding expiration, and
/// <see cref="Components.Account.IdentityRevalidatingAuthenticationStateProvider"/> only
/// revalidates the *identity* (security stamp) every 30 minutes; it knows nothing about
/// workspace membership.
/// </para>
/// <para>
/// <see cref="WorkspaceAuthorizationHandler"/> already queries the database on every call and
/// caches nothing, so calling it again immediately before a write is cheap and always reflects
/// the current role.
/// </para>
/// </remarks>
public class WorkspaceAccessGuard(IAuthorizationService authorizationService)
{
    /// <summary>
    /// Returns true when the user backing <paramref name="authStateTask"/> currently holds at
    /// least <paramref name="minimumRole"/> in <paramref name="workspaceId"/>.
    /// </summary>
    public async Task<bool> HasAccessAsync(
        Task<AuthenticationState>? authStateTask,
        Guid workspaceId,
        WorkspaceRole minimumRole)
    {
        if (authStateTask is null)
        {
            return false;
        }

        var authState = await authStateTask;
        var result = await authorizationService.AuthorizeAsync(
            authState.User, workspaceId, new WorkspaceAccessRequirement(minimumRole));

        return result.Succeeded;
    }
}
