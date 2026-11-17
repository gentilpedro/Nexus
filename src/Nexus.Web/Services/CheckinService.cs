using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services;

public sealed record CheckinInput(
    DateTime WeekDate,
    CheckinPerception Perception,
    string? Finished,
    string? InProgress,
    string? NextSteps,
    string? Observations);

public enum CheckinSaveStatus
{
    Created,
    Updated,
    Invalid,
}

public sealed record CheckinSaveResult(CheckinSaveStatus Status, WorkspaceCheckin? Checkin = null, string? Error = null);

/// <summary>Rascunho sugerido para a semana, a partir do quadro.</summary>
public sealed record CheckinDraft(string Finished, string InProgress);

// Check-in semanal (#79). Uma semana, um registro: salvar na mesma semana edita em vez de
// duplicar, que é a regra do ritual. Owner e Admin escrevem; todo membro lê.
public class CheckinService(IDbContextFactory<AppDbContext> dbFactory, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<List<WorkspaceCheckin>> ListAsync(Guid workspaceId, int take = 26, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceCheckins.AsNoTracking()
            .Include(c => c.Author)
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderByDescending(c => c.WeekStartUtc)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<WorkspaceCheckin?> GetForWeekAsync(Guid workspaceId, DateTime weekDate, CancellationToken ct = default)
    {
        var week = WorkspaceCheckin.WeekStartOf(weekDate);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceCheckins.AsNoTracking().Include(c => c.Author)
            .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId && c.WeekStartUtc == week, ct);
    }

    /// <summary>A percepção mais recente de cada workspace, para o semáforo da lista e do cabeçalho.</summary>
    public async Task<Dictionary<Guid, WorkspaceCheckin>> LatestByWorkspaceAsync(IEnumerable<Guid> workspaceIds, CancellationToken ct = default)
    {
        var ids = workspaceIds.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.WorkspaceCheckins.AsNoTracking()
            .Where(c => ids.Contains(c.WorkspaceId))
            .ToListAsync(ct);
        return rows.GroupBy(c => c.WorkspaceId).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.WeekStartUtc).First());
    }

    public async Task<CheckinSaveResult> SaveAsync(Guid workspaceId, CheckinInput input, string? authorId, CancellationToken ct = default)
    {
        var week = WorkspaceCheckin.WeekStartOf(input.WeekDate);
        var today = clock.GetUtcNow().UtcDateTime.Date;
        if (week > WorkspaceCheckin.WeekStartOf(today))
        {
            return new CheckinSaveResult(CheckinSaveStatus.Invalid, Error: "Não dá para registrar o check-in de uma semana que ainda não começou.");
        }

        if (!Enum.IsDefined(input.Perception))
        {
            return new CheckinSaveResult(CheckinSaveStatus.Invalid, Error: "Escolha uma das quatro percepções.");
        }

        var texts = new[] { input.Finished, input.InProgress, input.NextSteps, input.Observations }.Select(t => t?.Trim() ?? "").ToArray();
        if (texts.Any(t => t.Length > WorkspaceCheckin.MaxTextLength))
        {
            return new CheckinSaveResult(CheckinSaveStatus.Invalid, Error: "Cada campo tem no máximo 4000 caracteres.");
        }

        if (texts.Take(3).All(t => t.Length == 0))
        {
            return new CheckinSaveResult(CheckinSaveStatus.Invalid, Error: "Escreva pelo menos o que foi concluído, o que está em andamento ou os próximos passos.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.WorkspaceCheckins.FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId && c.WeekStartUtc == week, ct);
        var checkin = existing ?? new WorkspaceCheckin { Id = Guid.NewGuid(), WorkspaceId = workspaceId, WeekStartUtc = week, CreatedAtUtc = now };
        checkin.Perception = input.Perception;
        checkin.Finished = texts[0];
        checkin.InProgress = texts[1];
        checkin.NextSteps = texts[2];
        checkin.Observations = texts[3];
        checkin.AuthorId = authorId;
        if (existing is null)
        {
            db.WorkspaceCheckins.Add(checkin);
        }
        else
        {
            checkin.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
        return new CheckinSaveResult(existing is null ? CheckinSaveStatus.Created : CheckinSaveStatus.Updated, checkin);
    }

    /// <summary>
    /// Rascunho da semana a partir do quadro: o que foi para Feito nesta semana e o que está em
    /// andamento agora. Para editar, não para postar sem ler.
    /// </summary>
    public async Task<CheckinDraft> DraftAsync(Guid workspaceId, DateTime weekDate, CancellationToken ct = default)
    {
        var week = WorkspaceCheckin.WeekStartOf(weekDate);
        var weekEnd = week.AddDays(7);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.WorkItems.AsNoTracking()
            .Where(w => w.TaskList.Space.WorkspaceId == workspaceId)
            .Where(w => w.Status.Category == StatusCategory.InProgress
                        || (w.Status.Category == StatusCategory.Done && (w.UpdatedAtUtc ?? w.CreatedAtUtc) >= week && (w.UpdatedAtUtc ?? w.CreatedAtUtc) < weekEnd))
            .Select(w => new { w.Number, w.Title, Category = w.Status.Category, w.Assignee })
            .OrderBy(w => w.Number)
            .Take(60)
            .ToListAsync(ct);

        string Line(dynamic w) => $"- {WorkItem.FormatCode(w.Number)} {w.Title}" + (w.Assignee is null ? "" : $" ({ApiMappingNames.DisplayNameOf(w.Assignee)})");
        var finished = string.Join("\n", items.Where(w => w.Category == StatusCategory.Done).Select(w => Line(w)));
        var inProgress = string.Join("\n", items.Where(w => w.Category == StatusCategory.InProgress).Select(w => Line(w)));
        return new CheckinDraft(finished, inProgress);
    }
}
