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
builder.Services.AddScoped<WorkItemQueryService>();
builder.Services.AddScoped<NavigationContextService>();
builder.Services.AddScoped<NotificationBadgeService>();
builder.Services.AddSingleton<WorkspaceChatBroadcaster>();
builder.Services.AddSingleton<AttachmentStorageService>();
builder.Services.AddHostedService<SprintSnapshotHostedService>();
builder.Services.AddHostedService<DueDateNotificationHostedService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

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
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("APPLY_MIGRATIONS_ON_STARTUP"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Nexus.Infrastructure.Data.AppDbContext>();
    db.Database.Migrate();
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
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Baseline security headers — not a substitute for the framework protections already in
// place (antiforgery, HttpOnly/Secure auth cookie), but cheap defense-in-depth against
// clickjacking and MIME-sniffing that costs nothing to add.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseAntiforgery();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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
    UserManager<ApplicationUser> userManager,
    AttachmentStorageService storage) =>
{
    var user = await userManager.FindByIdAsync(userId.ToString());
    if (user?.AvatarStoragePath is null)
    {
        return Results.NotFound();
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
