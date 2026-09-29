namespace Nexus.Domain.Collab;

/// <summary>Resultado de pedir ao servidor que aceite uma alteração.</summary>
public abstract record SubmitDecision
{
    private SubmitDecision() { }

    /// <summary>Aceita: o documento passa a <paramref name="Document"/> na <paramref name="Revision"/>.</summary>
    public sealed record Accepted(Delta Document, long Revision) : SubmitDecision;

    /// <summary>
    /// O cliente partiu de uma revisão velha. Ele precisa receber as operações que perdeu,
    /// reescrever a sua sobre elas (transform) e enviar de novo.
    /// </summary>
    public sealed record Behind : SubmitDecision;

    /// <summary>
    /// Pedido de semeadura (converter o HTML antigo no primeiro Delta) quando alguém já semeou.
    /// Não pode virar <see cref="Behind"/>: o rebase de uma semeadura sobre outra duplicaria o
    /// documento inteiro.
    /// </summary>
    public sealed record AlreadySeeded : SubmitDecision;

    /// <summary>A alteração é inválida e não será aceita em nenhuma revisão.</summary>
    public sealed record Rejected(string Reason) : SubmitDecision;
}

/// <summary>
/// A regra do servidor como sequenciador: ele define a ordem total das alterações de um documento
/// e só aceita uma alteração escrita sobre a revisão atual.
/// </summary>
/// <remarks>
/// <para>
/// É isso que torna a edição simultânea convergente com uma única implementação de
/// <c>transform</c> no caminho real, a do navegador. Toda operação aceita foi escrita sobre o
/// documento exato em que é aplicada, então o servidor só compõe, nunca transforma. Quem estava
/// atrás recebe <see cref="SubmitDecision.Behind"/>, transforma as próprias pendências sobre o que
/// perdeu e tenta de novo. Como todo mundo aplica as mesmas operações na mesma ordem, e cada
/// cliente ajusta só o que ainda é dele, todos chegam ao mesmo documento: basta a propriedade TP1
/// do transform (<c>a ∘ a.transform(b, false) = b ∘ b.transform(a, true)</c>).
/// </para>
/// <para>
/// Função pura de propósito: a persistência (quem guarda o documento, o log e os reenvios) fica
/// no serviço da aplicação, e a simulação de convergência nos testes usa exatamente esta regra.
/// </para>
/// </remarks>
public static class DocSequencer
{
    public static SubmitDecision Decide(Delta document, long headRevision, long baseRevision, Delta change, bool isSeed)
    {
        if (baseRevision < 0 || baseRevision > headRevision)
        {
            return new SubmitDecision.Rejected("Revisão desconhecida.");
        }

        if (isSeed && headRevision != 0)
        {
            return new SubmitDecision.AlreadySeeded();
        }

        if (baseRevision < headRevision)
        {
            return new SubmitDecision.Behind();
        }

        return DocDeltaPolicy.TryApply(document, change, out var result, out var error)
            ? new SubmitDecision.Accepted(result, headRevision + 1)
            : new SubmitDecision.Rejected(error!);
    }
}
