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
        // AddDbContextFactory is what app code should use instead of injecting AppDbContext
        // directly: Blazor Server's DI scope lives for the whole SignalR circuit, so a
        // directly-injected AppDbContext accumulates tracked entities across an entire session —
        // this caused a real cascading-delete bug found during testing. The factory creates a
        // short-lived context per operation instead.
        //
        // ASP.NET Identity's EF store implementation still resolves AppDbContext directly (not via
        // a factory), so a Scoped AppDbContext registration is also needed — but calling
        // AddDbContext<AppDbContext>(...) separately alongside AddDbContextFactory<AppDbContext>(...)
        // makes both register DbContextOptions<AppDbContext> with conflicting lifetimes (a Singleton
        // factory ends up depending on a Scoped options instance), which only surfaces under strict
        // DI validation (e.g. `dotnet ef`), not normal `dotnet run`. The fix is to register the
        // factory once and derive the scoped AppDbContext from it.
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

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
