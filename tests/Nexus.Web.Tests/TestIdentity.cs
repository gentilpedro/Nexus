using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Tests;

// Um Identity de verdade (UserManager com os validadores padrão) sobre o mesmo banco em memória
// que o IDbContextFactory entrega aos serviços. Para os testes que criam contas pelo caminho
// oficial, como o login pelo Active Directory.
internal static class TestIdentity
{
    public sealed record Setup(IDbContextFactory<AppDbContext> Factory, UserManager<ApplicationUser> UserManager, ServiceProvider Services);

    public static Setup Create(Action<IServiceCollection>? configure = null)
    {
        var name = $"nexus-identity-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AppDbContext>(o => o
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)), ServiceLifetime.Scoped);
        services.AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
            .AddEntityFrameworkStores<AppDbContext>();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Setup(
            provider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            provider);
    }
}
