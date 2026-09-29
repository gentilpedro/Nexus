using System.Collections.Concurrent;
using System.Globalization;
using StackExchange.Redis;

namespace Nexus.Web.Services.Collab;

/// <summary>Alguém com o documento aberto em edição.</summary>
public sealed record DocEditor(string UserId, string Name);

/// <summary>
/// Quem está editando cada documento agora. Cada editor aberto é uma sessão que entra, renova a
/// presença de tempos em tempos (<see cref="Heartbeat"/>) e sai.
/// </summary>
/// <remarks>
/// A presença expira sozinha depois de <see cref="Lifetime"/> sem renovação. Sair é o caminho
/// normal, mas um circuito que cai sem avisar (instância reiniciada, processo morto) nunca chama
/// <see cref="LeaveAsync"/>, e sem expiração a pessoa ficaria "editando" para sempre.
/// </remarks>
public interface IDocPresenceStore
{
    static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);
    static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(75);

    Task JoinAsync(Guid docPageId, Guid sessionId, DocEditor editor, CancellationToken cancellationToken = default);

    Task LeaveAsync(Guid docPageId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Quem está editando, uma vez por pessoa (duas abas da mesma pessoa contam uma).</summary>
    Task<IReadOnlyList<DocEditor>> ListAsync(Guid docPageId, CancellationToken cancellationToken = default);
}

/// <summary>Instância única: a presença vive na memória do processo.</summary>
public sealed class InMemoryDocPresenceStore(TimeProvider? timeProvider = null) : IDocPresenceStore
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, (DocEditor Editor, DateTimeOffset ExpiresAt)>> docs = new();

    public Task JoinAsync(Guid docPageId, Guid sessionId, DocEditor editor, CancellationToken cancellationToken = default)
    {
        docs.GetOrAdd(docPageId, _ => new())[sessionId] = (editor, clock.GetUtcNow() + IDocPresenceStore.Lifetime);
        return Task.CompletedTask;
    }

    public Task LeaveAsync(Guid docPageId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (docs.TryGetValue(docPageId, out var sessions))
        {
            sessions.TryRemove(sessionId, out _);
            if (sessions.IsEmpty)
            {
                docs.TryRemove(KeyValuePair.Create(docPageId, sessions));
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocEditor>> ListAsync(Guid docPageId, CancellationToken cancellationToken = default)
    {
        if (!docs.TryGetValue(docPageId, out var sessions))
        {
            return Task.FromResult<IReadOnlyList<DocEditor>>([]);
        }

        var now = clock.GetUtcNow();
        foreach (var (sessionId, entry) in sessions)
        {
            if (entry.ExpiresAt <= now)
            {
                sessions.TryRemove(sessionId, out _);
            }
        }
        return Task.FromResult(Distinct(sessions.Values.Select(v => v.Editor)));
    }

    internal static IReadOnlyList<DocEditor> Distinct(IEnumerable<DocEditor> editors) =>
        editors.GroupBy(e => e.UserId).Select(g => g.First()).OrderBy(e => e.Name, StringComparer.CurrentCulture).ToList();
}

/// <summary>
/// Várias instâncias: a presença fica num hash do Redis por documento, visível para todas.
/// </summary>
/// <remarks>
/// Cada sessão é um campo do hash com o prazo embutido no valor, porque o Redis expira a chave
/// inteira, não campos. A chave também recebe expiração, para que um documento que ninguém mais
/// abre não deixe lixo. Presença é informativa: com o Redis fora do ar, a lista fica vazia em vez
/// de derrubar a edição.
/// </remarks>
public sealed class RedisDocPresenceStore(
    IConnectionMultiplexer redis,
    ILogger<RedisDocPresenceStore> logger,
    TimeProvider? timeProvider = null) : IDocPresenceStore
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private static RedisKey Key(Guid docPageId) => $"nexus:docs:presence:{docPageId:N}";

    public async Task JoinAsync(Guid docPageId, Guid sessionId, DocEditor editor, CancellationToken cancellationToken = default)
    {
        var expiresAt = (clock.GetUtcNow() + IDocPresenceStore.Lifetime).ToUnixTimeMilliseconds();
        try
        {
            var db = redis.GetDatabase();
            // O nome vai por último: pode conter o separador, e é lido como "o resto".
            await db.HashSetAsync(Key(docPageId), sessionId.ToString("N"), $"{expiresAt.ToString(CultureInfo.InvariantCulture)}|{editor.UserId}|{editor.Name}");
            await db.KeyExpireAsync(Key(docPageId), IDocPresenceStore.Lifetime);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível registrar a presença no documento {DocPageId}.", docPageId);
        }
    }

    public async Task LeaveAsync(Guid docPageId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().HashDeleteAsync(Key(docPageId), sessionId.ToString("N"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível remover a presença no documento {DocPageId}.", docPageId);
        }
    }

    public async Task<IReadOnlyList<DocEditor>> ListAsync(Guid docPageId, CancellationToken cancellationToken = default)
    {
        HashEntry[] entries;
        try
        {
            entries = await redis.GetDatabase().HashGetAllAsync(Key(docPageId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível ler a presença no documento {DocPageId}.", docPageId);
            return [];
        }

        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var editors = new List<DocEditor>();
        foreach (var entry in entries)
        {
            var parts = entry.Value.ToString().Split('|', 3);
            if (parts.Length == 3
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
                && expiresAt > now)
            {
                editors.Add(new DocEditor(parts[1], parts[2]));
            }
        }
        return InMemoryDocPresenceStore.Distinct(editors);
    }
}
