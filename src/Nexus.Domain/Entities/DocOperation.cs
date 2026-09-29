namespace Nexus.Domain.Entities;

/// <summary>
/// Uma alteração aceita num documento colaborativo, na ordem em que o servidor a aceitou.
/// </summary>
/// <remarks>
/// <para>
/// O log serve para quem ficou para trás: um editor que partiu da revisão 10 quando o documento
/// já está na 13 recebe as operações 11 a 13, transforma as próprias pendências sobre elas e
/// reenvia. O documento atual não depende do log — está sempre composto em
/// <see cref="DocPage.DeltaJson"/> — então as operações antigas podem ser podadas.
/// </para>
/// <para>
/// <see cref="ClientId"/> + <see cref="ClientSeq"/> identificam a alteração do lado de quem a
/// escreveu. Uma resposta perdida na rede faz o editor reenviar a mesma alteração, com o mesmo
/// número; o índice único nesse par é o que garante que ela não seja aplicada duas vezes.
/// </para>
/// </remarks>
public class DocOperation
{
    public long Id { get; set; }

    public Guid DocPageId { get; set; }
    public DocPage DocPage { get; set; } = null!;

    /// <summary>Revisão que esta operação criou (a primeira operação cria a revisão 1).</summary>
    public long Revision { get; set; }

    public Guid ClientId { get; set; }
    public long ClientSeq { get; set; }

    /// <summary>A alteração, em Delta JSON, exatamente como foi aplicada.</summary>
    public string ChangeJson { get; set; } = "";

    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
