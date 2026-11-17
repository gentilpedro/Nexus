using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.Domain.Entities;
using Nexus.Web.Services;

namespace Nexus.Web.Api;

public sealed record CheckinResponse(
    Guid Id,
    DateTime WeekStartUtc,
    CheckinPerception Perception,
    string Finished,
    string InProgress,
    string NextSteps,
    string Observations,
    string? AuthorName,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>Corpo de POST /api/v1/checkins. A semana é a da data informada (segunda a domingo).</summary>
public sealed record CheckinRequest(
    DateTime WeekDate,
    CheckinPerception Perception,
    string? Finished,
    string? InProgress,
    string? NextSteps,
    string? Observations);

// Check-ins pela API (#79): um painel externo lê a percepção da semana; um assistente redige o
// check-in a partir do quadro. Salvar na mesma semana edita o existente, como na tela.
public static class CheckinEndpoints
{
    public static RouteGroupBuilder MapCheckins(this RouteGroupBuilder api)
    {
        api.MapGet("/checkins", ListAsync)
            .WithTags("Check-in")
            .WithSummary("Lista os check-ins semanais")
            .WithDescription("Do mais recente para o mais antigo, até 26 semanas.");

        api.MapPost("/checkins", SaveAsync)
            .WithTags("Check-in")
            .WithSummary("Registra ou atualiza o check-in de uma semana")
            .WithDescription("Exige chave de leitura e escrita. perception: OnTrack, Frozen, OffTrack ou AtRisk. Pelo menos um de finished, inProgress ou nextSteps.");

        return api;
    }

    private static async Task<Ok<IReadOnlyList<CheckinResponse>>> ListAsync(ClaimsPrincipal user, CheckinService checkins, CancellationToken ct)
    {
        var list = await checkins.ListAsync(user.GetWorkspaceId(), ct: ct);
        IReadOnlyList<CheckinResponse> response = list.Select(ToResponse).ToList();
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<CheckinResponse>, Created<CheckinResponse>, ValidationProblem>> SaveAsync(
        CheckinRequest body, ClaimsPrincipal user, CheckinService checkins, CancellationToken ct)
    {
        var result = await checkins.SaveAsync(user.GetWorkspaceId(),
            new CheckinInput(body.WeekDate, body.Perception, body.Finished, body.InProgress, body.NextSteps, body.Observations),
            user.GetKeyCreatorUserId(), ct);
        return result.Status switch
        {
            CheckinSaveStatus.Created => TypedResults.Created($"{IntegrationApiEndpoints.RoutePrefix}/checkins/{result.Checkin!.Id}", ToResponse(result.Checkin)),
            CheckinSaveStatus.Updated => TypedResults.Ok(ToResponse(result.Checkin!)),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [""] = [result.Error ?? "Check-in inválido."] }),
        };
    }

    public static CheckinResponse ToResponse(WorkspaceCheckin c) => new(
        c.Id, c.WeekStartUtc, c.Perception, c.Finished, c.InProgress, c.NextSteps, c.Observations,
        c.Author is null ? null : ApiMappingNames.DisplayNameOf(c.Author), c.CreatedAtUtc, c.UpdatedAtUtc);
}
