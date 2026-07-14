using Nexus.Domain.Entities;

namespace Nexus.Web.Extensions;

public static class CustomFieldTypeLabels
{
    public static string ToDisplayLabel(this CustomFieldType type) => type switch
    {
        CustomFieldType.Text => "Texto",
        CustomFieldType.Number => "Número",
        CustomFieldType.Date => "Data",
        CustomFieldType.Select => "Seleção",
        _ => type.ToString()
    };
}
