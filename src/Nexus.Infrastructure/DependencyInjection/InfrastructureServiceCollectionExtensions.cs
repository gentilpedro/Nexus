using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Nexus.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        // Escape hatch for local development and tests, where no certificate exists and the host
        // may be Linux. Never set in production — the guard below throws instead of defaulting to
        // plaintext keys.
        var allowUnprotectedKeys = configuration.GetValue("DataProtection:AllowUnprotectedKeys", false);

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
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString, npgsql =>
        {
            // Retry transient failures. Npgsql's default execution strategy does NOT retry — it
            // only rewrites the exception message to say the failure "is likely due to a transient
            // failure", then rethrows. So a momentary network blip between the app and the
            // database surfaced as an error page. On shared hosting, where the app and the
            // database are separate services on a network the project does not control, that is
            // the single most likely runtime failure.
            //
            // 3 attempts with a 5s ceiling keeps a genuine outage from stacking retries into a
            // long hang (and from turning a struggling database into a retry storm).
            npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);

            // Bounds a single query. The Npgsql default is 30s; a query that slow here means
            // something is wrong, and holding the request (and its circuit) for half a minute
            // makes it worse.
            npgsql.CommandTimeout(15);
        }));
        services.AddScoped<AppDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        // Persists the Data Protection key ring in the database rather than the container's
        // ephemeral filesystem, so auth cookies / antiforgery tokens survive a container restart.
        //
        // SetApplicationName is pinned explicitly: it is part of the key derivation, and letting it
        // default to the content-root path means a host that changes that path (a new IIS physical
        // directory, a rebuilt container) silently invalidates every issued cookie.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Nexus")
            .PersistKeysToDbContext<AppDbContext>();

        // Encrypt the key ring at rest.
        //
        // Overriding the repository (PersistKeysToDbContext) also drops the key *encryptor* that
        // the default filesystem/registry discovery path would have wired up — so without an
        // explicit ProtectKeysWith* call the private keys sit in the DataProtectionKeys table as
        // plaintext XML. Anyone with read access to the database (a leaked connection string, a
        // restored backup, a SQL-injection-adjacent bug) could then forge authentication cookies
        // and antiforgery tokens for ANY user, including workspace Owners.
        //
        // Certificate first (works everywhere, survives a host move as long as the cert is kept),
        // DPAPI as the Windows-only fallback for the current IIS deployment. If neither is
        // available the app refuses to start rather than silently writing plaintext keys.
        var certPath = configuration["DataProtection:CertificatePath"];
        var certPassword = configuration["DataProtection:CertificatePassword"];

        if (!string.IsNullOrWhiteSpace(certPath))
        {
            var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader
                .LoadPkcs12FromFile(certPath, certPassword);
            dataProtection.ProtectKeysWithCertificate(certificate);
        }
        else if (OperatingSystem.IsWindows())
        {
            // Tied to the Windows machine key: readable only by this host, so a stolen database
            // dump alone is no longer enough to forge cookies.
            dataProtection.ProtectKeysWithDpapi();
        }
        else if (allowUnprotectedKeys)
        {
            // Local development / test only — opt-in and loud, never the implicit default.
        }
        else
        {
            throw new InvalidOperationException(
                "Data Protection keys would be stored unencrypted. Configure " +
                "'DataProtection:CertificatePath' (+ ':CertificatePassword'), run on Windows so DPAPI " +
                "can be used, or set 'DataProtection:AllowUnprotectedKeys' to true for local development only.");
        }

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                // Requires a confirmed e-mail before sign-in. This was previously false with the
                // note "no SMTP provider configured yet" — that stopped being true once Brevo was
                // wired up (BrevoEmailSender/BrevoMailer, used by four flows). Leaving it off let
                // anyone register using someone else's address and use the account immediately:
                // account pre-hijacking, plus Nexus becoming a spam relay against its own domain
                // reputation.
                //
                // Existing accounts predate confirmation e-mails and all have EmailConfirmed =
                // false; migration 'ConfirmExistingUsersAndAddPerformanceIndexes' backfills them
                // to true so this change does not lock out the current user base.
                options.SignIn.RequireConfirmedAccount = true;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;

                // Identity's defaults allow a 6-character password. Raised to 10 with the
                // complexity requirements kept — the app holds workspace-wide business data and
                // has no MFA enforcement, so the password is usually the only factor.
                options.Password.RequiredLength = 10;
                options.Password.RequiredUniqueChars = 4;

                // Defaults are 5 attempts / 5 minutes. Widening the window makes online password
                // guessing materially slower without affecting a user who simply mistyped.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return services;
    }
}
