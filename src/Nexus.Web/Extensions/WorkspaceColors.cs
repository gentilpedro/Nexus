namespace Nexus.Web.Extensions;

/// <summary>
/// Cores que um workspace pode ter. Todas mantêm contraste de pelo menos 4,5:1 com a inicial
/// branca do avatar (WCAG AA). Workspaces antigos podem ter outra cor gravada — ela continua
/// sendo exibida, só não é mais oferecida como opção.
/// </summary>
public static class WorkspaceColors
{
    public static readonly (string Hex, string Name)[] Options =
    [
        ("#5B5CEB", "Índigo"),
        ("#2563EB", "Azul"),
        ("#0F766E", "Petróleo"),
        ("#15803D", "Verde"),
        ("#C2410C", "Laranja"),
        ("#BE185D", "Magenta"),
        ("#7C3AED", "Roxo")
    ];

    public static string Default => Options[0].Hex;
}
