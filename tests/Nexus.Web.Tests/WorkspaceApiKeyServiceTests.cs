using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

public class WorkspaceApiKeyServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedWorkspaceAsync(IDbContextFactory<AppDbContext> factory, bool archived = false, WorkspacePlan? plan = null)
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = id, Name = "Acme", Slug = "acme", IsArchived = archived, CreatedAtUtc = DateTime.UtcNow });
        if (plan is { } p)
        {
            var subscription = WorkspaceSubscription.StartStarter(id, Start.UtcDateTime);
            subscription.Plan = p;
            db.WorkspaceSubscriptions.Add(subscription);
        }

        await db.SaveChangesAsync();
        return id;
    }

    private static WorkspaceApiKeyService Service(IDbContextFactory<AppDbContext> factory, FakeTimeProvider? clock = null, bool enforceLimits = false)
    {
        clock ??= new FakeTimeProvider(Start);
        var plans = new WorkspacePlanService(factory, clock, Options.Create(new PlanOptions { EnforceLimits = enforceLimits }));
        return new WorkspaceApiKeyService(factory, new AuditLogService(factory), plans, clock);
    }

    [Fact]
    public async Task Create_ReturnsTheSecretOnce_AndStoresOnlyItsHash()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory).CreateAsync(workspaceId, "  ERP Protheus ", ApiKeyScope.ReadWrite, "user-1", ct);

        Assert.Equal(ApiKeyCreateStatus.Created, result.Status);
        Assert.StartsWith(WorkspaceApiKeyService.SecretPrefix, result.Secret);
        await using var db = factory.CreateDbContext();
        var stored = await db.WorkspaceApiKeys.SingleAsync(ct);
        Assert.Equal("ERP Protheus", stored.Name);
        Assert.Equal(ApiKeyScope.ReadWrite, stored.Scope);
        Assert.Equal(WorkspaceApiKeyService.Hash(result.Secret!), stored.KeyHash);
        Assert.DoesNotContain(result.Secret!, stored.KeyHash);
        Assert.StartsWith(stored.KeyPrefix, result.Secret);
        Assert.True(stored.KeyPrefix.Length < result.Secret!.Length / 2);
        var audit = await db.AuditLogEntries.SingleAsync(ct);
        Assert.Equal(AuditAction.WorkspaceApiKeyCreated, audit.Action);
        Assert.Equal("user-1", audit.ActorUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_RefusesAnEmptyName(string name)
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory).CreateAsync(workspaceId, name, ApiKeyScope.ReadWrite, "user-1", ct);

        Assert.Equal(ApiKeyCreateStatus.InvalidName, result.Status);
        Assert.Null(result.Secret);
    }

    [Fact]
    public async Task Create_StopsAtTheActiveKeyLimit_ButRevokedKeysDoNotCount()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var first = await service.CreateAsync(workspaceId, "chave 1", ApiKeyScope.ReadWrite, null, ct);
        for (var i = 2; i <= WorkspaceApiKeyService.MaxActiveKeysPerWorkspace; i++)
        {
            await service.CreateAsync(workspaceId, $"chave {i}", ApiKeyScope.ReadWrite, null, ct);
        }

        var overLimit = await service.CreateAsync(workspaceId, "uma a mais", ApiKeyScope.ReadWrite, null, ct);
        await service.RevokeAsync(workspaceId, first.Key!.Id, null, ct);
        var afterRevoke = await service.CreateAsync(workspaceId, "no lugar da revogada", ApiKeyScope.ReadWrite, null, ct);

        Assert.Equal(ApiKeyCreateStatus.LimitReached, overLimit.Status);
        Assert.Equal(ApiKeyCreateStatus.Created, afterRevoke.Status);
    }

    [Fact]
    public async Task Authenticate_AcceptsAValidSecret_AndRecordsTheLastUse()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        var service = Service(factory, clock);
        var created = await service.CreateAsync(workspaceId, "ERP", ApiKeyScope.ReadWrite, "user-1", ct);

        var identity = await service.AuthenticateAsync(created.Secret, ct);

        Assert.NotNull(identity);
        Assert.Equal(workspaceId, identity.WorkspaceId);
        Assert.Equal(created.Key!.Id, identity.KeyId);
        Assert.Equal("ERP", identity.Name);
        Assert.Equal("user-1", identity.CreatedByUserId);
        Assert.False(identity.WorkspaceArchived);
        await using var db = factory.CreateDbContext();
        Assert.Equal(Start.UtcDateTime, (await db.WorkspaceApiKeys.SingleAsync(ct)).LastUsedAtUtc);
    }

    [Fact]
    public async Task Authenticate_OnlyRewritesTheLastUseAfterAMinute()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        var service = Service(factory, clock);
        var created = await service.CreateAsync(workspaceId, "ERP", ApiKeyScope.ReadWrite, null, ct);

        await service.AuthenticateAsync(created.Secret, ct);
        clock.Advance(TimeSpan.FromSeconds(30));
        await service.AuthenticateAsync(created.Secret, ct);
        var afterThirtySeconds = await LastUsedAsync(factory, ct);
        clock.Advance(TimeSpan.FromSeconds(31));
        await service.AuthenticateAsync(created.Secret, ct);
        var afterAMinute = await LastUsedAsync(factory, ct);

        Assert.Equal(Start.UtcDateTime, afterThirtySeconds);
        Assert.Equal(Start.UtcDateTime.AddSeconds(61), afterAMinute);
    }

    [Fact]
    public async Task Authenticate_FlagsAnArchivedWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory, archived: true);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var created = await service.CreateAsync(workspaceId, "ERP", ApiKeyScope.ReadWrite, null, ct);

        var identity = await service.AuthenticateAsync(created.Secret, ct);

        Assert.True(identity!.WorkspaceArchived);
    }

    [Fact]
    public async Task Authenticate_RejectsRevokedAndUnknownSecrets()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var created = await service.CreateAsync(workspaceId, "ERP", ApiKeyScope.ReadWrite, "user-1", ct);
        Assert.True(await service.RevokeAsync(workspaceId, created.Key!.Id, "user-1", ct));

        Assert.Null(await service.AuthenticateAsync(created.Secret, ct));
        Assert.Null(await service.AuthenticateAsync(WorkspaceApiKeyService.NewSecret(), ct));
        Assert.Null(await service.AuthenticateAsync("", ct));
        Assert.Null(await service.AuthenticateAsync(null, ct));
        Assert.Null(await service.AuthenticateAsync("Bearer " + created.Secret, ct));
        Assert.Null(await service.AuthenticateAsync(created.Secret + new string('a', 200), ct));
    }

    [Fact]
    public async Task Revoke_IgnoresKeysOfOtherWorkspaces_AndKeysAlreadyRevoked()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var otherWorkspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);
        var created = await service.CreateAsync(workspaceId, "ERP", ApiKeyScope.ReadWrite, null, ct);

        Assert.False(await service.RevokeAsync(otherWorkspaceId, created.Key!.Id, null, ct));
        Assert.NotNull(await service.AuthenticateAsync(created.Secret, ct));
        Assert.True(await service.RevokeAsync(workspaceId, created.Key.Id, null, ct));
        Assert.False(await service.RevokeAsync(workspaceId, created.Key.Id, null, ct));
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.WorkspaceApiKeyRevoked, ct));
    }

    [Fact]
    public async Task Authenticate_ReportsTheScope_AndReadOnlyKeysStayReadOnly()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Start);
        var service = Service(factory, clock);
        var reader = await service.CreateAsync(workspaceId, "Painel BI", ApiKeyScope.ReadOnly, null, ct);
        clock.Advance(TimeSpan.FromMinutes(1));
        var writer = await service.CreateAsync(workspaceId, "ERP", ApiKeyScope.ReadWrite, null, ct);

        Assert.Equal(ApiKeyScope.ReadOnly, (await service.AuthenticateAsync(reader.Secret, ct))!.Scope);
        Assert.Equal(ApiKeyScope.ReadWrite, (await service.AuthenticateAsync(writer.Secret, ct))!.Scope);
        Assert.Equal([ApiKeyScope.ReadWrite, ApiKeyScope.ReadOnly], (await service.ListAsync(workspaceId, ct)).Select(k => k.Scope));
    }

    [Fact]
    public async Task WithLimitsEnforced_StarterHasNoApi_ButPaidPlansDo()
    {
        var factory = TestDb.CreateFactory();
        var starter = await SeedWorkspaceAsync(factory, plan: WorkspacePlan.Starter);
        var pro = await SeedWorkspaceAsync(factory, plan: WorkspacePlan.Pro);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory, enforceLimits: true);

        var refused = await service.CreateAsync(starter, "ERP", ApiKeyScope.ReadWrite, null, ct);
        var created = await service.CreateAsync(pro, "ERP", ApiKeyScope.ReadWrite, null, ct);

        Assert.Equal(ApiKeyCreateStatus.PlanDoesNotIncludeApi, refused.Status);
        Assert.Equal(ApiKeyCreateStatus.Created, created.Status);
        Assert.True((await service.AuthenticateAsync(created.Secret, ct))!.ApiAccessAllowed);
    }

    [Fact]
    public async Task WithLimitsEnforced_AKeyOfAWorkspaceBackOnStarter_StillAuthenticates_ButWithoutApiAccess()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory, plan: WorkspacePlan.Pro);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory, enforceLimits: true);
        var created = await service.CreateAsync(workspaceId, "ERP", ApiKeyScope.ReadWrite, null, ct);

        await using (var db = factory.CreateDbContext())
        {
            (await db.WorkspaceSubscriptions.SingleAsync(s => s.WorkspaceId == workspaceId, ct)).Plan = WorkspacePlan.Starter;
            await db.SaveChangesAsync(ct);
        }

        var identity = await service.AuthenticateAsync(created.Secret, ct);

        Assert.NotNull(identity);
        Assert.False(identity.ApiAccessAllowed);
    }

    [Fact]
    public async Task WithoutLimitsEnforced_EveryPlanHasApiAccess()
    {
        var factory = TestDb.CreateFactory();
        var starter = await SeedWorkspaceAsync(factory, plan: WorkspacePlan.Starter);
        var ct = TestContext.Current.CancellationToken;
        var service = Service(factory);

        var created = await service.CreateAsync(starter, "ERP", ApiKeyScope.ReadOnly, null, ct);

        Assert.Equal(ApiKeyCreateStatus.Created, created.Status);
        Assert.True((await service.AuthenticateAsync(created.Secret, ct))!.ApiAccessAllowed);
    }

    private static async Task<DateTime?> LastUsedAsync(IDbContextFactory<AppDbContext> factory, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        return (await db.WorkspaceApiKeys.SingleAsync(ct)).LastUsedAtUtc;
    }
}
