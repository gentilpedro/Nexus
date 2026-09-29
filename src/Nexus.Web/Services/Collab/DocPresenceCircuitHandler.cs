using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Nexus.Web.Services.Collab;

/// <summary>
/// Liga a presença nos Docs à conexão do circuito, não à vida do componente.
/// </summary>
/// <remarks>
/// <para>
/// Quando a aba fecha ou a rede cai, o Blazor mantém o circuito vivo no servidor por alguns
/// minutos esperando a reconexão, e os componentes só são descartados no fim desse prazo. Sem
/// isto, o editor seguiria renovando a presença nesse intervalo e a pessoa continuaria aparecendo
/// como "editando" para os outros minutos depois de ter saído.
/// </para>
/// <para>
/// Com a conexão caída, as presenças deste circuito saem na hora e a renovação para; se a mesma
/// conexão voltar (reconexão do circuito), elas entram de novo.
/// </para>
/// </remarks>
public sealed class DocPresenceCircuitHandler(IDocPresenceStore store, DocChangeNotifier notifier) : CircuitHandler
{
    private readonly Dictionary<Guid, (Guid DocPageId, DocEditor Editor)> sessions = [];
    private readonly Lock gate = new();

    /// <summary>Falso entre a queda da conexão e a reconexão: a renovação espera.</summary>
    public bool IsConnected { get; private set; } = true;

    public void Register(Guid sessionId, Guid docPageId, DocEditor editor)
    {
        lock (gate)
        {
            sessions[sessionId] = (docPageId, editor);
        }
    }

    public void Unregister(Guid sessionId)
    {
        lock (gate)
        {
            sessions.Remove(sessionId);
        }
    }

    public override async Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        IsConnected = false;
        foreach (var (sessionId, (docPageId, _)) in Snapshot())
        {
            await store.LeaveAsync(docPageId, sessionId, cancellationToken);
            await notifier.PublishAsync(new DocChange(docPageId, 0, Guid.Empty, DocChangeKind.Presence), cancellationToken);
        }
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var wasDown = !IsConnected;
        IsConnected = true;
        if (!wasDown)
        {
            return; // primeira conexão: os editores anunciam a própria presença ao abrir
        }
        foreach (var (sessionId, (docPageId, editor)) in Snapshot())
        {
            await store.JoinAsync(docPageId, sessionId, editor, cancellationToken);
            await notifier.PublishAsync(new DocChange(docPageId, 0, Guid.Empty, DocChangeKind.Presence), cancellationToken);
        }
    }

    private List<KeyValuePair<Guid, (Guid DocPageId, DocEditor Editor)>> Snapshot()
    {
        lock (gate)
        {
            return [.. sessions];
        }
    }
}
