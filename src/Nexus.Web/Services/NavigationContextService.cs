namespace Nexus.Web.Services;

/// <summary>
/// Tracks which Workspace/Space/List the user is currently inside, so the persistent
/// sidebar (rendered once in MainLayout, not per-page) knows what tree to show and
/// which node to highlight without needing route parameters passed down to it.
/// Pages set this in OnInitializedAsync; MainLayout and WorkspaceSidebar subscribe to
/// Changed to re-render when it does, since mutating a shared service doesn't trigger
/// Blazor re-renders on its own.
/// </summary>
public class NavigationContextService
{
    public Guid? WorkspaceId { get; private set; }
    public string? WorkspaceName { get; private set; }
    public Guid? SpaceId { get; private set; }
    public Guid? ListId { get; private set; }

    public event Action? Changed;

    public void SetWorkspace(Guid workspaceId, string workspaceName)
    {
        WorkspaceId = workspaceId;
        WorkspaceName = workspaceName;
        SpaceId = null;
        ListId = null;
        Changed?.Invoke();
    }

    public void SetSpace(Guid workspaceId, string workspaceName, Guid spaceId)
    {
        WorkspaceId = workspaceId;
        WorkspaceName = workspaceName;
        SpaceId = spaceId;
        ListId = null;
        Changed?.Invoke();
    }

    public void SetList(Guid workspaceId, string workspaceName, Guid spaceId, Guid listId)
    {
        WorkspaceId = workspaceId;
        WorkspaceName = workspaceName;
        SpaceId = spaceId;
        ListId = listId;
        Changed?.Invoke();
    }

    /// <summary>
    /// Re-fires Changed without altering state, for callers that mutate the tree
    /// (create/rename a Space or List) without navigating away — the sidebar needs
    /// a nudge to reload even though WorkspaceId/SpaceId/ListId didn't change.
    /// </summary>
    public void Refresh() => Changed?.Invoke();

    public void Clear()
    {
        if (WorkspaceId is null)
        {
            return;
        }

        WorkspaceId = null;
        WorkspaceName = null;
        SpaceId = null;
        ListId = null;
        Changed?.Invoke();
    }
}
