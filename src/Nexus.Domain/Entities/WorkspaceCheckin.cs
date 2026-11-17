namespace Nexus.Domain.Entities;

// Exatamente quatro valores, como no ritual que inspirou (#79). Persistido como inteiro.
public enum CheckinPerception
{
    OnTrack = 0,
    Frozen = 1,
    OffTrack = 2,
    AtRisk = 3,
}

// Check-in semanal do workspace (#79): o que a gestão (e o cliente) lê sem abrir o quadro. Um por
// semana; postar de novo na mesma semana edita o existente.
public class WorkspaceCheckin
{
    public const int MaxTextLength = 4000;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    // Segunda-feira da semana, meia-noite UTC.
    public DateTime WeekStartUtc { get; set; }
    public CheckinPerception Perception { get; set; }
    public string Finished { get; set; } = "";
    public string InProgress { get; set; } = "";
    public string NextSteps { get; set; } = "";
    public string Observations { get; set; } = "";

    public string? AuthorId { get; set; }
    public ApplicationUser? Author { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public static DateTime WeekStartOf(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return DateTime.SpecifyKind(date.Date.AddDays(-offset), DateTimeKind.Utc);
    }
}

public static class CheckinPerceptionLabels
{
    public static string Emoji(this CheckinPerception p) => p switch
    {
        CheckinPerception.OnTrack => "✅",
        CheckinPerception.Frozen => "❄️",
        CheckinPerception.OffTrack => "⚡",
        _ => "☣️",
    };

    public static string Label(this CheckinPerception p) => p switch
    {
        CheckinPerception.OnTrack => "No planejado",
        CheckinPerception.Frozen => "Congelado",
        CheckinPerception.OffTrack => "Fora do planejado",
        _ => "Sob risco",
    };
}
