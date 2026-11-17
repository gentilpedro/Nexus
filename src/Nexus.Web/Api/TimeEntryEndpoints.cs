using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.Domain.Entities;
using Nexus.Web.Services;
using Nexus.Web.Services.TimeTracking;

namespace Nexus.Web.Api;

public sealed record TimeEntryResponse(
    Guid Id,
    string UserId,
    string UserName,
    Guid? TaskId,
    string? TaskCode,
    Guid? ContractId,
    string? ContractName,
    Guid? ActivityId,
    string? ActivityName,
    DateTime Date,
    decimal Hours,
    string? Comment,
    TimeEntrySource Source,
    DateTime CreatedAtUtc);

/// <summary>Corpo de POST /api/v1/time-entries.</summary>
/// <param name="UserId">De quem são as horas. Obrigatório: a chave é do workspace, não de uma pessoa.</param>
public sealed record TimeEntryRequest(
    string? UserId,
    DateTime Date,
    decimal Hours,
    Guid? TaskId = null,
    Guid? ActivityId = null,
    Guid? ContractId = null,
    string? Comment = null);

public sealed class TimeEntryQuery
{
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string? UserId { get; init; }
    public Guid? TaskId { get; init; }
    public Guid? ContractId { get; init; }
}

// Horas pela API (#78): um ERP que já controla o ponto manda os lançamentos para o Nexus, ou lê
// os apontados aqui para faturar. Mesmas regras do serviço; a pessoa precisa ser membro.
public static class TimeEntryEndpoints
{
    public static RouteGroupBuilder MapTimeEntries(this RouteGroupBuilder api)
    {
        api.MapGet("/time-entries", ListAsync)
            .WithTags("Horas")
            .WithSummary("Lista lançamentos de horas")
            .WithDescription("Por período (from/to, padrão: mês atual), pessoa, tarefa e contrato.");

        api.MapPost("/time-entries", CreateAsync)
            .WithTags("Horas")
            .WithSummary("Registra horas de um membro")
            .WithDescription("userId é obrigatório (veja GET /members). Contrato: sem informar, segue a regra do workspace (um ativo vincula sozinho; dois ou mais exigem contractId).");

        return api;
    }

    private static async Task<Ok<IReadOnlyList<TimeEntryResponse>>> ListAsync(
        [AsParameters] TimeEntryQuery query, ClaimsPrincipal user, TimeTrackingService time, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var from = query.From ?? new DateTime(today.Year, today.Month, 1);
        var to = query.To ?? from.AddMonths(1).AddDays(-1);
        var entries = await time.ListAsync(user.GetWorkspaceId(), query.UserId, from, to, ct);
        IReadOnlyList<TimeEntryResponse> response = entries
            .Where(e => query.TaskId is null || e.WorkItemId == query.TaskId)
            .Where(e => query.ContractId is null || e.ContractId == query.ContractId)
            .Select(ToResponse)
            .ToList();
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Created<TimeEntryResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        TimeEntryRequest body, ClaimsPrincipal user, TimeTrackingService time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.UserId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["userId"] = ["Informe de quem são as horas (id de GET /members)."] });
        }

        var result = await time.LogAsync(user.GetWorkspaceId(), body.UserId.Trim(),
            new TimeEntryInput(body.Date, body.Hours, body.TaskId, body.ActivityId, body.ContractId, body.Comment), ct: ct);
        return result.Status switch
        {
            TimeEntryStatus.Ok => TypedResults.Created($"{IntegrationApiEndpoints.RoutePrefix}/time-entries/{result.Entry!.Id}", ToResponse(result.Entry)),
            TimeEntryStatus.Forbidden => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Pessoa fora do workspace", detail: result.Error),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = [result.Error ?? "Lançamento inválido."] }),
        };
    }

    public static TimeEntryResponse ToResponse(TimeEntry e) => new(
        e.Id, e.UserId, ApiMappingNames.DisplayNameOf(e.User),
        e.WorkItemId, e.WorkItem?.Code,
        e.ContractId, e.Contract?.Name,
        e.ActivityId, e.Activity?.Name,
        e.Date, e.Hours, e.Comment, e.Source, e.CreatedAtUtc);
}
