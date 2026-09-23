using StackExchange.Redis;

namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Fixed-window limiter whose counter lives in Redis, so every instance of the app counts
/// against the same allowance.
/// </summary>
public sealed class RedisCircuitActionRateLimiter(
    IConnectionMultiplexer redis,
    CircuitActionRateLimiter localFallback,
    ILogger<RedisCircuitActionRateLimiter> logger) : ICircuitActionRateLimiter
{
    /// <summary>
    /// Increments the window counter and returns its new value, creating the key with an
    /// expiry on the first hit of a window.
    /// </summary>
    /// <remarks>
    /// A script rather than <c>INCR</c> followed by <c>EXPIRE</c>: the two commands are not
    /// atomic together, so a process that dies between them (or simply loses the race) leaves a
    /// counter with no expiry. That key then never resets, and the user it belongs to is rate
    /// limited forever with no way to recover short of manual intervention. Redis runs a script
    /// to completion without interleaving other commands, which makes the pair indivisible.
    /// <para>
    /// The expiry is only set when the increment returns 1 — that is, on the first request of a
    /// new window. Refreshing it on every hit would turn the fixed window into a sliding one
    /// that never expires while the user keeps trying, which is the opposite of the intent.
    /// </para>
    /// </remarks>
    private const string IncrementScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
          redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return current
        """;

    public async ValueTask<bool> TryAcquireAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var redisKey = (RedisKey)$"nexus:ratelimit:{key}";

        try
        {
            var current = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                IncrementScript,
                [redisKey],
                [(RedisValue)(long)window.TotalMilliseconds]);

            return current <= permitLimit;
        }
        catch (Exception ex)
        {
            // Fall back to the in-process limiter rather than failing open. Letting the action
            // through because Redis is unreachable would remove the throttle from the one code
            // path that has no other protection, and a Redis outage is a plausible moment for
            // load to be abnormal. Limiting per instance is weaker than limiting globally, but
            // it is the same guarantee the app had before the backplane existed.
            logger.LogWarning(
                ex,
                "Redis rate limiter unavailable for key {Key}; falling back to the in-process limiter.",
                key);

            return await localFallback.TryAcquireAsync(key, permitLimit, window, cancellationToken);
        }
    }
}
