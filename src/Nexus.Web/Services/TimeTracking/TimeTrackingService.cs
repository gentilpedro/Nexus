using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.TimeTracking;

public sealed record TimeEntryInput(
    DateTime Date,
    decimal Hours,
    Guid? WorkItemId = null,
    Guid? ActivityId = null,
    Guid? ContractId = null,
    string? Comment = null);

public enum TimeEntryStatus
{
    Ok,
    Invalid,
    NotFound,
    Forbidden,
}

public sealed record TimeEntryResult(TimeEntryStatus Status, TimeEntry? Entry = null, string? Error = null);

/// <param name="AlreadyRunning">Preenchido quando a pessoa já tinha um timer; nada foi iniciado.</param>
public sealed record TimerStartResult(RunningTimer? Timer, RunningTimer? AlreadyRunning);

public enum TimeReportGroup
{
    Person,
    List,
    Contract,
    Activity,
    Day,
}

public sealed record TimeReportRow(string Label, decimal Hours, int Entries);

public sealed record TimeReport(IReadOnlyList<TimeReportRow> Rows, decimal TotalHours, IReadOnlyList<TimeEntry> Entries);

// Apontamento de horas (#78): timer, lançamento manual, totais por tarefa e relatório por
// período. Toda hora passa por LogAsync, que aplica as mesmas regras venha da tela, do timer, da
// API ou do MCP: horas entre 0,01 e 24, data plausível, tarefa e atividade do workspace, e o
// contrato pela regra do ContractService.
public class TimeTrackingService(
    IDbContextFactory<AppDbContext> dbFactory,
    ContractService contracts,
    AuditLogService audit,
    TimeProvider? timeProvider = null)
{
    public const int MaxDaysInPast = 366;
    public const int MaxDaysInFuture = 1;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    // ---- Atividades ----

    public async Task<List<TimeActivity>> ListActivitiesAsync(Guid workspaceId, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureDefaultActivitiesAsync(db, workspaceId, ct);
        return await db.TimeActivities.AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId && (includeInactive || a.IsActive))
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);
    }

    public async Task<TimeActivity?> AddActivityAsync(Guid workspaceId, string? name, CancellationToken ct = default)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > TimeActivity.MaxNameLength)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureDefaultActivitiesAsync(db, workspaceId, ct);
        var next = await db.TimeActivities.Where(a => a.WorkspaceId == workspaceId).Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? -1;
        var activity = new TimeActivity { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = name, SortOrder = next + 1, IsActive = true };
        db.TimeActivities.Add(activity);
        await db.SaveChangesAsync(ct);
        return activity;
    }

    public async Task<bool> SetActivityActiveAsync(Guid workspaceId, Guid activityId, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var activity = await db.TimeActivities.FirstOrDefaultAsync(a => a.Id == activityId && a.WorkspaceId == workspaceId, ct);
        if (activity is null)
        {
            return false;
        }

        activity.IsActive = active;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task EnsureDefaultActivitiesAsync(AppDbContext db, Guid workspaceId, CancellationToken ct)
    {
        if (await db.TimeActivities.AnyAsync(a => a.WorkspaceId == workspaceId, ct))
        {
            return;
        }

        var order = 0;
        foreach (var name in TimeActivity.DefaultNames)
        {
            db.TimeActivities.Add(new TimeActivity { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = name, SortOrder = order++, IsActive = true });
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- Lançamentos ----

    public async Task<TimeEntryResult> LogAsync(Guid workspaceId, string userId, TimeEntryInput input, TimeEntrySource source = TimeEntrySource.Manual, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct))
        {
            return new TimeEntryResult(TimeEntryStatus.Forbidden, Error: "Só membros do workspace apontam horas nele.");
        }

        var validated = await ValidateAsync(db, workspaceId, input, ct);
        if (validated.Error is not null)
        {
            return new TimeEntryResult(TimeEntryStatus.Invalid, Error: validated.Error);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var entry = new TimeEntry
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            WorkItemId = input.WorkItemId,
            ActivityId = input.ActivityId,
            ContractId = validated.ContractId,
            Date = validated.Date,
            Hours = validated.Hours,
            Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim(),
            Source = source,
            CreatedAtUtc = now,
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        if (entry.ContractId is { } contractId)
        {
            await contracts.CheckWarningAsync(db, contractId, ct);
            await db.SaveChangesAsync(ct);
        }

        return new TimeEntryResult(TimeEntryStatus.Ok, await LoadAsync(db, entry.Id, ct));
    }

    public async Task<TimeEntryResult> UpdateAsync(Guid workspaceId, Guid entryId, string actorUserId, bool actorIsAdmin, TimeEntryInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.TimeEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.WorkspaceId == workspaceId, ct);
        if (entry is null)
        {
            return new TimeEntryResult(TimeEntryStatus.NotFound);
        }

        if (entry.UserId != actorUserId && !actorIsAdmin)
        {
            return new TimeEntryResult(TimeEntryStatus.Forbidden, Error: "Só quem apontou, ou um administrador, altera o lançamento.");
        }

        // Um lançamento já vinculado pode continuar no contrato mesmo que ele tenha sido travado
        // depois: o que não pode é hora nova entrar lá.
        var validated = await ValidateAsync(db, workspaceId, input, ct, keepContractId: entry.ContractId);
        if (validated.Error is not null)
        {
            return new TimeEntryResult(TimeEntryStatus.Invalid, Error: validated.Error);
        }

        var previousContract = entry.ContractId;
        entry.WorkItemId = input.WorkItemId;
        entry.ActivityId = input.ActivityId;
        entry.ContractId = validated.ContractId;
        entry.Date = validated.Date;
        entry.Hours = validated.Hours;
        entry.Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        entry.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        foreach (var contractId in new[] { entry.ContractId, previousContract }.Where(c => c is not null).Distinct())
        {
            await contracts.CheckWarningAsync(db, contractId!.Value, ct);
        }

        await db.SaveChangesAsync(ct);
        return new TimeEntryResult(TimeEntryStatus.Ok, await LoadAsync(db, entry.Id, ct));
    }

    public async Task<TimeEntryStatus> DeleteAsync(Guid workspaceId, Guid entryId, string actorUserId, bool actorIsAdmin, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.TimeEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.WorkspaceId == workspaceId, ct);
        if (entry is null)
        {
            return TimeEntryStatus.NotFound;
        }

        if (entry.UserId != actorUserId && !actorIsAdmin)
        {
            return TimeEntryStatus.Forbidden;
        }

        db.TimeEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        if (entry.UserId != actorUserId)
        {
            await audit.LogAsync(actorUserId, AuditAction.TimeEntryDeletedByAdmin, entry.Id.ToString(), $"{entry.Hours:0.##} h de {entry.UserId} em {entry.Date:yyyy-MM-dd}, workspace {workspaceId}", ct);
        }

        return TimeEntryStatus.Ok;
    }

    public async Task<List<TimeEntry>> ListAsync(Guid workspaceId, string? userId, DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        var from = AsUtcDate(fromDate);
        var to = AsUtcDate(toDate);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Query(db)
            .Where(e => e.WorkspaceId == workspaceId && e.Date >= from && e.Date <= to && (userId == null || e.UserId == userId))
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<TimeEntry>> ListForTaskAsync(Guid workItemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Query(db)
            .Where(e => e.WorkItemId == workItemId)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<decimal> TotalForTaskAsync(Guid workItemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TimeEntries.Where(e => e.WorkItemId == workItemId).SumAsync(e => e.Hours, ct);
    }

    public async Task<TimeReport> ReportAsync(Guid workspaceId, DateTime fromDate, DateTime toDate, TimeReportGroup groupBy, string? onlyUserId = null, CancellationToken ct = default)
    {
        var entries = await ListAsync(workspaceId, onlyUserId, fromDate, toDate, ct);
        var rows = entries
            .GroupBy(e => GroupLabel(e, groupBy))
            .Select(g => new TimeReportRow(g.Key, g.Sum(e => e.Hours), g.Count()))
            .OrderByDescending(r => r.Hours).ThenBy(r => r.Label)
            .ToList();
        return new TimeReport(rows, entries.Sum(e => e.Hours), entries);
    }

    public static string GroupLabel(TimeEntry entry, TimeReportGroup groupBy) => groupBy switch
    {
        TimeReportGroup.Person => Services.ApiMappingNames.DisplayNameOf(entry.User),
        TimeReportGroup.List => entry.WorkItem?.TaskList?.Name ?? "Avulso (sem tarefa)",
        TimeReportGroup.Contract => entry.Contract?.Name ?? "Sem contrato",
        TimeReportGroup.Activity => entry.Activity?.Name ?? "Sem atividade",
        _ => entry.Date.ToString("dd/MM/yyyy"),
    };

    // ---- Timer ----

    public async Task<RunningTimer?> GetRunningAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.RunningTimers.AsNoTracking()
            .Include(t => t.WorkItem)
            .Include(t => t.Workspace)
            .FirstOrDefaultAsync(t => t.UserId == userId, ct);
    }

    public async Task<TimerStartResult> StartAsync(string userId, Guid workspaceId, Guid? workItemId, Guid? activityId, string? note, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var running = await db.RunningTimers.AsNoTracking().Include(t => t.WorkItem).FirstOrDefaultAsync(t => t.UserId == userId, ct);
        if (running is not null)
        {
            return new TimerStartResult(null, running);
        }

        if (!await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct))
        {
            return new TimerStartResult(null, null);
        }

        if (workItemId is { } itemId && !await db.WorkItems.AnyAsync(w => w.Id == itemId && w.TaskList.Space.WorkspaceId == workspaceId, ct))
        {
            return new TimerStartResult(null, null);
        }

        var timer = new RunningTimer
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            WorkItemId = workItemId,
            ActivityId = activityId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, RunningTimer.MaxNoteLength)],
            StartedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.RunningTimers.Add(timer);
        await db.SaveChangesAsync(ct);
        return new TimerStartResult(timer, null);
    }

    /// <summary>
    /// Para o timer e grava o lançamento. Com dois ou mais contratos ativos e nenhum escolhido,
    /// devolve Invalid e deixa o timer rodando, para a tela perguntar.
    /// </summary>
    public async Task<TimeEntryResult> StopAsync(string userId, Guid? contractId = null, Guid? activityId = null, string? comment = null, CancellationToken ct = default)
    {
        RunningTimer? timer;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            timer = await db.RunningTimers.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId, ct);
        }

        if (timer is null)
        {
            return new TimeEntryResult(TimeEntryStatus.NotFound, Error: "Nenhum timer rodando.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var hours = HoursBetween(timer.StartedAtUtc, now);
        var result = await LogAsync(timer.WorkspaceId, userId,
            new TimeEntryInput(now, hours, timer.WorkItemId, activityId ?? timer.ActivityId, contractId, comment ?? timer.Note),
            TimeEntrySource.Timer, ct);
        if (result.Status != TimeEntryStatus.Ok)
        {
            return result;
        }

        await DiscardTimerAsync(userId, ct);
        return result;
    }

    public async Task DiscardTimerAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (db.Database.IsRelational())
        {
            await db.RunningTimers.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
            return;
        }

        // Provedor em memória (testes): sem ExecuteDelete.
        db.RunningTimers.RemoveRange(db.RunningTimers.Where(t => t.UserId == userId));
        await db.SaveChangesAsync(ct);
    }

    // Minutos inteiros, no mínimo um: um clique rápido não vira "0 h", e 7 min viram 0,12 h.
    public static decimal HoursBetween(DateTime startUtc, DateTime endUtc)
    {
        var minutes = Math.Max(1, (int)Math.Round((endUtc - startUtc).TotalMinutes));
        return Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);
    }

    // ---- Comum ----

    private sealed record Validated(string? Error, DateTime Date = default, decimal Hours = 0, Guid? ContractId = null);

    private async Task<Validated> ValidateAsync(AppDbContext db, Guid workspaceId, TimeEntryInput input, CancellationToken ct, Guid? keepContractId = null)
    {
        var hours = Math.Round(input.Hours, 2, MidpointRounding.AwayFromZero);
        if (hours <= 0 || hours > TimeEntry.MaxHoursPerEntry)
        {
            return new Validated("Informe as horas: mais que zero e até 24 por lançamento.");
        }

        var date = AsUtcDate(input.Date);
        var today = AsUtcDate(clock.GetUtcNow().UtcDateTime);
        if (date < today.AddDays(-MaxDaysInPast) || date > today.AddDays(MaxDaysInFuture))
        {
            return new Validated("A data precisa estar entre um ano atrás e amanhã.");
        }

        if (input.WorkItemId is { } itemId && !await db.WorkItems.AnyAsync(w => w.Id == itemId && w.TaskList.Space.WorkspaceId == workspaceId, ct))
        {
            return new Validated("Tarefa não encontrada neste workspace.");
        }

        if (input.ActivityId is { } activityId && !await db.TimeActivities.AnyAsync(a => a.Id == activityId && a.WorkspaceId == workspaceId, ct))
        {
            return new Validated("Atividade não encontrada neste workspace.");
        }

        Guid? contractId;
        if (input.ContractId is { } chosen)
        {
            var contract = await db.WorkspaceContracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chosen && c.WorkspaceId == workspaceId, ct);
            if (contract is null)
            {
                return new Validated("Contrato não encontrado neste workspace.");
            }

            if (!contract.AcceptsHours && chosen != keepContractId)
            {
                return new Validated($"O contrato \"{contract.Name}\" está {(contract.Status == ContractStatus.Locked ? "travado" : "encerrado")} e não recebe horas.");
            }

            contractId = chosen;
        }
        else
        {
            var choice = await contracts.ChoiceAsync(db, workspaceId, null, ct);
            if (choice.MustChoose)
            {
                return new Validated("Este workspace tem mais de um contrato ativo. Escolha qual recebe as horas.");
            }

            contractId = choice.DefaultContractId;
        }

        return new Validated(null, date, hours, contractId);
    }

    private static IQueryable<TimeEntry> Query(AppDbContext db) => db.TimeEntries.AsNoTracking()
        .Include(e => e.User)
        .Include(e => e.WorkItem).ThenInclude(w => w!.TaskList)
        .Include(e => e.Contract)
        .Include(e => e.Activity);

    private static Task<TimeEntry> LoadAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        Query(db).FirstAsync(e => e.Id == id, ct);

    public static DateTime AsUtcDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
