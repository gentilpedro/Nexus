namespace Nexus.Domain.Collab;

public enum DeltaOpKind
{
    Insert,
    Retain,
    Delete,
}

/// <summary>Embed do Quill: um objeto com uma única chave, como <c>{ "image": "https://…" }</c>.</summary>
public sealed record DeltaEmbed(string Type, AttributeValue Value);

/// <summary>
/// Uma operação do Delta: inserir texto ou embed, manter (<c>retain</c>, opcionalmente
/// formatando) ou apagar. Imutável.
/// </summary>
/// <remarks>
/// Retain com objeto (usado pelo Quill só para alterar dados de embeds complexos) não existe
/// aqui de propósito: nenhum formato permitido no editor gera isso, e a leitura recusa.
/// </remarks>
public sealed class DeltaOp
{
    /// <summary>Faz o papel do <c>Infinity</c> do quill-delta no iterador.</summary>
    internal const int Unbounded = int.MaxValue;

    private DeltaOp(DeltaOpKind kind, string? text, DeltaEmbed? embed, int count, AttributeMap? attributes)
    {
        Kind = kind;
        Text = text;
        Embed = embed;
        Count = count;
        Attributes = attributes;
    }

    public DeltaOpKind Kind { get; }

    /// <summary>Texto inserido; <c>null</c> quando o insert é um embed.</summary>
    public string? Text { get; }

    public DeltaEmbed? Embed { get; }

    /// <summary>Tamanho de um retain ou delete.</summary>
    public int Count { get; }

    public AttributeMap? Attributes { get; }

    public bool IsInsert => Kind == DeltaOpKind.Insert;
    public bool IsRetain => Kind == DeltaOpKind.Retain;
    public bool IsDelete => Kind == DeltaOpKind.Delete;
    public bool IsTextInsert => Kind == DeltaOpKind.Insert && Text is not null;

    /// <summary>Tamanho no documento: um embed ocupa uma posição, texto ocupa um por unidade UTF-16
    /// (a mesma conta do <c>string.length</c> no navegador).</summary>
    public int Length => Kind switch
    {
        DeltaOpKind.Insert => Text?.Length ?? 1,
        _ => Count,
    };

    public static DeltaOp InsertText(string text, AttributeMap? attributes = null) =>
        new(DeltaOpKind.Insert, text, null, 0, attributes);

    public static DeltaOp InsertEmbed(DeltaEmbed embed, AttributeMap? attributes = null) =>
        new(DeltaOpKind.Insert, null, embed, 0, attributes);

    public static DeltaOp Retain(int count, AttributeMap? attributes = null) =>
        new(DeltaOpKind.Retain, null, null, count, attributes);

    public static DeltaOp Delete(int count) => new(DeltaOpKind.Delete, null, null, count, null);

    /// <summary>A mesma operação com outros atributos (só faz sentido para insert e retain).</summary>
    public DeltaOp WithAttributes(AttributeMap? attributes) =>
        new(Kind, Text, Embed, Count, attributes);

    public static bool AreEqual(DeltaOp a, DeltaOp b) =>
        a.Kind == b.Kind
        && a.Count == b.Count
        && a.Text == b.Text
        && a.Embed == b.Embed
        && AttributeMap.AreEqual(a.Attributes, b.Attributes);
}
