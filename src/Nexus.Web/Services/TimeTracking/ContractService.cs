using Microsoft.EntityFrameworkCore;
using Nexus.Domain.Entities;
using Nexus.Infrastructure.Data;

namespace Nexus.Web.Services.TimeTracking;

/// <param name="HoursLogged">Soma dos lançamentos vinculados, qualquer situação do contrato.</param>
public sealed record ContractUsage(WorkspaceContract Contract, decimal HoursLogged)
{
    public decimal? Remaining => Contract.ContractedHours is { } total ? total - HoursLogged : null;

    public int? Percent => Contract.ContractedHours is { } total && total > 0
        ? (int)Math.Min(999, Math.Round(HoursLogged * 100 / total))
        : null;

    public bool IsNearLimit => Percent is { } p && p >= WorkspaceContract.WarningPercent;
}

public sealed record ContractInput(
    string? Name,
    ContractType Type,
    decimal? ContractedHours,
    DateTime? StartDateUtc,
    DateTime? EndDateUtc,
    string? Notes);

public enum ContractSaveStatus
{
    Ok,
    Invalid,
    NotFound,
}

public sealed record ContractSaveResult(ContractSaveStatus Status, WorkspaceContract? Contract = null, string? Error = null);

/// <summary>Contratos que podem receber hora agora, e qual entra sozinho.</summary>
/// <param name="DefaultContractId">Null quando não há contrato (hora sem vínculo) ou quando há mais de um e ninguém escolheu.</param>
public sealed record ContractChoice(IReadOnlyList<WorkspaceContract> Options, Guid? DefaultContractId)
{
    public bool MustChoose => Options.Count > 1 && DefaultContractId is null;
}

// Contratos do workspace (#77). A regra de vínculo da hora é a de consultoria: sem contrato,
// sem vínculo; um ativo, vincula sozinho; dois ou mais, quem aponta escolhe. Travado ou
// encerrado não recebe hora nova, para a hora não "nascer órfã" num contrato que não vale.
public class ContractService(
    IDbContextFactory<AppDbContext> dbFactory,
    AuditLogService audit,
    TimeProvider? timeProvider = null)
{
    public const decimal MaxContractedHours = 100_000m;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<List<ContractUsage>> ListAsync(Guid workspaceId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var contracts = await db.WorkspaceContracts.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId)
            .ToListAsync(ct);
        var hours = await db.TimeEntries.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId && e.ContractId != null)
            .GroupBy(e => e.ContractId!.Value)
            .Select(g => new { ContractId = g.Key, Hours = g.Sum(e => e.Hours) })
            .ToDictionaryAsync(g => g.ContractId, g => g.Hours, ct);

        return contracts
            .OrderBy(c => c.Status)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new ContractUsage(c, hours.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<ContractSaveResult> CreateAsync(Guid workspaceId, ContractInput input, string? actorUserId, CancellationToken ct = default)
    {
        if (Validate(input) is { } error)
        {
            return new ContractSaveResult(ContractSaveStatus.Invalid, Error: error);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var contract = new WorkspaceContract
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Status = ContractStatus.Active,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        Apply(contract, input);
        db.WorkspaceContracts.Add(contract);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(actorUserId, AuditAction.ContractCreated, contract.Id.ToString(), $"{contract.Name} no workspace {workspaceId}", ct);
        return new ContractSaveResult(ContractSaveStatus.Ok, contract);
    }

    public async Task<ContractSaveResult> UpdateAsync(Guid workspaceId, Guid contractId, ContractInput input, string? actorUserId, CancellationToken ct = default)
    {
        if (Validate(input) is { } error)
        {
            return new ContractSaveResult(ContractSaveStatus.Invalid, Error: error);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var contract = await db.WorkspaceContracts.FirstOrDefaultAsync(c => c.Id == contractId && c.WorkspaceId == workspaceId, ct);
        if (contract is null)
        {
            return new ContractSaveResult(ContractSaveStatus.NotFound);
        }

        Apply(contract, input);
        contract.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        // Horas contratadas aumentaram: o aviso de 80% pode valer de novo mais tarde.
        if (contract.ContractedHours is { } total && contract.WarningNotifiedAtUtc is not null)
        {
            var logged = await db.TimeEntries.Where(e => e.ContractId == contract.Id).SumAsync(e => e.Hours, ct);
            if (total > 0 && logged * 100 / total < WorkspaceContract.WarningPercent)
            {
                contract.WarningNotifiedAtUtc = null;
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync(actorUserId, AuditAction.ContractUpdated, contract.Id.ToString(), $"{contract.Name} no workspace {workspaceId}", ct);
        return new ContractSaveResult(ContractSaveStatus.Ok, contract);
    }

    public async Task<bool> SetStatusAsync(Guid workspaceId, Guid contractId, ContractStatus status, string? actorUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var contract = await db.WorkspaceContracts.FirstOrDefaultAsync(c => c.Id == contractId && c.WorkspaceId == workspaceId, ct);
        if (contract is null)
        {
            return false;
        }

        contract.Status = status;
        contract.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(actorUserId, AuditAction.ContractUpdated, contract.Id.ToString(), $"{contract.Name}: {status} no workspace {workspaceId}", ct);
        return true;
    }

    /// <summary>Apaga o contrato; as horas vinculadas ficam, sem contrato.</summary>
    public async Task<bool> DeleteAsync(Guid workspaceId, Guid contractId, string? actorUserId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var contract = await db.WorkspaceContracts.FirstOrDefaultAsync(c => c.Id == contractId && c.WorkspaceId == workspaceId, ct);
        if (contract is null)
        {
            return false;
        }

        db.WorkspaceContracts.Remove(contract);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(actorUserId, AuditAction.ContractDeleted, contract.Id.ToString(), $"{contract.Name} no workspace {workspaceId}", ct);
        return true;
    }

    /// <summary>Os contratos ativos do workspace e qual entra sozinho num lançamento novo.</summary>
    public async Task<ContractChoice> ChoiceAsync(Guid workspaceId, Guid? preferredContractId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await ChoiceAsync(db, workspaceId, preferredContractId, ct);
    }

    public async Task<ContractChoice> ChoiceAsync(AppDbContext db, Guid workspaceId, Guid? preferredContractId, CancellationToken ct = default)
    {
        var options = await db.WorkspaceContracts.AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId && c.Status == ContractStatus.Active)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return new ContractChoice(options, ResolveDefault(options, preferredContractId));
    }

    public static Guid? ResolveDefault(IReadOnlyList<WorkspaceContract> activeOptions, Guid? preferredContractId) => activeOptions.Count switch
    {
        0 => null,
        1 => activeOptions[0].Id,
        _ => preferredContractId is { } preferred && activeOptions.Any(c => c.Id == preferred) ? preferred : null,
    };

    /// <summary>
    /// Depois de um lançamento: passou de 80% do contratado, avisa Owners e Admins uma vez.
    /// Só adiciona ao contexto; quem chama salva.
    /// </summary>
    public async Task CheckWarningAsync(AppDbContext db, Guid contractId, CancellationToken ct = default)
    {
        var contract = await db.WorkspaceContracts.FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null || contract.ContractedHours is not { } total || total <= 0 || contract.WarningNotifiedAtUtc is not null)
        {
            return;
        }

        var logged = await db.TimeEntries.Where(e => e.ContractId == contractId).SumAsync(e => e.Hours, ct);
        var percent = logged * 100 / total;
        if (percent < WorkspaceContract.WarningPercent)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        contract.WarningNotifiedAtUtc = now;
        var admins = await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == contract.WorkspaceId && (m.Role == WorkspaceRole.Owner || m.Role == WorkspaceRole.Admin))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        foreach (var userId in admins)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = NotificationType.ContractAlmostUsed,
                Message = Notification.TruncateMessage($"O contrato \"{contract.Name}\" já usou {Math.Round(percent)}% das {total:0.##} horas contratadas ({logged:0.##} h apontadas)."),
                WorkspaceId = contract.WorkspaceId,
                LinkUrl = $"/workspaces/{contract.WorkspaceId}/hours",
                IsRead = false,
                CreatedAtUtc = now,
            });
        }
    }

    private static string? Validate(ContractInput input)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > WorkspaceContract.MaxNameLength)
        {
            return "Dê um nome ao contrato, com até 120 caracteres.";
        }

        if (input.ContractedHours is { } hours && (hours <= 0 || hours > MaxContractedHours))
        {
            return "Horas contratadas precisam ser um número maior que zero.";
        }

        if (input.Type == ContractType.Hourly && input.ContractedHours is null)
        {
            return "Um contrato por hora precisa das horas contratadas.";
        }

        if (input.StartDateUtc is { } start && input.EndDateUtc is { } end && start > end)
        {
            return "O início precisa ser antes do fim.";
        }

        if ((input.Notes?.Trim().Length ?? 0) > WorkspaceContract.MaxNotesLength)
        {
            return "Observações com no máximo 2000 caracteres.";
        }

        return null;
    }

    private static void Apply(WorkspaceContract contract, ContractInput input)
    {
        contract.Name = input.Name!.Trim();
        contract.Type = input.Type;
        contract.ContractedHours = input.ContractedHours is { } h ? Math.Round(h, 2) : null;
        contract.StartDateUtc = AsUtcDate(input.StartDateUtc);
        contract.EndDateUtc = AsUtcDate(input.EndDateUtc);
        contract.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
    }

    public static DateTime? AsUtcDate(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc);
}
