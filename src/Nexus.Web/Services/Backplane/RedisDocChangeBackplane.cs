using Nexus.Web.Services.Collab;
using StackExchange.Redis;

namespace Nexus.Web.Services.Backplane;

/// <summary>
/// Publica as mudanças de documentos para as outras instâncias, no canal dos Docs.
/// </summary>
public sealed class RedisDocChangePublisher(
    IConnectionMultiplexer redis,
    NexusInstance instance,
    ILogger<RedisDocChangePublisher> logger) : IDocChangePublisher
{
    /// <summary>Um canal para todos os documentos; quem recebe filtra (ver
    /// <see cref="RedisChatMessagePublisher.Channel"/>).</summary>
    public static readonly RedisChannel Channel = RedisChannel.Literal("nexus:docs:changed");

    public async Task PublishAsync(DocChange change, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetSubscriber().PublishAsync(Channel, new DocChangeEnvelope(instance.Id, change).ToString());
        }
        catch (Exception ex)
        {
            // A mudança já está gravada e já foi avisada nesta instância. Sem o Redis, quem está
            // em outra instância vê na próxima vez que o editor dele buscar o que falta.
            logger.LogWarning(ex, "Não foi possível avisar as outras instâncias sobre o documento {DocPageId}.", change.DocPageId);
        }
    }
}

/// <summary>
/// Recebe as mudanças de documentos feitas em outras instâncias e avisa os editores conectados
/// aqui.
/// </summary>
public sealed class RedisDocChangeSubscriber(
    IConnectionMultiplexer redis,
    NexusInstance instance,
    DocChangeNotifier notifier,
    ILogger<RedisDocChangeSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();

        // Fila, e não callback, pelo mesmo motivo do chat: os avisos chegam na ordem publicada.
        var queue = await subscriber.SubscribeAsync(RedisDocChangePublisher.Channel);
        queue.OnMessage(message =>
        {
            try
            {
                if (!DocChangeEnvelope.TryParse(message.Message, out var envelope))
                {
                    logger.LogWarning("Aviso de documento ilegível descartado.");
                    return;
                }
                // O próprio aviso, devolvido pelo Redis: já foi dado localmente.
                if (envelope.InstanceId != instance.Id)
                {
                    notifier.RaiseFromRemote(envelope.Change);
                }
            }
            catch (Exception ex)
            {
                // Uma exceção aqui faria a fila parar de entregar sem ninguém perceber.
                logger.LogError(ex, "Falha ao tratar um aviso de documento do backplane.");
            }
        });

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await subscriber.UnsubscribeAsync(RedisDocChangePublisher.Channel);
        }
    }
}
