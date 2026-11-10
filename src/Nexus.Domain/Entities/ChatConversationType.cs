namespace Nexus.Domain.Entities;

public enum ChatConversationType
{
    // The single conversation every workspace member sees; one per workspace.
    Workspace,
    Direct,
    Group
}
