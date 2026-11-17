namespace Nexus.Domain.Entities;

public enum TimeEntrySource
{
    Manual = 0,
    Timer = 1,
}

// Tipo de trabalho de um lançamento (#78): Desenvolvimento, Reunião, Suporte... Lista por
// workspace, que nasce com os padrões e o Admin ajusta.
public class TimeActivity
{
    public const int MaxNameLength = 60;

    public static readonly string[] DefaultNames = ["Desenvolvimento", "Reunião", "Suporte", "Análise", "Outra"];

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// Horas trabalhadas (#78): de uma pessoa, num dia, numa tarefa ou avulsas no workspace, com
// atividade e, quando o workspace tem contrato, o contrato que recebe as horas.
public class TimeEntry
{
    public const int MaxCommentLength = 500;
    public const decimal MaxHoursPerEntry = 24m;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    // De quem são as horas.
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    public Guid? ContractId { get; set; }
    public WorkspaceContract? Contract { get; set; }

    public Guid? ActivityId { get; set; }
    public TimeActivity? Activity { get; set; }

    // Só a data (meia-noite UTC): o dia em que o trabalho foi feito, no calendário de quem apontou.
    public DateTime Date { get; set; }
    public decimal Hours { get; set; }
    public string? Comment { get; set; }
    public TimeEntrySource Source { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

// O timer que uma pessoa deixou rodando (#78). Um por pessoa; parar vira um TimeEntry.
public class RunningTimer
{
    public const int MaxNoteLength = 200;

    public Guid Id { get; set; }

    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    public Guid WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public Guid? WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }

    public Guid? ActivityId { get; set; }
    public string? Note { get; set; }
    public DateTime StartedAtUtc { get; set; }
}
