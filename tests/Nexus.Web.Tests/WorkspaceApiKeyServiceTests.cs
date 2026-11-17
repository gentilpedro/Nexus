using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

public class WorkspaceApiKeyServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedWorkspaceAsync(IDbContextFactory<AppDbContext> factory, bool archived = false)
    {
        var id = Guid.NewGuid();
        await using var db = factory.CreateDbContext();
        db.Workspaces.Add(new Workspace { Id = id, Name = "Acme", Slug = "acme", IsArchived = archived, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private static WorkspaceApiKeyService Service(IDbContextFactory<AppDbContext> factory, FakeTimeProvider? clock = null) =>
        new(factory, new AuditLogService(factory), clock ?? new FakeTimeProvider(Start));

    [Fact]
    public async Task Create_ReturnsTheSecretOnce_AndStoresOnlyItsHash()
    {
        var factory = TestDb.CreateFactory();
        var workspaceId = await SeedWorkspaceAsync(factory);
        var ct = TestContext.Current.CancellationToken;

        var result = await Service(factory).CreateAsync(workspaceId, "  ERP Protheus ", "user-1", ct);

        Assert.Equal(ApiKeyCreateStatus.Created, result.Status);
        Assert.StartsWith(WorkspaceApiKeyService.SecretPrefix, result.Secret);
        await using var db = factory.CreateDbContext();
        var stored = await db.WorkspaceApiKeys.SingleAsync(ct);
        Assert.Equal("ERP Protheus", stored.Name);
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

        var result = await Service(factory).CreateAsync(workspaceId, name, "user-1", ct);

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
        var first = await service.CreateAsync(workspaceId, "chave 1", null, ct);
        for (var i = 2; i <= WorkspaceApiKeyService.MaxActiveKeysPerWorkspace; i++)
        {
            await service.CreateAsync(workspaceId, $"chave {i}", null, ct);
        }

        var overLimit = await service.CreateAsync(workspaceId, "uma a mais", null, ct);
        await service.RevokeAsync(workspaceId, first.Key!.Id, null, ct);
        var afterRevoke = await service.CreateAsync(workspaceId, "no lugar da revogada", null, ct);

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
        var created = await service.CreateAsync(workspaceId, "ERP", "user-1", ct);

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
        var created = await service.CreateAsync(workspaceId, "ERP", null, ct);

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
        var created = await service.CreateAsync(workspaceId, "ERP", null, ct);

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
        var created = await service.CreateAsync(workspaceId, "ERP", "user-1", ct);
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
        var created = await service.CreateAsync(workspaceId, "ERP", null, ct);

        Assert.False(await service.RevokeAsync(otherWorkspaceId, created.Key!.Id, null, ct));
        Assert.NotNull(await service.AuthenticateAsync(created.Secret, ct));
        Assert.True(await service.RevokeAsync(workspaceId, created.Key.Id, null, ct));
        Assert.False(await service.RevokeAsync(workspaceId, created.Key.Id, null, ct));
        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.Action == AuditAction.WorkspaceApiKeyRevoked, ct));
    }

    private static async Task<DateTime?> LastUsedAsync(IDbContextFactory<AppDbContext> factory, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        return (await db.WorkspaceApiKeys.SingleAsync(ct)).LastUsedAtUtc;
    }
}
