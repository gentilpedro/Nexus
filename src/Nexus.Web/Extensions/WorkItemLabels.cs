using Nexus.Domain.Entities;

namespace Nexus.Web.Extensions;

// Portuguese display labels for enums that otherwise render their raw C# name
// (e.g. "Highest", "Story") — used anywhere a WorkItemType/WorkItemPriority is
// shown as text, so it isn't just the badges that are translated while the
// TaskDetailPanel's <select> options are left in English.
public static class WorkItemLabels
{
    public static string ToDisplayLabel(this WorkItemPriority priority) => priority switch
    {
        WorkItemPriority.Lowest => "Baixíssima",
        WorkItemPriority.Low => "Baixa",
        WorkItemPriority.Medium => "Média",
        WorkItemPriority.High => "Alta",
        WorkItemPriority.Highest => "Altíssima",
        _ => priority.ToString()
    };

    public static string ToDisplayLabel(this WorkItemType type) => type switch
    {
        WorkItemType.Task => "Tarefa",
        WorkItemType.Bug => "Bug",
        WorkItemType.Story => "História",
        WorkItemType.Epic => "Epic",
        _ => type.ToString()
    };
}
