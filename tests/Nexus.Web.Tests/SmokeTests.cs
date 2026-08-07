using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;

namespace Nexus.Web.Tests;

// Proves the in-memory harness can actually materialize the real AppDbContext model before any
// other test relies on it — the model carries Npgsql-specific mapping (the xmin row-version on
// WorkItem), so this failing would invalidate every DB-backed test below.
public class SmokeTests
{
    [Fact]
    public async Task InMemoryContext_CanRoundTripAWorkspace()
    {
        var factory = TestDb.CreateFactory();
        var id = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            db.Workspaces.Add(new Workspace { Id = id, Name = "Acme", Slug = "acme" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal("Acme", (await db.Workspaces.SingleAsync(w => w.Id == id, TestContext.Current.CancellationToken)).Name);
        }
    }
}
