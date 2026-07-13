using Nexus.Domain.Entities;
using Microsoft.AspNetCore.Authorization;

namespace Nexus.Web.Authorization;

public class WorkspaceAccessRequirement(WorkspaceRole minimumRole) : IAuthorizationRequirement
{
    public WorkspaceRole MinimumRole { get; } = minimumRole;
}
