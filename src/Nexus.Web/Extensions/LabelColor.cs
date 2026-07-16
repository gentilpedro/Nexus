using System.Globalization;

namespace Nexus.Web.Extensions;

// Picks readable chip text color for an arbitrary label background — avoids having to
// hand-pick a palette where every color happens to read well with white text.
public static class LabelColor
{
    public static string TextColorFor(string hex)
    {
        if (TryParseRgb(hex, out var r, out var g, out var b))
        {
            var yiq = ((r * 299) + (g * 587) + (b * 114)) / 1000.0;
            return yiq >= 128 ? "#1A1A1A" : "#FFFFFF";
        }

        return "#FFFFFF";
    }

    private static bool TryParseRgb(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        var value = hex.TrimStart('#');
        if (value.Length != 6)
        {
            return false;
        }

        return int.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && int.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && int.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }
}
