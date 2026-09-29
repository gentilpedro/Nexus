using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Authorization;

namespace Nexus.Web.Services;

/// <summary>
/// What every view of a list (List, Board, Backlog, Calendar, Gantt) needs besides its own
/// items: the list with its breadcrumb, whether the caller is an admin, and the list's
/// configuration (statuses, members, custom fields, transitions, labels).
/// </summary>
public sealed record ListViewShell(
    TaskList TaskList,
    bool CanManage,
    List<TaskStatusDefinition> Statuses,
    List<(string UserId, string DisplayName)> AssigneeOptions,
    List<CustomFieldDefinition> CustomFieldDefinitions,
    List<StatusTransition> StatusTransitions,
    List<Label> Labels)
{
    public Guid WorkspaceId => TaskList.Space.WorkspaceId;
}

/// <summary>
/// Loads a list view in three round trips instead of a dozen.
/// </summary>
/// <remarks>
/// Each view is its own page, so switching between them rebuilds everything from scratch. The
/// views used to await every query one after the other — list, workspace id, the Member check,
/// the Admin check, then six configuration queries, then the items, then the epics again —
/// so a tab switch paid ~12 sequential database round trips. Every query here opens its own
/// short-lived context from the factory, so the independent ones can safely run concurrently:
/// <list type="number">
/// <item>the list (with space and workspace for the breadcrumb, which also yields the workspace id);</item>
/// <item>the Member and Admin checks, together;</item>
/// <item>the configuration queries and the view's own data (<c>loadViewData</c>), together.</item>
/// </list>
/// The view's data only starts after the Member check passes, as before.
/// </remarks>
public class ListViewLoader(
    IDbContextFactory<AppDbContext> dbFactory,
    WorkItemQueryService queries,
    IAuthorizationService authorizationService)
{
    /// <summary>
    /// Returns null when the list does not exist or the user is not a member of its workspace —
    /// the views render both as "not found".
    /// </summary>
    public async Task<ListViewShell?> LoadAsync(Guid listId, ClaimsPrincipal user, Func<Task> loadViewData)
    {
        TaskList? taskList;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            taskList = await db.TaskLists
                .AsNoTracking()
                .Include(l => l.Space)
                .ThenInclude(s => s.Workspace)
                .FirstOrDefaultAsync(l => l.Id == listId);
        }

        if (taskList is null)
        {
            return null;
        }

        var workspaceId = taskList.Space.WorkspaceId;
        var memberCheck = authorizationService.AuthorizeAsync(
            user, workspaceId, new WorkspaceAccessRequirement(WorkspaceRole.Member));
        var adminCheck = authorizationService.AuthorizeAsync(
            user, workspaceId, new WorkspaceAccessRequirement(WorkspaceRole.Admin));
        await Task.WhenAll(memberCheck, adminCheck);

        if (!memberCheck.Result.Succeeded)
        {
            return null;
        }

        var statuses = queries.GetStatusesForListAsync(listId);
        var assigneeOptions = queries.GetWorkspaceMemberOptionsAsync(workspaceId);
        var customFields = queries.GetCustomFieldDefinitionsForListAsync(listId);
        var transitions = queries.GetStatusTransitionsForListAsync(listId);
        var labels = queries.GetLabelsForListAsync(listId);
        await Task.WhenAll(statuses, assigneeOptions, customFields, transitions, labels, loadViewData());

        return new ListViewShell(
            taskList,
            adminCheck.Result.Succeeded,
            statuses.Result,
            assigneeOptions.Result,
            customFields.Result,
            transitions.Result,
            labels.Result);
    }
}
