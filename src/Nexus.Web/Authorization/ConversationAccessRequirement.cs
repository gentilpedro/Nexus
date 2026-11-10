using Microsoft.AspNetCore.Authorization;

namespace Nexus.Web.Authorization;

/// <summary>
/// The user may read and post in the conversation given as the resource (its id). Satisfied by
/// <see cref="ConversationAuthorizationHandler"/>.
/// </summary>
public class ConversationAccessRequirement : IAuthorizationRequirement
{
    public static readonly ConversationAccessRequirement Instance = new();
}
