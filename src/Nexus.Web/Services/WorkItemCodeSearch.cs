using System.Text.RegularExpressions;

namespace Nexus.Web.Services;

// Recognizes a task code typed into search: "TASK-42", "task 42", "#42" or a bare "42".
public static partial class WorkItemCodeSearch
{
    public static int? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = CodePattern().Match(text.Trim());
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0 ? number : null;
    }

    [GeneratedRegex(@"^(?:task[\s-]?|#)?(\d{1,9})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
