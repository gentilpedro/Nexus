using System.Collections.Concurrent;

using Nexus.Web.Services.Backplane;

namespace Nexus.Web.Services;

/// <summary>
/// A small per-user fixed-window limiter for actions that happen over the Blazor SignalR
/// circuit rather than as discrete HTTP requests.
/// </summary>
/// <remarks>
/// <para>
/// The ASP.NET Core rate limiter middleware in Program.cs can only see real HTTP requests, so it
/// covers the static-SSR auth pages and nothing else. Everything the app does once a circuit is
/// established — posting chat messages, uploading attachments — travels over one long-lived
/// WebSocket and was completely unthrottled: a single authenticated user could post as fast as
/// the server would accept, and every message fans out a notification row per workspace member
/// plus a broadcast to every connected circuit.
/// </para>
/// <para>
/// Registered as a singleton. Entries are pruned opportunistically so the dictionary cannot grow
/// without bound as users come and go.
/// </para>
/// </remarks>
public class CircuitActionRateLimiter(TimeProvider? timeProvider = null) : ICircuitActionRateLimiter
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Window> windows = new();

    // How often the stale-entry sweep may run, regardless of how many entries exist.
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);
    private long lastPruneTicks;

    private sealed class Window
    {
        public DateTimeOffset StartedAt;
        public int Count;
    }

    /// <summary>
    /// Records an attempt and reports whether it is allowed.
    /// </summary>
    /// <param name="key">Partition key — a user id, optionally combined with the action name.</param>
    /// <param name="permitLimit">Attempts allowed per window.</param>
    /// <param name="window">Window length.</param>
    public bool TryAcquire(string key, int permitLimit, TimeSpan window)
    {
        var now = clock.GetUtcNow();
        var allowed = true;

        windows.AddOrUpdate(
            key,
            _ => new Window { StartedAt = now, Count = 1 },
            (_, existing) =>
            {
                lock (existing)
                {
                    if (now - existing.StartedAt >= window)
                    {
                        existing.StartedAt = now;
                        existing.Count = 1;
                    }
                    else if (existing.Count >= permitLimit)
                    {
                        allowed = false;
                    }
                    else
                    {
                        existing.Count++;
                    }
                }

                return existing;
            });

        Prune(now, window);
        return allowed;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Completes synchronously: the state is a local dictionary, so there is nothing to await.
    /// The async signature exists because the interface must also fit a limiter whose state
    /// lives in Redis.
    /// </remarks>
    public ValueTask<bool> TryAcquireAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(TryAcquire(key, permitLimit, window));

    // Time-based cleanup, not size-based.
    //
    // This used to skip the scan while the dictionary held fewer than 1000 entries. That guard
    // inverted at scale: with 1000+ *active* users, none of their windows are stale, so the scan
    // removed nothing, the count stayed above the threshold, and every single TryAcquire walked
    // the entire dictionary. The limiter added to protect the chat became O(n) per message
    // exactly at the traffic level where it mattered.
    //
    // Running at most once per PruneInterval makes the cost independent of how many users are
    // active: the common path is a single dictionary operation plus one Interlocked read.
    private void Prune(DateTimeOffset now, TimeSpan window)
    {
        var last = Interlocked.Read(ref lastPruneTicks);
        if (now.UtcTicks - last < PruneInterval.Ticks)
        {
            return;
        }

        // Only the thread that wins this exchange does the scan; everyone else moves on.
        if (Interlocked.CompareExchange(ref lastPruneTicks, now.UtcTicks, last) != last)
        {
            return;
        }

        foreach (var (key, value) in windows)
        {
            if (now - value.StartedAt >= window + window)
            {
                windows.TryRemove(key, out _);
            }
        }
    }
}
