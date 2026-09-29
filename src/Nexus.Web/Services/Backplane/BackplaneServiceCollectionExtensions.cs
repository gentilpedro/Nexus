using Nexus.Web.Services.Collab;
using StackExchange.Redis;

namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Wires the chat fan-out and the circuit rate limiter to either this process alone or to a
/// Redis backplane shared by every instance.
/// </summary>
public static class BackplaneServiceCollectionExtensions
{
    /// <summary>Configuration key holding the Redis connection string. Absent means single instance.</summary>
    public const string ConnectionStringKey = "Redis:ConnectionString";

    /// <summary>
    /// Registers the chat broadcaster and the circuit rate limiter, backed by Redis when
    /// <c>Redis:ConnectionString</c> is configured.
    /// </summary>
    /// <remarks>
    /// Absence of configuration selects the single-instance implementations, which is what the
    /// current production deployment (one IIS site) runs. Multi-instance is opt-in: an operator
    /// who has not provisioned Redis gets exactly the previous behaviour instead of a startup
    /// failure telling them about infrastructure they do not have.
    /// </remarks>
    public static IServiceCollection AddChatBackplane(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<NexusInstance>();

        // Registered in both modes: on its own when there is no Redis, and as the fallback the
        // Redis limiter uses when the connection is down.
        services.AddSingleton<CircuitActionRateLimiter>();
        services.AddSingleton<WorkspaceChatBroadcaster>();
        services.AddSingleton<DocChangeNotifier>();

        var connectionString = configuration[ConnectionStringKey];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddSingleton<IChatMessagePublisher, NullChatMessagePublisher>();
            services.AddSingleton<IDocChangePublisher, NullDocChangePublisher>();
            services.AddSingleton<ICircuitActionRateLimiter>(
                sp => sp.GetRequiredService<CircuitActionRateLimiter>());
            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);

            // Without this, Connect throws when Redis is not reachable at startup and the whole
            // app fails to boot — turning a cache outage into a total outage. With it, the
            // multiplexer starts disconnected and reconnects on its own; publishes fail in the
            // meantime and are swallowed by design (see RedisChatMessagePublisher), and the rate
            // limiter falls back to its in-process counter.
            options.AbortOnConnectFail = false;

            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IChatMessagePublisher, RedisChatMessagePublisher>();
        // Os avisos dos Docs ainda não cruzam instâncias; cada uma vê só os próprios editores
        // até o catch-up (#42).
        services.AddSingleton<IDocChangePublisher, NullDocChangePublisher>();
        services.AddSingleton<ICircuitActionRateLimiter, RedisCircuitActionRateLimiter>();
        services.AddHostedService<RedisChatSubscriber>();

        return services;
    }
}
