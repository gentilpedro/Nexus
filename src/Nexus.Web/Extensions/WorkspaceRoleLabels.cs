using Nexus.Domain.Entities;

namespace Nexus.Web.Extensions;

public static class WorkspaceRoleLabels
{
    public static string ToDisplayLabel(this WorkspaceRole role) => role switch
    {
        WorkspaceRole.Owner => "Dono",
        WorkspaceRole.Admin => "Admin",
        WorkspaceRole.Member => "Membro",
        _ => role.ToString()
    };
}
