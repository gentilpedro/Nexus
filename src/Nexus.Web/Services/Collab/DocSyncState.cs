namespace Nexus.Web.Services.Collab;

/// <summary>Estado da sincronização do editor colaborativo, como a página mostra.</summary>
/// <param name="Status"><c>synced</c>, <c>syncing</c>, <c>offline</c> ou <c>error</c>.</param>
/// <param name="Pending">Alterações ainda não confirmadas pelo servidor.</param>
/// <param name="Message">Motivo, quando <paramref name="Status"/> é <c>error</c> ou <c>offline</c>.</param>
/// <param name="Recovery">Texto que a pessoa tinha no editor quando as pendências não puderam mais
/// ser juntadas ao documento, para ela copiar.</param>
public sealed record DocSyncState(string Status, int Pending, string? Message, string? Recovery)
{
    public static DocSyncState Initial { get; } = new("syncing", 0, null, null);

    public bool IsError => Status == "error";
}
