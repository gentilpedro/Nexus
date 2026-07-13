using System.Security.Claims;
using Nexus.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Web.Authorization;

public class WorkspaceAuthorizationHandler(AppDbContext db)
    : AuthorizationHandler<WorkspaceAccessRequirement, Guid>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkspaceAccessRequirement requirement,
        Guid workspaceId)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return;
        }

        var member = await db.WorkspaceMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId);

        // Lower enum value = higher privilege (Owner=0, Admin=1, Member=2), so a member's
        // role satisfies the requirement when it is at or above the minimum privilege level.
        if (member is not null && (int)member.Role <= (int)requirement.MinimumRole)
        {
            context.Succeed(requirement);
        }
    }
}
