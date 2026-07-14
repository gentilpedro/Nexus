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
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

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
