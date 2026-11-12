using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using QRCoder;

namespace Nexus.Web.Services;

/// <summary>
/// The otpauth:// link and its QR code for setting up an authenticator app (#128).
/// </summary>
/// <remarks>
/// The QR code is drawn on the server as inline SVG. The enable page renders as static SSR and the
/// CSP forbids inline scripts, so a client-side QR library would need a separate module just for
/// this; an SVG needs nothing and stays sharp at any zoom.
/// </remarks>
public static class AuthenticatorQrCode
{
    /// <summary>Name the authenticator app shows above the code.</summary>
    public const string Issuer = "Nexus";

    /// <summary>
    /// Key URI in the format authenticator apps read:
    /// <c>otpauth://totp/Issuer:account?secret=...&amp;issuer=Issuer&amp;digits=6</c>.
    /// </summary>
    public static string BuildUri(string account, string unformattedKey, UrlEncoder? encoder = null)
    {
        encoder ??= UrlEncoder.Default;
        var issuer = encoder.Encode(Issuer);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{issuer}:{encoder.Encode(account)}?secret={unformattedKey}&issuer={issuer}&digits=6");
    }

    /// <summary>
    /// SVG markup of the QR code for <paramref name="content"/>, one unit per module, quiet zone
    /// included. It scales with CSS; it carries no text from <paramref name="content"/>.
    /// </summary>
    public static string RenderSvg(string content)
    {
        using var generator = new QRCodeGenerator();
        // Level M: the payload is short, so it keeps the code small while tolerating a glare or a
        // smudge on the screen.
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;

        var path = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            var row = matrix[y];
            for (var x = 0; x < size; x++)
            {
                if (row[x])
                {
                    path.Append(CultureInfo.InvariantCulture, $"M{x} {y}h1v1h-1z");
                }
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" shape-rendering="crispEdges" role="img" aria-label="QR code para configurar o aplicativo autenticador"><rect width="{size}" height="{size}" fill="#fff"/><path fill="#000" d="{path}"/></svg>""");
    }
}
