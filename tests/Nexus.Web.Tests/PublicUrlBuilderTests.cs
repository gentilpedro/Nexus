using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Regression tests for C1 — password-reset / e-mail-confirmation link poisoning via the
/// Host header.
/// </summary>
public class PublicUrlBuilderTests
{
    // Stands in for the request-bound NavigationManager. Its BaseUri is exactly what an attacker
    // controls in the real attack: ASP.NET Core derives it from the incoming Host header, so
    // "Host: evil.com" makes BaseUri "https://evil.com/".
    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager(string baseUri) => Initialize(baseUri, baseUri);
    }

    private static PublicUrlBuilder Build(string? configuredBaseUrl, string requestBaseUri)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = configuredBaseUrl })
            .Build();

        return new PublicUrlBuilder(config, new FakeNavigationManager(requestBaseUri));
    }

    [Fact]
    public void ResetLink_IgnoresAttackerControlledHost()
    {
        // Simulates POST /Account/ForgotPassword with "Host: evil.com".
        var builder = Build("https://usenexus.runasp.net", "https://evil.com/");

        var url = builder.BuildUrl("Account/ResetPassword", new Dictionary<string, object?> { ["code"] = "TOKEN123" });

        Assert.StartsWith("https://usenexus.runasp.net/Account/ResetPassword", url);
        Assert.DoesNotContain("evil.com", url);
        Assert.Contains("code=TOKEN123", url);
    }

    [Fact]
    public void ConfirmEmailLink_IgnoresAttackerControlledHost()
    {
        var builder = Build("https://usenexus.runasp.net", "https://evil.com/");

        var url = builder.BuildUrl(
            "Account/ConfirmEmail",
            new Dictionary<string, object?> { ["userId"] = "u1", ["code"] = "C1" });

        Assert.StartsWith("https://usenexus.runasp.net/Account/ConfirmEmail", url);
        Assert.DoesNotContain("evil.com", url);
    }

    [Fact]
    public void InviteLink_IgnoresAttackerControlledHost()
    {
        var builder = Build("https://usenexus.runasp.net", "https://evil.com/");

        var url = builder.BuildUrl("convite/abc123", new Dictionary<string, object?>());

        Assert.Equal("https://usenexus.runasp.net/convite/abc123", url);
    }

    [Theory]
    [InlineData("https://usenexus.runasp.net")]
    [InlineData("https://usenexus.runasp.net/")]
    public void TrailingSlashInConfig_DoesNotDoubleUp(string configured)
    {
        var builder = Build(configured, "https://evil.com/");

        Assert.Equal("https://usenexus.runasp.net/convite/x", builder.BuildUrl("convite/x", new Dictionary<string, object?>()));
    }

    [Fact]
    public void LeadingSlashOnRelativePath_DoesNotEscapeTheConfiguredBase()
    {
        var builder = Build("https://usenexus.runasp.net", "https://evil.com/");

        Assert.Equal("https://usenexus.runasp.net/convite/x", builder.BuildUrl("/convite/x", new Dictionary<string, object?>()));
    }

    // Local development has no PUBLIC_BASE_URL override in some setups; falling back to the
    // request base keeps `dotnet run` working. Production always configures it.
    [Fact]
    public void WithoutConfiguration_FallsBackToRequestBase()
    {
        var builder = Build(null, "http://localhost:5289/");

        Assert.Equal("http://localhost:5289/convite/x", builder.BuildUrl("convite/x", new Dictionary<string, object?>()));
    }
}
