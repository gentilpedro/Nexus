using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Nexus.Web.Services;
using ZXing;
using ZXing.Common;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the authenticator setup QR code (#128): the otpauth:// link names the account as Nexus,
/// and the SVG drawn on the server decodes back to that exact link in an independent reader.
/// </summary>
public partial class AuthenticatorQrCodeTests
{
    private const string Key = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public void BuildUri_UsesNexusAsTheIssuer()
    {
        var uri = AuthenticatorQrCode.BuildUri("ana@empresa.com.br", Key, UrlEncoder.Default);

        Assert.Equal(
            $"otpauth://totp/Nexus:ana@empresa.com.br?secret={Key}&issuer=Nexus&digits=6",
            uri);
    }

    [Fact]
    public void BuildUri_EncodesCharactersThatWouldBreakTheLink()
    {
        var uri = AuthenticatorQrCode.BuildUri("a&b?c=d@x.com", Key, UrlEncoder.Default);

        Assert.StartsWith("otpauth://totp/Nexus:a%26b%3Fc%3Dd@x.com?", uri);
        Assert.EndsWith($"?secret={Key}&issuer=Nexus&digits=6", uri);
    }

    [Theory]
    [InlineData("ana@empresa.com.br")]
    [InlineData("joão.silva+trabalho@exemplo.com.br")]
    public void RenderSvg_DecodesBackToTheSameLink(string account)
    {
        var uri = AuthenticatorQrCode.BuildUri(account, Key, UrlEncoder.Default);

        var decoded = Decode(AuthenticatorQrCode.RenderSvg(uri));

        Assert.Equal(uri, decoded);
    }

    [Fact]
    public void RenderSvg_NeverEmbedsTheContentAsText()
    {
        const string hostile = "\"><script>alert(1)</script>";

        var svg = AuthenticatorQrCode.RenderSvg(hostile);

        Assert.DoesNotContain("script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(SvgShape(), svg);
    }

    // Rasterizes the SVG's own path (not QRCoder's matrix) so the test reads exactly what the
    // browser draws.
    private static string? Decode(string svg)
    {
        var shape = SvgShape().Match(svg);
        Assert.True(shape.Success, "SVG fora do formato esperado.");

        const int scale = 4;
        var modules = int.Parse(shape.Groups["size"].Value);
        var dark = new bool[modules, modules];
        foreach (Match module in ModuleCommand().Matches(shape.Groups["path"].Value))
        {
            dark[int.Parse(module.Groups["y"].Value), int.Parse(module.Groups["x"].Value)] = true;
        }

        var width = modules * scale;
        var pixels = new byte[width * width];
        for (var y = 0; y < width; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = dark[y / scale, x / scale] ? (byte)0 : (byte)255;
            }
        }

        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true },
        };
        return reader.Decode(new RGBLuminanceSource(pixels, width, width, RGBLuminanceSource.BitmapFormat.Gray8))?.Text;
    }

    [GeneratedRegex("""^<svg xmlns="http://www\.w3\.org/2000/svg" viewBox="0 0 (?<size>\d+) \k<size>" [^>]*><rect [^>]*/><path fill="#000" d="(?<path>[Mhvz\d \-]*)"/></svg>$""")]
    private static partial Regex SvgShape();

    [GeneratedRegex(@"M(?<x>\d+) (?<y>\d+)h1v1h-1z")]
    private static partial Regex ModuleCommand();
}
