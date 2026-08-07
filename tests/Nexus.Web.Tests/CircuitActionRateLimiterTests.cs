using Microsoft.Extensions.Time.Testing;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Regression tests for M8 — no throttle existed on actions performed over the Blazor circuit
/// (chat sends, attachment uploads), only on the static-SSR auth pages.
/// </summary>
public class CircuitActionRateLimiterTests
{
    [Fact]
    public void AllowsUpToThePermitLimit()
    {
        var limiter = new CircuitActionRateLimiter(new FakeTimeProvider());

        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.TryAcquire("u1", 20, TimeSpan.FromMinutes(1)), $"attempt {i + 1} should be allowed");
        }
    }

    [Fact]
    public void BlocksBeyondThePermitLimit()
    {
        var limiter = new CircuitActionRateLimiter(new FakeTimeProvider());

        for (var i = 0; i < 20; i++)
        {
            limiter.TryAcquire("u1", 20, TimeSpan.FromMinutes(1));
        }

        Assert.False(limiter.TryAcquire("u1", 20, TimeSpan.FromMinutes(1)));
        Assert.False(limiter.TryAcquire("u1", 20, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void WindowResetsAfterItElapses()
    {
        var clock = new FakeTimeProvider();
        var limiter = new CircuitActionRateLimiter(clock);

        for (var i = 0; i < 20; i++)
        {
            limiter.TryAcquire("u1", 20, TimeSpan.FromMinutes(1));
        }
        Assert.False(limiter.TryAcquire("u1", 20, TimeSpan.FromMinutes(1)));

        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryAcquire("u1", 20, TimeSpan.FromMinutes(1)));
    }

    /// <summary>
    /// Partitioning matters as much as the limit: one noisy user must not be able to block
    /// everyone else, which is the failure mode of a single global bucket.
    /// </summary>
    [Fact]
    public void OneUserExhaustingTheirBudgetDoesNotAffectAnother()
    {
        var limiter = new CircuitActionRateLimiter(new FakeTimeProvider());

        for (var i = 0; i < 20; i++)
        {
            limiter.TryAcquire("noisy", 20, TimeSpan.FromMinutes(1));
        }

        Assert.False(limiter.TryAcquire("noisy", 20, TimeSpan.FromMinutes(1)));
        Assert.True(limiter.TryAcquire("quiet", 20, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void DifferentActionsForTheSameUserAreCountedSeparately()
    {
        var limiter = new CircuitActionRateLimiter(new FakeTimeProvider());

        for (var i = 0; i < 20; i++)
        {
            limiter.TryAcquire("chat-send:u1", 20, TimeSpan.FromMinutes(1));
        }

        Assert.False(limiter.TryAcquire("chat-send:u1", 20, TimeSpan.FromMinutes(1)));
        Assert.True(limiter.TryAcquire("upload:u1", 20, TimeSpan.FromMinutes(1)));
    }

    /// <summary>
    /// Regression test for RL1 — the stale-entry sweep used to run on every call once the
    /// dictionary held 1000+ entries. With that many *active* users nothing is stale, so the sweep
    /// removed nothing, the count never dropped, and every TryAcquire walked the whole dictionary:
    /// an O(n) cost per chat message at exactly the traffic level the limiter exists to handle.
    ///
    /// Asserts the throughput stays flat when the dictionary is large, which is only true if the
    /// sweep is time-bounded rather than size-bounded.
    /// </summary>
    [Fact]
    public void LargeDictionary_DoesNotDegradePerCallCost()
    {
        var clock = new FakeTimeProvider();
        var limiter = new CircuitActionRateLimiter(clock);
        var window = TimeSpan.FromMinutes(1);

        // 5000 distinct, all fresh — none are eligible for removal.
        for (var i = 0; i < 5000; i++)
        {
            limiter.TryAcquire($"user-{i}", 20, window);
        }

        // Time does not advance, so a time-bounded sweep must not run again during this loop.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 5000; i++)
        {
            limiter.TryAcquire($"user-{i}", 20, window);
        }
        sw.Stop();

        // The size-based implementation performed 5000 full scans of a 5000-entry dictionary
        // (~25M comparisons) here. Generous bound so the test is not flaky on a loaded agent,
        // but far below what a quadratic implementation could achieve.
        Assert.True(sw.ElapsedMilliseconds < 1000, $"5000 calls against a 5000-entry limiter took {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// The sweep must still happen — entries for users who left have to be reclaimed, or the
    /// dictionary is a slow memory leak.
    /// </summary>
    [Fact]
    public void StaleEntries_AreEventuallyReclaimed()
    {
        var clock = new FakeTimeProvider();
        var limiter = new CircuitActionRateLimiter(clock);
        var window = TimeSpan.FromMinutes(1);

        for (var i = 0; i < 100; i++)
        {
            limiter.TryAcquire($"gone-{i}", 20, window);
        }

        // Well past both the window and the prune interval.
        clock.Advance(TimeSpan.FromMinutes(30));
        limiter.TryAcquire("someone-still-here", 20, window);

        // The reclaimed users get a fresh window, which is observable: they are allowed the full
        // permit budget again rather than being remembered as exhausted.
        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.TryAcquire("gone-0", 20, window), $"attempt {i + 1} after reclamation should be allowed");
        }
    }

    /// <summary>
    /// The limiter is a singleton shared by every circuit, so concurrent access must neither
    /// throw nor over-admit.
    /// </summary>
    [Fact]
    public async Task ConcurrentCallers_NeverExceedThePermitLimit()
    {
        var limiter = new CircuitActionRateLimiter(new FakeTimeProvider());
        var granted = 0;

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() =>
        {
            if (limiter.TryAcquire("shared", 20, TimeSpan.FromMinutes(1)))
            {
                Interlocked.Increment(ref granted);
            }
        })));

        Assert.Equal(20, granted);
    }
}
