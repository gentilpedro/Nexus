using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;
using Nexus.Web.Services.Webhooks;

namespace Nexus.Web.Tests;

public class WebhookServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    internal static async Task<Guid> SeedWorkspaceAsync(IDbContextFactory<AppDbContext> factory)
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = id, Name = "Acme", Slug = "acme", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    internal static WebhookService Service(IDbContextFactory<AppDbContext> factory, FakeTimeProvider? clock = null, bool allowInsecure = false, bool enforceLimits = false)
    {
        clock ??= new FakeTimeProvider(Start);
        return new WebhookService(
            factory,
            new EphemeralDataProtectionProvider(),
            new AuditLogService(factory),
            new WorkspacePlanService(factory, clock, Options.Create(new PlanOptions { EnforceLimits = enforceLimits })),
            Options.Create(new WebhookOptions { AllowInsecureUrls = allowInsecure }),
            clock);
    }

    [Fact]
    public async Task Create_ShowsTheSecretOnce_AndStoresItEncrypted()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        var result = await service.CreateAsync(workspaceId, " ERP ", "https://erp.acme.com.br/nexus", WebhookEvents.TaskCreated | WebhookEvents.TaskStatusChanged, "user-1", ct);

        Assert.Equal(WebhookCreateStatus.Created, result.Status);
        Assert.StartsWith(WebhookService.SecretPrefix, result.Secret);
        await using var db = factory.CreateDbContext();
        var stored = await db.WorkspaceWebhooks.SingleAsync(ct);
        Assert.Equal("ERP", stored.Name);
        Assert.DoesNotContain(result.Secret!, stored.SecretProtected);
        Assert.Equal(result.Secret, service.Unprotect(stored.SecretProtected));
        Assert.True(stored.IsActive);
        Assert.True(stored.Subscribes(WebhookEvents.TaskCreated));
        Assert.False(stored.Subscribes(WebhookEvents.CommentCreated));
        Assert.Equal(AuditAction.WorkspaceWebhookCreated, (await db.AuditLogEntries.SingleAsync(ct)).Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("erp.acme.com.br/nexus")]
    [InlineData("http://erp.acme.com.br/nexus")]
    [InlineData("https://user:senha@erp.acme.com.br/nexus")]
    [InlineData("https://localhost/nexus")]
    [InlineData("https://erp.acme.local/nexus")]
    [InlineData("https://127.0.0.1/nexus")]
    [InlineData("https://10.0.0.5/nexus")]
    [InlineData("https://172.16.0.5/nexus")]
    [InlineData("https://192.168.1.10/nexus")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/nexus")]
    public async Task Create_RefusesUrlsThatAreNotPublicHttps(string url)
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory).CreateAsync(workspaceId, "ERP", url, WebhookEvents.All, null, ct);

        Assert.Equal(WebhookCreateStatus.Invalid, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Create_AcceptsLocalUrls_OnlyWhenInsecureUrlsAreAllowed()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory, allowInsecure: true).CreateAsync(workspaceId, "Local", "http://localhost:5000/hook", WebhookEvents.All, null, ct);

        Assert.Equal(WebhookCreateStatus.Created, result.Status);
    }

    [Fact]
    public async Task Create_RequiresAtLeastOneEvent_AndRespectsTheLimitAndThePlan()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        Assert.Equal(WebhookCreateStatus.Invalid, (await service.CreateAsync(workspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.None, null, ct)).Status);
        for (var i = 0; i < WebhookService.MaxWebhooksPerWorkspace; i++)
        {
            Assert.Equal(WebhookCreateStatus.Created, (await service.CreateAsync(workspaceId, $"hook {i}", $"https://erp.acme.com.br/{i}", WebhookEvents.All, null, ct)).Status);
        }

        Assert.Equal(WebhookCreateStatus.LimitReached, (await service.CreateAsync(workspaceId, "um a mais", "https://erp.acme.com.br/x", WebhookEvents.All, null, ct)).Status);
        Assert.Equal(WebhookCreateStatus.PlanDoesNotIncludeApi, (await Service(factory, enforceLimits: true).CreateAsync(await SeedWorkspaceAsync(factory), "ERP", "https://erp.acme.com.br/a", WebhookEvents.All, null, ct)).Status);
    }

    [Fact]
    public async Task Enqueue_CreatesOneDeliveryPerActiveSubscribedWebhook()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var otherWorkspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var subscribed = await service.CreateAsync(workspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.TaskStatusChanged, null, ct);
        var notSubscribed = await service.CreateAsync(workspaceId, "BI", "https://bi.acme.com.br/a", WebhookEvents.CommentCreated, null, ct);
        var paused = await service.CreateAsync(workspaceId, "Antigo", "https://old.acme.com.br/a", WebhookEvents.All, null, ct);
        await service.SetActiveAsync(workspaceId, paused.Webhook!.Id, false, ct);
        await service.CreateAsync(otherWorkspaceId, "Outro", "https://outro.com.br/a", WebhookEvents.All, null, ct);

        await using (var db = factory.CreateDbContext())
        {
            Assert.True(await service.AnySubscriberAsync(db, workspaceId, WebhookEvents.TaskStatusChanged, ct));
            Assert.False(await service.AnySubscriberAsync(db, workspaceId, WebhookEvents.TaskUpdated, ct));
            await service.EnqueueAsync(db, workspaceId, WebhookEvents.TaskStatusChanged, Guid.NewGuid(), "{}", ct);
            await db.SaveChangesAsync(ct);
        }

        await using var check = factory.CreateDbContext();
        var delivery = await check.WebhookDeliveries.SingleAsync(ct);
        Assert.Equal(subscribed.Webhook!.Id, delivery.WebhookId);
        Assert.Equal(WebhookEventNames.TaskStatusChanged, delivery.EventType);
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(Start.UtcDateTime, delivery.NextAttemptAtUtc);
        _ = notSubscribed;
    }

    [Fact]
    public async Task SendTest_EnqueuesAPing_AndResendClonesADelivery()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var hook = (await service.CreateAsync(workspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.TaskCreated, null, ct)).Webhook!;

        Assert.True(await service.SendTestAsync(workspaceId, hook.Id, ct));
        Assert.False(await service.SendTestAsync(Guid.NewGuid(), hook.Id, ct));
        var ping = Assert.Single(await service.ListDeliveriesAsync(workspaceId, hook.Id, ct));
        Assert.Equal(WebhookEventNames.Ping, ping.EventType);
        Assert.Contains("\"type\":\"ping\"", ping.Payload);

        Assert.True(await service.ResendAsync(workspaceId, ping.Id, ct));
        var all = await service.ListDeliveriesAsync(workspaceId, hook.Id, ct);
        Assert.Equal(2, all.Count);
        Assert.All(all, d => Assert.Equal(ping.EventId, d.EventId));
        Assert.All(all, d => Assert.Equal(WebhookDeliveryStatus.Pending, d.Status));
        Assert.Equal(2, all.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public async Task Reactivating_ResetsTheConsecutiveFailures()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var hook = (await service.CreateAsync(workspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.TaskCreated, null, ct)).Webhook!;
        await using (var db = factory.CreateDbContext())
        {
            var tracked = await db.WorkspaceWebhooks.SingleAsync(ct);
            tracked.IsActive = false;
            tracked.ConsecutiveFailures = 50;
            await db.SaveChangesAsync(ct);
        }

        Assert.True(await service.SetActiveAsync(workspaceId, hook.Id, true, ct));

        var reloaded = Assert.Single(await service.ListAsync(workspaceId, ct));
        Assert.True(reloaded.IsActive);
        Assert.Equal(0, reloaded.ConsecutiveFailures);
        Assert.Null(reloaded.PausedAtUtc);
    }

    [Fact]
    public async Task Delete_RemovesTheWebhook_OnlyInsideItsWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var hook = (await service.CreateAsync(workspaceId, "ERP", "https://erp.acme.com.br/a", WebhookEvents.TaskCreated, "user-1", ct)).Webhook!;

        Assert.False(await service.DeleteAsync(Guid.NewGuid(), hook.Id, "user-1", ct));
        Assert.True(await service.DeleteAsync(workspaceId, hook.Id, "user-1", ct));
        Assert.Empty(await service.ListAsync(workspaceId, ct));
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.WorkspaceWebhookDeleted, ct));
    }
}
