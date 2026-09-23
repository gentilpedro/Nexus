namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Throttles actions that travel over the Blazor circuit rather than as HTTP requests.
/// </summary>
/// <remarks>
/// An interface so the window counter can live either in this process or in Redis. With more
/// than one instance and a per-process counter, the effective limit is multiplied by the number
/// of instances — a user whose circuit lands on a different server after a reconnect simply gets
/// a fresh allowance, which is exactly the abuse the limiter exists to stop.
/// </remarks>
public interface ICircuitActionRateLimiter
{
    /// <summary>
    /// Records an attempt and reports whether it is allowed.
    /// </summary>
    /// <param name="key">Partition key — a user id, optionally combined with the action name.</param>
    /// <param name="permitLimit">Attempts allowed per window.</param>
    /// <param name="window">Window length.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    ValueTask<bool> TryAcquireAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
