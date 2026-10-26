using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Web.Components.Pages;

namespace Nexus.Web.Tests;

/// <summary>
/// Cobre os textos de planos da landing (#51).
/// </summary>
/// <remarks>
/// A página pública é a primeira promessa que o visitante lê. Enquanto não existem limites,
/// cobrança, cancelamento nem canal de suporte, ela não pode prometer nada disso como se já
/// funcionasse — o que depende das issues de planos aparece como "em breve".
/// </remarks>
public class LandingPricingTests
{
    [Theory]
    [InlineData("Planilhas colaborativas")]
    [InlineData("cancele quando quiser")]
    [InlineData("Começar com o Pro")]
    [InlineData("Plano gratuito para até 5 pessoas")]
    public async Task Landing_DoesNotPromiseWhatTheProductLacks(string promise)
    {
        var html = await RenderLandingAsync();

        Assert.DoesNotContain(promise, html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Landing_SaysBillingIsNotActiveYet()
    {
        var html = await RenderLandingAsync();

        Assert.Contains("grátis e sem limites", html);
        Assert.Contains("A cobrança ainda não está ativa", html);
    }

    [Fact]
    public async Task PaidPlans_AreFlaggedAsComingSoon()
    {
        var html = await RenderLandingAsync();

        Assert.Equal(2, Regex.Matches(html, """<span class="pricing-flag">Em breve</span>""").Count);
        Assert.Contains("""Suporte prioritário <span class="pricing-soon">em breve</span>""", html);
    }

    /// <summary>
    /// O botão do Business não pode levar para "Criar conta grátis": quem procura contrato e nota
    /// fiscal não está pedindo uma conta. Ele leva para o formulário de contato comercial (#56).
    /// </summary>
    [Fact]
    public async Task BusinessCard_LinksToTheCommercialContactForm()
    {
        var html = await RenderLandingAsync();
        var business = html[html.IndexOf("<h3>Business</h3>", StringComparison.Ordinal)..];
        business = business[..business.IndexOf("</div>", StringComparison.Ordinal)];

        Assert.DoesNotContain("Account/Register", business);
        Assert.Contains("""href="contato-comercial""", business);
    }

    private static async Task<string> RenderLandingAsync()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<Landing>()).ToHtmlString());
    }
}
