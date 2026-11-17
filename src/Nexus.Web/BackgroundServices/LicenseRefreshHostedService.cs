using Nexus.Web.Services.Licensing;

namespace Nexus.Web.BackgroundServices;

/// <summary>
/// Reavalia a licença de hora em hora (#81): o relógio anda, a contagem de usuários ativos muda e
/// uma chave trocada por License:KeyFile passa a valer sem reiniciar. A avaliação inicial já
/// aconteceu em Program.cs, antes das migrations.
/// </summary>
public sealed class LicenseRefreshHostedService(LicenseService license) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!license.IsEnforced)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await license.RefreshAsync(stoppingToken);
        }
    }
}
