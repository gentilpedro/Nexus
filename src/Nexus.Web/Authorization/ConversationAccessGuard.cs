using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;

namespace Nexus.Web.Authorization;

/// <summary>
/// Conversation counterpart of <see cref="WorkspaceAccessGuard"/>: re-checks access at the moment
/// of a write, since the page's own check happened when it first rendered and the circuit may have
/// been open for hours since. See that class for the full reasoning.
/// </summary>
public class ConversationAccessGuard(IAuthorizationService authorizationService)
{
    public async Task<bool> HasAccessAsync(Task<AuthenticationState>? authStateTask, Guid conversationId)
    {
        if (authStateTask is null)
        {
            return false;
        }

        var authState = await authStateTask;
        var result = await authorizationService.AuthorizeAsync(
            authState.User, conversationId, ConversationAccessRequirement.Instance);

        return result.Succeeded;
    }
}
