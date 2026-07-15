using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
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

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<IAuthorizationHandler, WorkspaceAuthorizationHandler>();
builder.Services.AddScoped<WorkItemQueryService>();
builder.Services.AddScoped<NavigationContextService>();
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

    // The container serves plain HTTP (see Dockerfile) — TLS termination happens at whatever
    // reverse proxy/load balancer sits in front once a host is chosen. ForwardedHeaders lets the
    // app see the original scheme from that proxy; UseHttpsRedirection is intentionally omitted
    // here since forcing a redirect inside the container (no cert bound) would break direct access.
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

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

app.Run();
