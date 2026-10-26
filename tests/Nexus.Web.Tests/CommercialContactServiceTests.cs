using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Covers the Business-plan contact form (#56): the lead is stored before the e-mail, bots caught
/// by the honeypot leave no trace, and the e-mail escapes what the visitor typed.
/// </summary>
public class CommercialContactServiceTests
{
    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("A test must not make HTTP calls.");
    }

    private sealed class FakeMailer(bool succeeds = true)
        : BrevoMailer(Options.Create(new BrevoOptions()), new UnusedHttpClientFactory(), NullLogger<BrevoMailer>.Instance)
    {
        public List<(string To, string Subject, string Html, string? Text)> Sent { get; } = [];

        public override Task<bool> SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null)
        {
            Sent.Add((toEmail, subject, htmlBody, textBody));
            return Task.FromResult(succeeds);
        }
    }

    private static CommercialContactRequest Request(string? website = null, string? message = "Precisamos de contrato anual.") =>
        new(" Ana Souza ", "Josapar", "ana@josapar.com.br", "51 a 200 pessoas", message, website);

    private static CommercialContactService Service(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> factory,
        FakeMailer mailer, string? inbox = "comercialnexus@aamora.com.br", string? support = null) =>
        new(factory, mailer, Options.Create(new CommercialOptions { Email = inbox }), Options.Create(new SupportOptions { Email = support }));

    [Fact]
    public async Task ValidRequest_IsStored_AndEmailedToTheCommercialInbox()
    {
        var factory = TestDb.CreateFactory();
        var mailer = new FakeMailer();
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory, mailer).SubmitAsync(Request(), ct);

        Assert.Equal(CommercialContactResult.Received, result);
        await using var db = factory.CreateDbContext();
        var stored = await db.CommercialContacts.SingleAsync(ct);
        Assert.Equal("Ana Souza", stored.Name);
        Assert.Equal("ana@josapar.com.br", stored.Email);

        var mail = Assert.Single(mailer.Sent);
        Assert.Equal("comercialnexus@aamora.com.br", mail.To);
        Assert.Contains("Josapar", mail.Subject);
        Assert.Contains("ana@josapar.com.br", mail.Text);
        Assert.Contains("Precisamos de contrato anual.", mail.Text);
    }

    [Fact]
    public async Task HoneypotFilled_StoresAndSendsNothing()
    {
        var factory = TestDb.CreateFactory();
        var mailer = new FakeMailer();
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory, mailer).SubmitAsync(Request(website: "http://spam.example"), ct);

        Assert.Equal(CommercialContactResult.Ignored, result);
        Assert.Empty(mailer.Sent);
        await using var db = factory.CreateDbContext();
        Assert.False(await db.CommercialContacts.AnyAsync(ct));
    }

    // A failed delivery must not lose the lead: it is in the database either way.
    [Theory]
    [InlineData(false, "comercialnexus@aamora.com.br")]
    [InlineData(true, null)]
    public async Task NoDelivery_StillKeepsTheLead(bool mailerSucceeds, string? inbox)
    {
        var factory = TestDb.CreateFactory();
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory, new FakeMailer(mailerSucceeds), inbox).SubmitAsync(Request(), ct);

        Assert.Equal(CommercialContactResult.ReceivedWithoutEmail, result);
        await using var db = factory.CreateDbContext();
        Assert.True(await db.CommercialContacts.AnyAsync(ct));
    }

    [Fact]
    public async Task WithoutACommercialInbox_FallsBackToSupport()
    {
        var factory = TestDb.CreateFactory();
        var mailer = new FakeMailer();

        await Service(factory, mailer, inbox: null, support: "suportenexus@aamora.com.br").SubmitAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal("suportenexus@aamora.com.br", Assert.Single(mailer.Sent).To);
    }

    // What the visitor types ends up in our inbox: it has to arrive as text, not as markup.
    [Fact]
    public async Task VisitorInput_IsEscapedInTheEmail()
    {
        var factory = TestDb.CreateFactory();
        var mailer = new FakeMailer();

        await Service(factory, mailer).SubmitAsync(Request(message: "<script>alert(1)</script>"), TestContext.Current.CancellationToken);

        var mail = Assert.Single(mailer.Sent);
        Assert.DoesNotContain("<script>", mail.Html);
        Assert.Contains("&lt;script&gt;", mail.Html);
    }
}
