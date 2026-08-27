using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Pins the wire contract with Brevo's HTTP API.
/// </summary>
/// <remarks>
/// This path had no test while it talked SMTP, and it failed silently in production for weeks:
/// the host blocks outbound SMTP, every send returned false, and callers are designed to carry on
/// when a send fails — so nothing surfaced. The shape of the request is now pinned here, and the
/// "a rejection is not a crash" contract with it.
/// </remarks>
public class BrevoMailerTests
{
    private sealed class StubHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static BrevoMailer CreateMailer(StubHandler handler, string apiKey = "xkeysib-test", string sender = "nexus@example.com")
        => new(
            Options.Create(new BrevoOptions { ApiKey = apiKey, SenderEmail = sender, SenderName = "Nexus" }),
            new StubFactory(handler),
            NullLogger<BrevoMailer>.Instance);

    [Fact]
    public async Task Sends_the_payload_Brevo_expects()
    {
        var handler = new StubHandler(HttpStatusCode.Created, """{"messageId":"<abc@brevo>"}""");

        var sent = await CreateMailer(handler).SendAsync("alguem@example.com", "Assunto", "<p>Corpo</p>");

        Assert.True(sent);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("xkeysib-test", handler.LastRequest.Headers.GetValues("api-key").Single());

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("nexus@example.com", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("Nexus", root.GetProperty("sender").GetProperty("name").GetString());
        Assert.Equal("alguem@example.com", root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("Assunto", root.GetProperty("subject").GetString());
        Assert.Equal("<p>Corpo</p>", root.GetProperty("htmlContent").GetString());
    }

    [Fact]
    public async Task Returns_false_when_Brevo_rejects_the_send()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"code":"invalid_parameter","message":"sender not valid"}""");

        var sent = await CreateMailer(handler).SendAsync("alguem@example.com", "Assunto", "<p>Corpo</p>");

        Assert.False(sent);
    }

    [Theory]
    [InlineData("", "nexus@example.com")]
    [InlineData("xkeysib-test", "")]
    public async Task Does_not_call_Brevo_when_it_is_not_configured(string apiKey, string sender)
    {
        var handler = new StubHandler(HttpStatusCode.Created);

        var sent = await CreateMailer(handler, apiKey, sender).SendAsync("alguem@example.com", "Assunto", "<p>Corpo</p>");

        Assert.False(sent);
        Assert.Equal(0, handler.Calls);
    }
}
