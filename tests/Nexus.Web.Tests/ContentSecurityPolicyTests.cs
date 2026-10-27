using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// CSP (#62): o import map do Blazor roda com o nonce da resposta, o beacon do Cloudflare é
/// permitido, e script-src continua sem 'unsafe-inline'.
/// </summary>
public class ContentSecurityPolicyTests
{
    private static Dictionary<string, string> Directives(string policy) =>
        policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToDictionary(d => d.Split(' ')[0], d => d);

    [Fact]
    public void ScriptSrc_AllowsTheNonceAndTheCloudflareBeacon_ButNoInlineScript()
    {
        var policy = Directives(ContentSecurityPolicy.Build("abc123"));

        Assert.Equal("script-src 'self' 'nonce-abc123' https://static.cloudflareinsights.com", policy["script-src"]);
        Assert.DoesNotContain("unsafe-inline", policy["script-src"]);
        Assert.DoesNotContain("unsafe-eval", policy["script-src"]);
        Assert.Equal("connect-src 'self' ws: wss: https://cloudflareinsights.com", policy["connect-src"]);
        Assert.Equal("object-src 'none'", policy["object-src"]);
        Assert.Equal("frame-ancestors 'none'", policy["frame-ancestors"]);
    }

    [Fact]
    public void Nonce_IsFreshAndUnguessable()
    {
        var nonces = Enumerable.Range(0, 50).Select(_ => ContentSecurityPolicy.NewNonce()).ToList();

        Assert.Equal(50, nonces.Distinct().Count());
        Assert.All(nonces, n => Assert.Equal(16, Convert.FromBase64String(n).Length));
    }

    /// <summary>
    /// App.razor passes the nonce as an attribute to &lt;ImportMap /&gt;. Without it on the
    /// rendered &lt;script type="importmap"&gt;, the browser blocks the map and logs a CSP error.
    /// </summary>
    [Fact]
    public async Task ImportMap_RendersTheNonceOnItsScriptTag()
    {
        var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ImportMap>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ImportMap.ImportMapDefinition)] = new ImportMapDefinition(
                    new Dictionary<string, string> { ["/js/chat-interop.js"] = "/js/chat-interop.abc.js" }, null, null),
                ["nonce"] = "abc123",
            }));
            return output.ToHtmlString();
        });

        Assert.Contains("type=\"importmap\"", html);
        Assert.Contains("nonce=\"abc123\"", html);
    }
}
