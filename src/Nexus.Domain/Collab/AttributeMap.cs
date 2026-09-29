namespace Nexus.Domain.Collab;

/// <summary>
/// Conjunto imutável de atributos de uma operação. Nunca vazio: "sem atributos" é <c>null</c>,
/// como o <c>undefined</c> do quill-delta, para que a comparação entre operações bata com a dele.
/// </summary>
public sealed class AttributeMap
{
    private readonly SortedDictionary<string, AttributeValue> values;

    private AttributeMap(SortedDictionary<string, AttributeValue> values) => this.values = values;

    public IReadOnlyDictionary<string, AttributeValue> Values => values;

    public int Count => values.Count;

    /// <summary>Cria o mapa, ou devolve <c>null</c> quando não há atributo nenhum.</summary>
    public static AttributeMap? Create(IEnumerable<KeyValuePair<string, AttributeValue>> entries)
    {
        var dictionary = new SortedDictionary<string, AttributeValue>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dictionary[key] = value;
        }
        return dictionary.Count == 0 ? null : new AttributeMap(dictionary);
    }

    public static AttributeMap? Of(params (string Key, AttributeValue Value)[] entries) =>
        Create(entries.Select(e => KeyValuePair.Create(e.Key, e.Value)));

    public bool TryGetValue(string key, out AttributeValue value) => values.TryGetValue(key, out value);

    public static bool AreEqual(AttributeMap? a, AttributeMap? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }
        foreach (var (key, value) in a.values)
        {
            if (!b.values.TryGetValue(key, out var other) || other != value)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Atributos de <paramref name="a"/> seguidos de <paramref name="b"/>: <c>b</c> vence, e
    /// <c>null</c> em <c>b</c> remove o formato — a menos que <paramref name="keepNull"/>, usado
    /// quando o resultado ainda é um retain e o <c>null</c> precisa continuar valendo.
    /// </summary>
    /// <remarks>Porte de <c>AttributeMap.compose</c> do quill-delta 5.1.0, inclusive na borda em
    /// que um <c>null</c> vindo de <c>a</c> é mantido mesmo sem <c>keepNull</c>.</remarks>
    public static AttributeMap? Compose(AttributeMap? a, AttributeMap? b, bool keepNull)
    {
        var result = new List<KeyValuePair<string, AttributeValue>>();
        if (b is not null)
        {
            foreach (var entry in b.values)
            {
                if (keepNull || !entry.Value.IsNull)
                {
                    result.Add(entry);
                }
            }
        }
        if (a is not null)
        {
            foreach (var entry in a.values)
            {
                if (b is null || !b.values.ContainsKey(entry.Key))
                {
                    result.Add(entry);
                }
            }
        }
        return Create(result);
    }

    /// <summary>
    /// Os atributos de <paramref name="b"/> ajustados para depois de <paramref name="a"/>, que
    /// aconteceu em paralelo. Com prioridade, <c>a</c> chegou primeiro e vence nas chaves em comum.
    /// </summary>
    /// <remarks>Porte de <c>AttributeMap.transform</c> do quill-delta 5.1.0.</remarks>
    public static AttributeMap? Transform(AttributeMap? a, AttributeMap? b, bool priority)
    {
        if (a is null)
        {
            return b;
        }
        if (b is null)
        {
            return null;
        }
        if (!priority)
        {
            return b;
        }
        return Create(b.values.Where(entry => !a.values.ContainsKey(entry.Key)));
    }
}
