using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;
using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Busca sem diferenciar maiúsculas e acentos (#60). Aqui, no provedor em memória;
/// <see cref="SearchTextPostgresTests"/> cobre o ILIKE + unaccent do PostgreSQL.
/// </summary>
public class SearchTextTests
{
    [Theory]
    [InlineData("Especificação", "especificacao")]
    [InlineData("PAGAMENTO por Pix", "pagamento por pix")]
    [InlineData("Ação à vista, coração", "acao a vista, coracao")]
    [InlineData(null, "")]
    public void Fold_LowercasesAndDropsAccents(string? input, string expected)
    {
        Assert.Equal(expected, SearchText.Fold(input));
    }

    internal static async Task<(WorkItemQueryService Service, string UserId)> SeedTasksAsync(IDbContextFactory<AppDbContext> factory, Guid workspaceId, params string[] titles)
    {
        var userId = "u-" + workspaceId.ToString("N");
        var spaceId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        await using var db = factory.CreateDbContext();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = userId, Email = userId + "@example.com" });
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Busca", Slug = "busca-" + workspaceId.ToString("N")[..8], CreatedAtUtc = DateTime.UtcNow });
        db.WorkspaceMembers.Add(new WorkspaceMember { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = userId, Role = WorkspaceRole.Owner, JoinedAtUtc = DateTime.UtcNow });
        db.Spaces.Add(new Space { Id = spaceId, WorkspaceId = workspaceId, Name = "S" });
        db.TaskLists.Add(new TaskList { Id = listId, SpaceId = spaceId, Name = "L" });
        db.TaskStatusDefinitions.Add(new TaskStatusDefinition { Id = statusId, TaskListId = listId, Name = "A fazer", Category = StatusCategory.ToDo });
        var number = 0;
        foreach (var title in titles)
        {
            db.WorkItems.Add(new WorkItem { Id = Guid.NewGuid(), Number = ++number, TaskListId = listId, StatusId = statusId, Title = title, CreatedAtUtc = DateTime.UtcNow });
        }

        await db.SaveChangesAsync(ct);
        return (new WorkItemQueryService(factory), userId);
    }

    internal static async Task<List<string>> SearchAsync(WorkItemQueryService service, string userId, Guid workspaceId, string text)
    {
        var found = await service.SearchWorkItemsAsync(userId, new WorkItemSearchFilters { WorkspaceId = workspaceId, Text = text }, TestContext.Current.CancellationToken);
        return found.Select(w => w.Title).Order().ToList();
    }

    [Theory]
    [InlineData("pagamento", new[] { "Pagamento por Pix com QR Code" })]
    [InlineData("PIX", new[] { "Pagamento por Pix com QR Code" })]
    [InlineData("especificacao", new[] { "Especificação do checkout" })]
    [InlineData("ESPECIFICAÇÃO", new[] { "Especificação do checkout" })]
    [InlineData("100%", new[] { "Cobertura de 100% dos testes" })]
    [InlineData("_", new string[0])]
    public async Task Tasks_IgnoreCaseAndAccents(string text, string[] expected)
    {
        var workspaceId = Guid.NewGuid();
        var (service, userId) = await SeedTasksAsync(TestDb.CreateFactory(), workspaceId,
            "Pagamento por Pix com QR Code", "Especificação do checkout", "Cobertura de 100% dos testes");

        Assert.Equal(expected.Order(), await SearchAsync(service, userId, workspaceId, text));
    }
}
