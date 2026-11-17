using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services.Webhooks;

namespace Nexus.Web.Tests;

public class WebhookSenderTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    // Responde o que o teste mandar e guarda cada requisição recebida, com o corpo já lido.
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Received { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Received.Add((request, body));
            return respond(request);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record Fixture(IDbContextFactory<AppDbContext> Factory, WebhookService Webhooks, FakeTimeProvider Clock, FakeHandler Handler, WebhookSender Sender, Guid WorkspaceId, WorkspaceWebhook Hook, string Secret);

    private static async Task<Fixture> SetupAsync(Func<HttpRequestMessage, HttpResponseMessage> respond, WebhookEvents events = WebhookEvents.All)
    {
        var factory = TestDb.CreateFactory();
        var clock = new FakeTimeProvider(Start);
        var workspaceId = await WebhookServiceTests.SeedWorkspaceAsync(factory);
        var webhooks = WebhookServiceTests.Service(factory, clock);
        var created = await webhooks.CreateAsync(workspaceId, "ERP", "https://erp.acme.com.br/nexus", events, null, TestContext.Current.CancellationToken);
        var handler = new FakeHandler(respond);
        var sender = new WebhookSender(factory, new FakeFactory(handler), webhooks, NullLogger<WebhookSender>.Instance, clock);
        return new Fixture(factory, webhooks, clock, handler, sender, workspaceId, created.Webhook!, created.Secret!);
    }

    private static async Task<WebhookDelivery> OnlyDeliveryAsync(Fixture f)
    {
        await using var db = f.Factory.CreateDbContext();
        return await db.WebhookDeliveries.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeliversAPing_SignedWithTheSecret_AndMarksItDelivered()
    {
        var f = await SetupAsync(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var ct = TestContext.Current.CancellationToken;
        await f.Webhooks.SendTestAsync(f.WorkspaceId, f.Hook.Id, ct);

        var sent = await f.Sender.ProcessDueAsync(ct);

        Assert.Equal(1, sent);
        var (request, body) = Assert.Single(f.Handler.Received);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://erp.acme.com.br/nexus", request.RequestUri!.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("ping", request.Headers.GetValues("X-Nexus-Event").Single());
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(f.Secret), Encoding.UTF8.GetBytes(body)));
        Assert.Equal(expected, request.Headers.GetValues("X-Nexus-Signature").Single());
        Assert.Contains("\"type\":\"ping\"", body);

        var delivery = await OnlyDeliveryAsync(f);
        Assert.Equal(WebhookDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(204, delivery.ResponseStatusCode);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(delivery.Id.ToString(), request.Headers.GetValues("X-Nexus-Delivery").Single());
        await using var db = f.Factory.CreateDbContext();
        Assert.Equal(Start.UtcDateTime, (await db.WorkspaceWebhooks.SingleAsync(ct)).LastDeliveryAtUtc);
    }

    [Fact]
    public async Task RetriesAfterOneFiveAndThirtyMinutes_ThenFails()
    {
        var f = await SetupAsync(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var ct = TestContext.Current.CancellationToken;
        await f.Webhooks.SendTestAsync(f.WorkspaceId, f.Hook.Id, ct);

        Assert.Equal(1, await f.Sender.ProcessDueAsync(ct));
        var afterFirst = await OnlyDeliveryAsync(f);
        Assert.Equal(WebhookDeliveryStatus.Pending, afterFirst.Status);
        Assert.Equal(Start.UtcDateTime.AddMinutes(1), afterFirst.NextAttemptAtUtc);
        Assert.Contains("500", afterFirst.ResponseSummary);
        Assert.Contains("boom", afterFirst.ResponseSummary);

        Assert.Equal(0, await f.Sender.ProcessDueAsync(ct));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await f.Sender.ProcessDueAsync(ct));
        Assert.Equal(Start.UtcDateTime.AddMinutes(6), (await OnlyDeliveryAsync(f)).NextAttemptAtUtc);

        f.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await f.Sender.ProcessDueAsync(ct));
        Assert.Equal(Start.UtcDateTime.AddMinutes(36), (await OnlyDeliveryAsync(f)).NextAttemptAtUtc);

        f.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, await f.Sender.ProcessDueAsync(ct));
        var final = await OnlyDeliveryAsync(f);
        Assert.Equal(WebhookDeliveryStatus.Failed, final.Status);
        Assert.Equal(WebhookDelivery.MaxAttempts, final.Attempts);
        Assert.Equal(4, f.Handler.Received.Count);
        await using var db = f.Factory.CreateDbContext();
        Assert.Equal(1, (await db.WorkspaceWebhooks.SingleAsync(ct)).ConsecutiveFailures);
    }

    [Fact]
    public async Task ANetworkErrorCountsAsAFailedAttempt()
    {
        var f = await SetupAsync(_ => throw new HttpRequestException("Nome não resolvido"));
        var ct = TestContext.Current.CancellationToken;
        await f.Webhooks.SendTestAsync(f.WorkspaceId, f.Hook.Id, ct);

        await f.Sender.ProcessDueAsync(ct);

        var delivery = await OnlyDeliveryAsync(f);
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Null(delivery.ResponseStatusCode);
        Assert.Equal("Nome não resolvido", delivery.ResponseSummary);
    }

    [Fact]
    public async Task PausesTheWebhook_AndNotifiesAdmins_AfterTooManyFinalFailures()
    {
        var f = await SetupAsync(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var ct = TestContext.Current.CancellationToken;
        await using (var db = f.Factory.CreateDbContext())
        {
            db.Users.AddRange(
                new ApplicationUser { Id = "owner", UserName = "owner", Email = "owner@acme.com.br", DisplayName = "Dona" },
                new ApplicationUser { Id = "member", UserName = "member", Email = "m@acme.com.br", DisplayName = "Membro" });
            db.WorkspaceMembers.AddRange(
                new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = f.WorkspaceId, UserId = "owner", Role = WorkspaceRole.Owner, JoinedAtUtc = DateTime.UtcNow },
                new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = f.WorkspaceId, UserId = "member", Role = WorkspaceRole.Member, JoinedAtUtc = DateTime.UtcNow });
            var hook = await db.WorkspaceWebhooks.SingleAsync(ct);
            hook.ConsecutiveFailures = WorkspaceWebhook.ConsecutiveFailuresBeforePause - 1;
            await db.SaveChangesAsync(ct);
        }

        await f.Webhooks.SendTestAsync(f.WorkspaceId, f.Hook.Id, ct);
        for (var attempt = 0; attempt < WebhookDelivery.MaxAttempts; attempt++)
        {
            await f.Sender.ProcessDueAsync(ct);
            f.Clock.Advance(TimeSpan.FromMinutes(31));
        }

        await using var check = f.Factory.CreateDbContext();
        var paused = await check.WorkspaceWebhooks.SingleAsync(ct);
        Assert.False(paused.IsActive);
        Assert.NotNull(paused.PausedAtUtc);
        var notification = await check.Notifications.SingleAsync(ct);
        Assert.Equal("owner", notification.UserId);
        Assert.Equal(NotificationType.WebhookPaused, notification.Type);
        Assert.Contains("pausado", notification.Message);
    }

    [Fact]
    public async Task APausedWebhookDoesNotGetCalled()
    {
        var f = await SetupAsync(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var ct = TestContext.Current.CancellationToken;
        await f.Webhooks.SendTestAsync(f.WorkspaceId, f.Hook.Id, ct);
        await f.Webhooks.SetActiveAsync(f.WorkspaceId, f.Hook.Id, false, ct);

        await f.Sender.ProcessDueAsync(ct);

        Assert.Empty(f.Handler.Received);
        Assert.Equal(WebhookDeliveryStatus.Failed, (await OnlyDeliveryAsync(f)).Status);
    }
}
