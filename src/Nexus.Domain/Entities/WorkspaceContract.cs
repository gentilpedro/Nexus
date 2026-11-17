namespace Nexus.Domain.Entities;

public enum ContractType
{
    // Cobra por hora apontada.
    Hourly = 0,

    // Valor fechado; as horas contratadas, quando informadas, servem só para acompanhar o consumo.
    Fixed = 1,
}

public enum ContractStatus
{
    Active = 0,

    // Não recebe hora nova (suspenso, em renegociação); o histórico continua contando.
    Locked = 1,

    // Terminou; sai das escolhas, fica nos relatórios.
    Closed = 2,
}

// Contrato de um workspace com o cliente (#77): quantas horas foram vendidas e até quando. Toda
// hora apontada pode ficar vinculada a um contrato; o saldo é contratado menos apontado.
public class WorkspaceContract
{
    public const int MaxNameLength = 120;
    public const int MaxNotesLength = 2000;

    // Percentual de consumo em que Owners e Admins são avisados, uma vez por contrato.
    public const int WarningPercent = 80;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Name { get; set; } = "";
    public ContractType Type { get; set; }
    public decimal? ContractedHours { get; set; }
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
    public ContractStatus Status { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? WarningNotifiedAtUtc { get; set; }

    public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();

    public bool AcceptsHours => Status == ContractStatus.Active;
}
