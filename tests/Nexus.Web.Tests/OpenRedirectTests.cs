using Nexus.Web.Components.Account;

namespace Nexus.Web.Tests;

/// <summary>
/// Regression tests for H5 — open redirect via IdentityRedirectManager.
/// Reachable through /Account/Login?ReturnUrl=... , which is passed straight to RedirectTo
/// after a successful sign-in.
/// </summary>
public class OpenRedirectTests
{
    // The actual vulnerability: these are well-formed *relative* URIs, so the previous
    // Uri.IsWellFormedUriString(uri, UriKind.Relative) guard let them through untouched, and the
    // browser resolved them against the current scheme -> https://evil.com/.
    [Theory]
    [InlineData("//evil.com")]
    [InlineData("///evil.com")]
    [InlineData("//evil.com/path?x=1")]
    [InlineData(@"/\evil.com")]
    [InlineData(@"\\evil.com")]
    [InlineData(@"\/evil.com")]
    [InlineData(@"\evil.com")]
    public void SchemeRelativeUrls_AreRejected(string hostile)
    {
        Assert.False(IdentityRedirectManager.IsLocalUrl(hostile));
    }

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com/x")]
    [InlineData("javascript:alert(1)")]
    public void AbsoluteAndSchemeCarryingUrls_AreRejected(string hostile)
    {
        Assert.False(IdentityRedirectManager.IsLocalUrl(hostile));
    }

    // These are the shapes the application itself passes to RedirectTo. If the fix rejected any
    // of them the app would bounce users to the root instead of the intended page, so they are
    // just as important to pin down as the hostile inputs above.
    [Theory]
    [InlineData("")]
    [InlineData("Account/Login")]
    [InlineData("Account/ForgotPasswordConfirmation")]
    [InlineData("Account/Manage/Email")]
    [InlineData("/workspaces")]
    [InlineData("/workspaces/1a2b?tab=members")]
    [InlineData("/lists/abc/board")]
    public void LegitimateApplicationUrls_AreAccepted(string benign)
    {
        Assert.True(IdentityRedirectManager.IsLocalUrl(benign));
    }

    [Fact]
    public void NullUrl_IsRejected()
    {
        Assert.False(IdentityRedirectManager.IsLocalUrl(null));
    }

    /// <summary>
    /// Documents the exact defect being fixed, so the regression is pinned rather than merely
    /// asserted: the guard that used to protect this code path (Uri.IsWellFormedUriString with
    /// UriKind.Relative) classifies "//evil.com" as a valid relative URI and therefore passed it
    /// through unchanged — and resolving it against the app's base yields an off-origin URL.
    /// </summary>
    [Fact]
    public void PreviousGuard_WouldHaveAllowedTheOffOriginRedirect()
    {
        const string hostile = "//evil.com";

        // What the old code checked.
        Assert.True(Uri.IsWellFormedUriString(hostile, UriKind.Relative));

        // Where that actually sent the user.
        Assert.Equal("https://evil.com/", new Uri(new Uri("https://usenexus.runasp.net/"), hostile).ToString());

        // What the new guard does with the same input.
        Assert.False(IdentityRedirectManager.IsLocalUrl(hostile));
    }
}
