using Nexus.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Nexus.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<TaskList> TaskLists => Set<TaskList>();
    public DbSet<TaskStatusDefinition> TaskStatusDefinitions => Set<TaskStatusDefinition>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
