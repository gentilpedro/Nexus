using Nexus.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    // Persists ASP.NET Core's Data Protection key ring in the database instead of the container's
    // ephemeral filesystem, so auth cookies and antiforgery tokens survive a container restart/rebuild.
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<TaskList> TaskLists => Set<TaskList>();
    public DbSet<TaskStatusDefinition> TaskStatusDefinitions => Set<TaskStatusDefinition>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<SprintBurndownSnapshot> SprintBurndownSnapshots => Set<SprintBurndownSnapshot>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
