using Nexus.Web.Services.Webhooks;

namespace Nexus.Web.BackgroundServices;

// Entrega os webhooks de saída (#75) a cada poucos segundos. Um lote por rodada; se o lote veio
// cheio, roda de novo sem esperar, para uma rajada de eventos não ficar presa ao intervalo.
public class WebhookDeliveryHostedService(IServiceScopeFactory scopeFactory, ILogger<WebhookDeliveryHostedService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                int sent;
                do
                {
                    using var scope = scopeFactory.CreateScope();
                    sent = await scope.ServiceProvider.GetRequiredService<WebhookSender>().ProcessDueAsync(stoppingToken);
                }
                while (sent >= WebhookSender.BatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A próxima rodada tenta de novo; o que não deve acontecer é o job morrer.
                logger.LogError(ex, "Falha na rodada de entrega de webhooks.");
            }
        }
    }
}
