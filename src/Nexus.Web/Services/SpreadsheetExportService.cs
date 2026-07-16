using ClosedXML.Excel;

namespace Nexus.Web.Services;

public static class SpreadsheetExportService
{
    public static byte[] BuildXlsx(string title, List<List<string>> grid)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SanitizeSheetName(title));

        for (var r = 0; r < grid.Count; r++)
        {
            for (var c = 0; c < grid[r].Count; c++)
            {
                sheet.Cell(r + 1, c + 1).Value = grid[r][c];
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // Excel worksheet names: max 31 chars, and : \ / ? * [ ] are not allowed.
    private static string SanitizeSheetName(string title)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var clean = new string(title.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "Planilha";
        }

        return clean.Length > 31 ? clean[..31] : clean;
    }
}
