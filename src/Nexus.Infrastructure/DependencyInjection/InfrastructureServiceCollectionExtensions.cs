using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // AddDbContext is kept because ASP.NET Identity's EF store implementation resolves
        // AppDbContext directly (not via a factory). AddDbContextFactory is what app code should
        // use instead of injecting AppDbContext directly: Blazor Server's DI scope lives for the
        // whole SignalR circuit, so a directly-injected AppDbContext accumulates tracked entities
        // across an entire session — this caused a real cascading-delete bug found during testing.
        // The factory creates a short-lived context per operation instead.
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlServer(connectionString));

        // Persists the Data Protection key ring in the database rather than the container's
        // ephemeral filesystem, so auth cookies / antiforgery tokens survive a container restart.
        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // MVP: no SMTP provider configured yet, small trusted team — see plan.
                options.SignIn.RequireConfirmedAccount = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return services;
    }
}
