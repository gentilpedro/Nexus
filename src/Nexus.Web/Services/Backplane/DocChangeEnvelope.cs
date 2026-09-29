using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Nexus.Web.Services.Collab;

namespace Nexus.Web.Services.Backplane;

/// <summary>
/// O que cruza entre instâncias quando um documento muda: qual instância avisou e o
/// <see cref="DocChange"/>, que já é só identificadores.
/// </summary>
/// <remarks>
/// Mesmo raciocínio do <see cref="ChatBackplaneEnvelope"/>: nada de conteúdo no fio. Quem recebe
/// um aviso de operação busca o que falta pelo catch-up, que já sabe tratar ordem, buraco e poda;
/// mandar a operação junto criaria um segundo caminho de entrega para manter coerente com o
/// primeiro.
/// </remarks>
public readonly record struct DocChangeEnvelope(Guid InstanceId, DocChange Change)
{
    private const char Separator = '|';

    /// <summary>Formato: instância|documento|revisão|cliente|tipo.</summary>
    public override string ToString() => string.Join(Separator,
        InstanceId.ToString("N"),
        Change.DocPageId.ToString("N"),
        Change.Revision.ToString(CultureInfo.InvariantCulture),
        Change.ClientId.ToString("N"),
        ((int)Change.Kind).ToString(CultureInfo.InvariantCulture));

    /// <summary>Lê o formato; recusa qualquer coisa malformada em vez de lançar (ver
    /// <see cref="ChatBackplaneEnvelope.TryParse"/>).</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out DocChangeEnvelope envelope)
    {
        envelope = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(Separator);
        if (parts.Length != 5
            || !Guid.TryParse(parts[0], out var instanceId)
            || !Guid.TryParse(parts[1], out var docPageId)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || !Guid.TryParse(parts[3], out var clientId)
            || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var kind)
            || !Enum.IsDefined(typeof(DocChangeKind), kind))
        {
            return false;
        }

        envelope = new DocChangeEnvelope(instanceId, new DocChange(docPageId, revision, clientId, (DocChangeKind)kind));
        return true;
    }
}
