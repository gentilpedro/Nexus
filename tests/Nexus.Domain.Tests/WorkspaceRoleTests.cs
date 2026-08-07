using Nexus.Domain.Entities;

namespace Nexus.Domain.Tests;

/// <summary>
/// WorkspaceAuthorizationHandler compares roles numerically —
/// <c>(int)member.Role &lt;= (int)requirement.MinimumRole</c> — so the *declaration order* of this
/// enum is a security control, not a cosmetic detail. Reordering the members (or inserting a new
/// role in the middle) silently changes who can do what across the entire application, with no
/// compiler error and nothing else to catch it.
/// </summary>
public class WorkspaceRoleTests
{
    [Fact]
    public void OrdinalsEncodePrivilege_LowestValueIsMostPrivileged()
    {
        Assert.Equal(0, (int)WorkspaceRole.Owner);
        Assert.Equal(1, (int)WorkspaceRole.Admin);
        Assert.Equal(2, (int)WorkspaceRole.Member);
    }

    [Fact]
    public void OwnerOutranksAdmin_WhichOutranksMember()
    {
        Assert.True((int)WorkspaceRole.Owner < (int)WorkspaceRole.Admin);
        Assert.True((int)WorkspaceRole.Admin < (int)WorkspaceRole.Member);
    }

    [Fact]
    public void NoUnexpectedRolesExist()
    {
        Assert.Equal(3, Enum.GetValues<WorkspaceRole>().Length);
    }

    // StatusCategory.Done is used to exclude finished work from due-date notifications and
    // burndown remaining-work counts; its identity matters the same way.
    [Fact]
    public void StatusCategory_HasTheExpectedMembers()
    {
        Assert.Equal(
            [StatusCategory.ToDo, StatusCategory.InProgress, StatusCategory.Done],
            Enum.GetValues<StatusCategory>());
    }
}
