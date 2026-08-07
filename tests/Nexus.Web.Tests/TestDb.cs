using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Tests;

// Builds a throwaway in-memory AppDbContext (one uniquely-named store per call, so tests never
// see each other's rows) plus the IDbContextFactory<AppDbContext> that every service and page in
// this app actually takes as a dependency.
internal static class TestDb
{
    public static IDbContextFactory<AppDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"nexus-tests-{Guid.NewGuid()}")
            // The in-memory provider has no transaction support; several services under test call
            // SaveChangesAsync more than once per operation, which would otherwise warn.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new PooledFactory(options);
    }

    private sealed class PooledFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
