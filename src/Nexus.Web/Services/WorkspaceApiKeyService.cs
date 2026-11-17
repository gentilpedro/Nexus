using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

public enum ApiKeyCreateStatus
{
    Created,
    InvalidName,
    LimitReached,
}

/// <param name="Secret">O valor completo da chave. Só existe aqui: o banco guarda o hash.</param>
public sealed record ApiKeyCreateResult(ApiKeyCreateStatus Status, WorkspaceApiKey? Key = null, string? Secret = null);

/// <summary>Quem está do outro lado de uma requisição da API, depois de validar a chave.</summary>
public sealed record ApiKeyIdentity(Guid KeyId, Guid WorkspaceId, string Name, string? CreatedByUserId, bool WorkspaceArchived);

// Chaves da API de integração (#66). O segredo é "nxk_" + 32 bytes aleatórios; no banco fica só
// o SHA-256 dele, então um vazamento do banco não entrega chave nenhuma. Como o segredo tem
// entropia alta, o hash simples basta: não há senha fraca para proteger com bcrypt.
public class WorkspaceApiKeyService(
    IDbContextFactory<AppDbContext> dbFactory,
    AuditLogService audit,
    TimeProvider? timeProvider = null)
{
    public const string SecretPrefix = "nxk_";
    public const int MaxActiveKeysPerWorkspace = 20;

    // O "último uso" é informativo. Gravar a cada requisição custaria um UPDATE por chamada,
    // então ele só avança quando o valor gravado tem mais de um minuto.
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(1);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public static string NewSecret() => SecretPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    // O que a lista mostra: "nxk_a1b2c3". Curto o bastante para não ajudar a adivinhar o resto.
    public static string PrefixOf(string secret) => secret[..Math.Min(secret.Length, SecretPrefix.Length + 6)];

    public async Task<List<WorkspaceApiKey>> ListAsync(Guid workspaceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceApiKeys
            .AsNoTracking()
            .Where(k => k.WorkspaceId == workspaceId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<ApiKeyCreateResult> CreateAsync(Guid workspaceId, string? name, string? createdByUserId, CancellationToken ct = default)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > WorkspaceApiKey.MaxNameLength)
        {
            return new ApiKeyCreateResult(ApiKeyCreateStatus.InvalidName);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var active = await db.WorkspaceApiKeys.CountAsync(k => k.WorkspaceId == workspaceId && k.RevokedAtUtc == null, ct);
        if (active >= MaxActiveKeysPerWorkspace)
        {
            return new ApiKeyCreateResult(ApiKeyCreateStatus.LimitReached);
        }

        var secret = NewSecret();
        var key = new WorkspaceApiKey
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name,
            KeyPrefix = PrefixOf(secret),
            KeyHash = Hash(secret),
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.WorkspaceApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(createdByUserId, AuditAction.WorkspaceApiKeyCreated, key.Id.ToString(), $"{name} ({key.KeyPrefix}) no workspace {workspaceId}", ct);

        return new ApiKeyCreateResult(ApiKeyCreateStatus.Created, key, secret);
    }

    /// <returns>False quando a chave não existe neste workspace ou já estava revogada.</returns>
    public async Task<bool> RevokeAsync(Guid workspaceId, Guid keyId, string? actorUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var key = await db.WorkspaceApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.WorkspaceId == workspaceId, ct);
        if (key is null || key.RevokedAtUtc is not null)
        {
            return false;
        }

        key.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(actorUserId, AuditAction.WorkspaceApiKeyRevoked, key.Id.ToString(), $"{key.Name} ({key.KeyPrefix}) no workspace {workspaceId}", ct);
        return true;
    }

    /// <returns>Null para chave ausente, mal formada, desconhecida ou revogada.</returns>
    public async Task<ApiKeyIdentity?> AuthenticateAsync(string? secret, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secret)
            || secret.Length > 128
            || !secret.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Hash(secret);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var key = await db.WorkspaceApiKeys
            .AsNoTracking()
            .Where(k => k.KeyHash == hash && k.RevokedAtUtc == null)
            .Select(k => new { k.Id, k.WorkspaceId, k.Name, k.CreatedByUserId, k.LastUsedAtUtc, k.Workspace.IsArchived })
            .FirstOrDefaultAsync(ct);
        if (key is null)
        {
            return null;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (key.LastUsedAtUtc is null || now - key.LastUsedAtUtc.Value >= LastUsedWriteInterval)
        {
            await TouchAsync(db, key.Id, now, ct);
        }

        return new ApiKeyIdentity(key.Id, key.WorkspaceId, key.Name, key.CreatedByUserId, key.IsArchived);
    }

    private static async Task TouchAsync(AppDbContext db, Guid keyId, DateTime now, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.WorkspaceApiKeys.Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAtUtc, now), ct);
            return;
        }

        // Provedor em memória (testes): sem ExecuteUpdate.
        var tracked = await db.WorkspaceApiKeys.FirstAsync(k => k.Id == keyId, ct);
        tracked.LastUsedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }
}
