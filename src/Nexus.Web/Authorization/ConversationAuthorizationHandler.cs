using System.Security.Claims;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Web.Authorization;

/// <summary>
/// Access to a conversation needs two things: current membership in its workspace, and — for a
/// direct or group conversation — a membership row in the conversation itself. The workspace
/// check is what makes removing someone from the workspace also cut them off from every private
/// conversation they were in, without touching those rows.
/// </summary>
public class ConversationAuthorizationHandler(IDbContextFactory<AppDbContext> dbFactory)
    : AuthorizationHandler<ConversationAccessRequirement, Guid>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ConversationAccessRequirement requirement,
        Guid conversationId)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync();

        var conversation = await db.ChatConversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new { c.WorkspaceId, c.Type })
            .FirstOrDefaultAsync();

        if (conversation is null)
        {
            return;
        }

        var isWorkspaceMember = await db.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == conversation.WorkspaceId && m.UserId == userId);

        if (!isWorkspaceMember)
        {
            return;
        }

        if (conversation.Type == ChatConversationType.Workspace
            || await db.ChatConversationMembers.AnyAsync(m => m.ConversationId == conversationId && m.UserId == userId))
        {
            context.Succeed(requirement);
        }
    }
}
