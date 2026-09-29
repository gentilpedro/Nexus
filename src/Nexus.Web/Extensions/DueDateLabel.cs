using System.Globalization;

namespace Nexus.Web.Extensions;

/// <summary>
/// Prazo escrito como as pessoas falam: "Hoje", "Amanhã", "Ontem", "12 set" (e "12 set 2027"
/// fora do ano corrente). Compara só a data, em UTC — o mesmo critério que o resto do app usa
/// para decidir se algo está atrasado.
/// </summary>
public static class DueDateLabel
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public static string Format(DateTime dueUtc)
    {
        var due = dueUtc.Date;
        var today = DateTime.UtcNow.Date;
        var days = (due - today).Days;

        return days switch
        {
            0 => "Hoje",
            1 => "Amanhã",
            -1 => "Ontem",
            _ when due.Year == today.Year => due.ToString("d MMM", PtBr).Replace(".", ""),
            _ => due.ToString("d MMM yyyy", PtBr).Replace(".", "")
        };
    }

    /// <summary>Texto longo para leitor de tela e tooltip: "Prazo: 12 de setembro de 2026 (atrasada)".</summary>
    public static string Describe(DateTime dueUtc, bool done = false)
    {
        var text = $"Prazo: {dueUtc.Date.ToString("d 'de' MMMM 'de' yyyy", PtBr)}";
        return !done && IsOverdue(dueUtc) ? $"{text} (atrasada)" : text;
    }

    public static bool IsOverdue(DateTime dueUtc) => dueUtc.Date < DateTime.UtcNow.Date;

    public static bool IsDueSoon(DateTime dueUtc)
    {
        var days = (dueUtc.Date - DateTime.UtcNow.Date).Days;
        return days is >= 0 and <= 2;
    }
}
