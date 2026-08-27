using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.DependencyInjection;
using Nexus.Web.Authorization;
using Nexus.Web.BackgroundServices;
using Nexus.Web.Components;
using Nexus.Web.Components.Account;
using Nexus.Web.Services;

// Container HEALTHCHECK support (see Dockerfile). The dotnet/aspnet base image ships neither
// curl nor wget, and installing one purely to probe an HTTP endpoint would add packages to the
// runtime image for no other reason. Instead the app probes itself: `--healthcheck` performs a
// single GET against its own /health/live and exits 0 (healthy) or 1 (unhealthy), which is
// exactly the contract Docker expects from a HEALTHCHECK command.
if (args.Contains("--healthcheck"))
{
    try
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORT") ?? "8080";
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var probeResponse = await probe.GetAsync($"http://localhost:{port}/health/live");
        return probeResponse.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Default SignalR hub message size (~32KB) is too small for file attachment uploads —
// raised to comfortably fit AttachmentStorageService.MaxSizeBytes plus framing overhead.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 11 * 1024 * 1024;
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthorizationCore(options =>
{
    // Deny-by-default: any route without an explicit [Authorize]/[AllowAnonymous] now
    // requires an authenticated user, instead of quietly defaulting to public. Every
    // genuinely public page (landing, login/register/forgot-password flow, error/404)
    // is marked [AllowAnonymous] explicitly — see those files for the full list.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceAuthorizationHandler>();
builder.Services.AddScoped<WorkspaceAccessGuard>();
builder.Services.AddScoped<WorkItemQueryService>();
builder.Services.AddScoped<NavigationContextService>();
builder.Services.AddScoped<NotificationBadgeService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddSingleton<WorkspaceChatBroadcaster>();
builder.Services.AddSingleton<CircuitActionRateLimiter>();
builder.Services.AddSingleton<AttachmentStorageService>();
builder.Services.AddHostedService<SprintSnapshotHostedService>();
builder.Services.AddHostedService<DueDateNotificationHostedService>();
builder.Services.AddHostedService<DataRetentionHostedService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// Explicit hardening of the Identity.Application cookie — without this, SecurePolicy
// defaults to SameAsRequest, so the session cookie could be issued/accepted over a plain
// HTTP request if ForwardedHeaders ever misreports the original scheme (misconfigured
// proxy, direct health-check hit). Always is safe here since HTTPS is enforced end-to-end
// in every environment this app runs in (HSTS + reverse-proxy TLS termination).
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    // Stated explicitly rather than inherited. Lax is already Identity's default and is the right
    // value here (Strict would drop the cookie when a user arrives from an invite link in their
    // e-mail, logging them out at the worst moment), but relying on a framework default for a
    // CSRF-relevant setting means a future default change alters this app's security silently.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;

    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddInfrastructure(connectionString, builder.Configuration);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Health checks. There was no way to ask this app whether it was actually working — a monitor
// could only see that the port answered, which it does even when the database is unreachable and
// every page is failing.
//
// /health/live  — process is up (no dependencies touched); safe for a restart probe.
// /health/ready — can actually reach PostgreSQL; what an uptime monitor or load balancer wants.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<Nexus.Infrastructure.Data.AppDbContext>(
        name: "database",
        tags: ["ready"]);

builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection("Brevo"));
// Named client rather than a typed one: BrevoMailer is a singleton, and a singleton typed
// client pins one HttpMessageHandler for the life of the process, so it never picks up DNS
// changes. The factory rotates handlers underneath.
builder.Services.AddHttpClient(BrevoMailer.HttpClientName);
builder.Services.AddSingleton<BrevoMailer>();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, BrevoEmailSender>();
builder.Services.AddScoped<PublicUrlBuilder>();
builder.Services.AddScoped<WorkspaceInviteService>();

// Login/Register/ForgotPassword are static SSR pages (not interactive — see
// AcceptsInteractiveRouting in App.razor), so their form posts are real discrete HTTP
// requests this middleware can see and throttle, unlike most of the app which runs over a
// persistent SignalR circuit. Keyed by client IP (post-ForwardedHeaders in production) so
// one abusive client can't lock out everyone else sharing the limiter.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Separate, more permissive bucket for /health/ready — it must stay reachable by monitors but
    // touches the database on every call, so it cannot be left completely unbounded.
    options.AddPolicy("health", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("APPLY_MIGRATIONS_ON_STARTUP"))
{
    using var scope = app.Services.CreateScope();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<Nexus.Infrastructure.Data.AppDbContext>();

    // Wrapped so a failed migration is reported as a failed migration. Previously any exception
    // here surfaced as an opaque startup crash with no indication that the schema was the cause,
    // and — because migrations run unattended on every deploy — that is exactly the failure most
    // likely to happen at the worst moment. Logging the pending list first also leaves a record
    // of what a given deploy actually applied.
    try
    {
        var pending = db.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
        {
            migrationLogger.LogWarning(
                "Applying {Count} pending EF Core migration(s) on startup: {Migrations}. " +
                "Ensure a database backup exists — see docs/backup-restore.md.",
                pending.Count, string.Join(", ", pending));
        }

        db.Database.Migrate();

        if (pending.Count > 0)
        {
            migrationLogger.LogWarning("Applied {Count} migration(s) successfully.", pending.Count);
        }
    }
    catch (Exception ex)
    {
        migrationLogger.LogCritical(ex, "Startup migration failed. The application will not start.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();

    // TLS termination happens in front of the app (IIS on the MonsterASP.net host, or a
    // reverse proxy/load balancer on any other host) — the app itself never binds a cert.
    // ForwardedHeaders lets it see the original scheme from that front door; UseHttpsRedirection
    // is intentionally omitted since the front door already owns the HTTP->HTTPS decision.
    //
    // ForwardLimit = 1: only the single reverse proxy directly in front of this app may set the
    // client IP. Without a limit, a client that sends its own X-Forwarded-For gets that value
    // appended and can spoof the address the rate limiter partitions on — letting one attacker
    // present as unlimited distinct clients and bypass the login throttle entirely.
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };

    // Which peers may set X-Forwarded-*.
    //
    // KnownIPNetworks/KnownProxies default to loopback only, which silently discards the headers
    // when the proxy is on another host. Clearing them unconditionally is the tempting fix, but it
    // means trusting whatever peer happens to be on the other end of the socket — and if the app
    // is ever reachable without a proxy in front, that peer is the client, who can then forge
    // X-Forwarded-For and defeat the per-IP login rate limiter by presenting as unlimited
    // distinct addresses.
    //
    // So: trust exactly what is configured. KNOWN_PROXIES (comma-separated IPs) names the front
    // door explicitly; with nothing configured the defaults stand and only loopback is trusted,
    // which is the correct behaviour for IIS in-process hosting, where ASP.NET Core Module already
    // supplies the real client IP and these headers are not needed at all.
    var knownProxies = builder.Configuration["KNOWN_PROXIES"];
    if (!string.IsNullOrWhiteSpace(knownProxies))
    {
        forwardedHeaders.KnownIPNetworks.Clear();
        forwardedHeaders.KnownProxies.Clear();

        foreach (var proxy in knownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (System.Net.IPAddress.TryParse(proxy, out var address))
            {
                forwardedHeaders.KnownProxies.Add(address);
            }
        }
    }

    app.UseForwardedHeaders(forwardedHeaders);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Baseline security headers — not a substitute for the framework protections already in
// place (antiforgery, HttpOnly/Secure auth cookie), but cheap defense-in-depth against
// clickjacking and MIME-sniffing that costs nothing to add.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.Append("X-Content-Type-Options", "nosniff");
    headers.Append("X-Frame-Options", "DENY");
    headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    // Content-Security-Policy. The app renders user-authored HTML on doc pages as a raw
    // MarkupString (Doc.razor), so HtmlSanitizer is the primary XSS defense and this is the
    // second line: even if something slips past the sanitizer, an injected <script> has no way
    // to execute and no origin it is allowed to exfiltrate to.
    //
    // script-src deliberately has no 'unsafe-inline' — the one inline script this app had (the
    // theme bootstrap) was moved to wwwroot/js/theme-interop.js precisely so this could be
    // strict. style-src does keep 'unsafe-inline': Quill and Chart.js both set element styles
    // at runtime, and Blazor injects inline styles of its own.
    //
    // connect-src must allow ws:/wss: — Blazor Server's entire interactive layer is a SignalR
    // WebSocket back to this same origin.
    headers.Append("Content-Security-Policy", string.Join("; ",
        "default-src 'self'",
        "script-src 'self'",
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data:",
        "font-src 'self' data:",
        "connect-src 'self' ws: wss:",
        "form-action 'self'",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        "object-src 'none'"));

    // Denies access to device APIs this app never uses, so a successful injection cannot reach
    // for them either.
    headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");

    await next();
});

app.UseAntiforgery();
app.UseRateLimiter();

// AllowAnonymous is required here despite MapStaticAssets serving public files: without it,
// the global FallbackPolicy (RequireAuthenticatedUser) applies to these endpoints too, and an
// anonymous visitor hitting the landing page gets every CSS/JS/image request redirected to
// /Account/Login — the page silently renders unstyled instead of failing loudly.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Anonymous by design: a monitor must be able to poll these without credentials. They return only
// a status word, never dependency names, connection strings, or exception detail.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Rate-limited: unlike /health/live this one opens a database connection on every call, so an
// unauthenticated caller could otherwise use it to hammer the connection pool. Stays anonymous
// because an uptime monitor has to reach it without credentials, but 12/minute per IP is far more
// than any monitor needs and far less than a useful amplifier.
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous().RequireRateLimiting("health");

// Qual build está no ar. Responde a pergunta "o deploy realmente subiu?" sem depender de olhar o
// log do Actions, e serve de âncora ao investigar um bug: o commit exato que gerou este binário.
//
// Anônimo e sem rate limit, ao contrário de /health/ready: os valores são constantes lidas de
// atributos do assembly na inicialização, então a resposta é mais barata que servir o favicon —
// não há pool de conexões nem nada a proteger. A versão também já aparece no rodapé de qualquer
// página pública, então o endpoint não revela nada novo.
app.MapGet("/version", () => Results.Json(new
{
    version = AppVersion.Current.Version,
    commit = AppVersion.Current.Commit,
    builtAt = AppVersion.Current.BuiltAt
})).AllowAnonymous();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapGet("/attachments/{id:guid}/download", async (
    Guid id,
    HttpContext http,
    IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> dbFactory,
    IAuthorizationService authorizationService,
    AttachmentStorageService storage) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var attachment = await db.WorkItemAttachments
        .Include(a => a.WorkItem).ThenInclude(w => w.TaskList).ThenInclude(l => l.Space)
        .FirstOrDefaultAsync(a => a.Id == id);

    if (attachment is null)
    {
        return Results.NotFound();
    }

    var workspaceId = attachment.WorkItem.TaskList.Space.WorkspaceId;
    var authResult = await authorizationService.AuthorizeAsync(
        http.User, workspaceId, new WorkspaceAccessRequirement(WorkspaceRole.Member));

    if (!authResult.Succeeded)
    {
        // Not Results.Forbid() — that redirects to the login page under cookie auth,
        // which would download an HTML page instead of a 403 for this raw API route.
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var fullPath = storage.GetFullPath(attachment.StoragePath);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    return Results.File(fullPath, attachment.ContentType, attachment.FileName);
}).RequireAuthorization();

app.MapGet("/chat-attachments/{id:guid}/download", async (
    Guid id,
    HttpContext http,
    IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> dbFactory,
    IAuthorizationService authorizationService,
    AttachmentStorageService storage) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var attachment = await db.ChatMessageAttachments
        .Include(a => a.ChatMessage)
        .FirstOrDefaultAsync(a => a.Id == id);

    if (attachment is null)
    {
        return Results.NotFound();
    }

    var authResult = await authorizationService.AuthorizeAsync(
        http.User, attachment.ChatMessage.WorkspaceId, new WorkspaceAccessRequirement(WorkspaceRole.Member));

    if (!authResult.Succeeded)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var fullPath = storage.GetFullPath(attachment.StoragePath);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    return Results.File(fullPath, attachment.ContentType, attachment.FileName);
}).RequireAuthorization();

app.MapGet("/avatars/{userId:guid}/download", async (
    Guid userId,
    HttpContext http,
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> dbFactory,
    AttachmentStorageService storage) =>
{
    var user = await userManager.FindByIdAsync(userId.ToString());
    if (user?.AvatarStoragePath is null)
    {
        return Results.NotFound();
    }

    // Avatars are personal data (LGPD art. 5, I) — gate them the same way as every other
    // attachment endpoint: only someone who shares a workspace with the owner (or the owner
    // themselves) can fetch it, not just "any authenticated user in the whole system".
    var requesterId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (requesterId != userId.ToString())
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sharesWorkspace = await db.WorkspaceMembers
            .Where(m => m.UserId == requesterId)
            .Select(m => m.WorkspaceId)
            .Intersect(db.WorkspaceMembers.Where(m => m.UserId == userId.ToString()).Select(m => m.WorkspaceId))
            .AnyAsync();

        if (!sharesWorkspace)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    var fullPath = storage.GetFullPath(user.AvatarStoragePath);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    return Results.File(fullPath, user.AvatarContentType ?? "application/octet-stream");
}).RequireAuthorization();

app.MapGet("/spreadsheets/{id:guid}/download.xlsx", async (
    Guid id,
    HttpContext http,
    IDbContextFactory<Nexus.Infrastructure.Data.AppDbContext> dbFactory,
    IAuthorizationService authorizationService) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var doc = await db.DocPages.FirstOrDefaultAsync(d => d.Id == id && d.Type == DocPageType.Spreadsheet);

    if (doc is null)
    {
        return Results.NotFound();
    }

    var authResult = await authorizationService.AuthorizeAsync(
        http.User, doc.WorkspaceId, new WorkspaceAccessRequirement(WorkspaceRole.Member));

    if (!authResult.Succeeded)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var grid = System.Text.Json.JsonSerializer.Deserialize<List<List<string>>>(doc.GridDataJson ?? "[]") ?? [];
    var bytes = SpreadsheetExportService.BuildXlsx(doc.Title, grid);

    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{doc.Title}.xlsx");
}).RequireAuthorization();

app.Run();

// Explicit exit code: the `--healthcheck` branch above returns an int, so every path must.
return 0;
