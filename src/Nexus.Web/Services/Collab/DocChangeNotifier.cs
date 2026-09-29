namespace Nexus.Web.Services.Collab;

public enum DocChangeKind
{
    /// <summary>Uma operação foi aceita: quem está editando busca o que falta.</summary>
    Operation,

    /// <summary>O HTML do modo leitura (ou o título) foi atualizado: quem está lendo recarrega.</summary>
    Content,

    /// <summary>Alguém entrou ou saiu do documento.</summary>
    Presence,
}

/// <summary>
/// Aviso de que um documento mudou. Leva só identificadores: quem recebe busca o que precisa no
/// banco, pelo mesmo motivo do backplane do chat (ver <c>ChatBackplaneEnvelope</c>).
/// </summary>
/// <param name="ClientId">Editor que causou a mudança, para ele não reagir ao próprio aviso.</param>
public readonly record struct DocChange(Guid DocPageId, long Revision, Guid ClientId, DocChangeKind Kind);

/// <summary>Leva um <see cref="DocChange"/> às outras instâncias do app.</summary>
/// <remarks>Nunca lança: a mudança já foi gravada, e perder o aviso entre instâncias só atrasa
/// quem está do outro lado até o próximo catch-up.</remarks>
public interface IDocChangePublisher
{
    Task PublishAsync(DocChange change, CancellationToken cancellationToken = default);
}

/// <summary>Instância única: não há ninguém a avisar.</summary>
public sealed class NullDocChangePublisher : IDocChangePublisher
{
    public Task PublishAsync(DocChange change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Espalha as mudanças de um documento para os circuitos abertos nele, nesta instância e — via
/// <see cref="IDocChangePublisher"/> — nas outras. Mesmo desenho do
/// <see cref="WorkspaceChatBroadcaster"/>: evento local primeiro e sempre, entrega entre
/// instâncias por cima, em melhor esforço.
/// </summary>
public sealed class DocChangeNotifier(IDocChangePublisher publisher, ILogger<DocChangeNotifier> logger)
{
    public event Action<DocChange>? Changed;

    public async Task PublishAsync(DocChange change, CancellationToken cancellationToken = default)
    {
        RaiseLocal(change);
        await publisher.PublishAsync(change, cancellationToken);
    }

    /// <summary>Mudança vinda de outra instância; não volta para o backplane.</summary>
    public void RaiseFromRemote(DocChange change) => RaiseLocal(change);

    private void RaiseLocal(DocChange change)
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        // Um circuito com problema não pode impedir os outros de receberem o aviso, nem
        // derrubar a gravação de quem editou.
        foreach (var handler in handlers.GetInvocationList().Cast<Action<DocChange>>())
        {
            try
            {
                handler(change);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao avisar um editor sobre a mudança no documento {DocPageId}.", change.DocPageId);
            }
        }
    }
}
