using Microsoft.Extensions.Options;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the support channel links (#55): built from configuration, hidden when not configured,
/// and carrying only what the person already sees (workspace, plan, version, error code).
/// </summary>
public class SupportLinksTests
{
    private static SupportLinks Links(string? whatsApp, string? email) =>
        new(Options.Create(new SupportOptions { WhatsAppNumber = whatsApp, Email = email }));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    [InlineData("sem dígitos", null)]
    public void NothingConfigured_HidesTheChannel(string? whatsApp, string? email)
    {
        var links = Links(whatsApp, email);

        Assert.False(links.IsConfigured);
        Assert.Null(links.WhatsAppUrl(new SupportContext()));
        Assert.Null(links.MailtoUrl(new SupportContext()));
    }

    [Fact]
    public void WhatsAppUrl_UsesTheBareInternationalNumber()
    {
        var url = Links("+55 (53) 99999-0000", null).WhatsAppUrl(new SupportContext());

        Assert.StartsWith("https://wa.me/5553999990000?text=", url);
    }

    [Fact]
    public void Message_CarriesWorkspacePlanAndVersion_Encoded()
    {
        var url = Links("5553999990000", null).WhatsAppUrl(new SupportContext("Agência Norte", "Pro"))!;
        var text = Uri.UnescapeDataString(url[(url.IndexOf("?text=", StringComparison.Ordinal) + 6)..]);

        Assert.Contains("Workspace: Agência Norte (plano Pro)", text);
        Assert.Contains($"Versão: {AppVersion.Current.Version}", text);
        Assert.DoesNotContain(" ", url); // spaces and accents travel encoded
    }

    [Fact]
    public void ErrorPageMessage_CarriesTheErrorCode_WithoutAWorkspace()
    {
        var message = SupportLinks.Message(new SupportContext(ErrorCode: "00-abc-01"));

        Assert.Contains("Código do erro: 00-abc-01", message);
        Assert.DoesNotContain("Workspace:", message);
    }

    [Fact]
    public void EmailOnly_StillOffersAChannel()
    {
        var links = Links(null, " suporte@nexus.dev ");

        Assert.True(links.IsConfigured);
        Assert.Null(links.WhatsAppUrl(new SupportContext()));
        Assert.Equal("suporte@nexus.dev", links.Email);
        Assert.StartsWith("mailto:suporte@nexus.dev?subject=Suporte%20Nexus&body=", links.MailtoUrl(new SupportContext()));
    }
}
