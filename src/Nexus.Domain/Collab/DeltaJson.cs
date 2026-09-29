using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Nexus.Domain.Collab;

/// <summary>A alteração ou documento recebido não é um Delta bem formado.</summary>
public sealed class DeltaFormatException(string message) : Exception(message);

/// <summary>
/// Leitura e escrita de Deltas no JSON que o Quill usa: um array de operações, ou
/// <c>{ "ops": [...] }</c>.
/// </summary>
/// <remarks>
/// A leitura é estrita porque o JSON chega do navegador pelo circuito do Blazor e pode ter sido
/// montado à mão: cada operação precisa ter exatamente um de <c>insert</c>, <c>retain</c> ou
/// <c>delete</c>, com tamanhos positivos e atributos escalares. Qualquer outra coisa é recusada
/// em vez de ignorada, para que o servidor nunca componha algo diferente do que o cliente quis
/// dizer. Quais formatos são permitidos é outra camada: <see cref="DocDeltaPolicy"/>.
/// </remarks>
public static class DeltaJson
{
    /// <summary>Teto de operações num único Delta, bem acima de qualquer edição humana.</summary>
    public const int MaxOps = 100_000;

    public static Delta Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException)
        {
            throw new DeltaFormatException("JSON inválido.");
        }

        using (document)
        {
            return Parse(document.RootElement);
        }
    }

    public static Delta Parse(JsonElement root)
    {
        var array = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("ops", out var ops) && ops.ValueKind == JsonValueKind.Array => ops,
            _ => throw new DeltaFormatException("Esperado um array de operações."),
        };

        if (array.GetArrayLength() > MaxOps)
        {
            throw new DeltaFormatException("Operações demais.");
        }

        // Push normaliza: um Delta já canônico (o que o quill-delta sempre produz) passa igual, e
        // um montado de outro jeito chega à mesma forma, com o mesmo significado.
        var delta = new Delta();
        try
        {
            foreach (var element in array.EnumerateArray())
            {
                delta.Push(ParseOp(element));
            }
        }
        catch (InvalidOperationException)
        {
            // O JSON é válido, mas algum texto traz só metade de um par UTF-16 (um emoji cortado
            // ao meio). O JavaScript aceita isso; o .NET não consegue ler como string.
            throw new DeltaFormatException("Texto com caractere UTF-16 incompleto.");
        }
        return delta;
    }

    private static DeltaOp ParseOp(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new DeltaFormatException("Operação precisa ser um objeto.");
        }

        JsonElement? insert = null, retain = null, delete = null, attributes = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "insert" when insert is null: insert = property.Value; break;
                case "retain" when retain is null: retain = property.Value; break;
                case "delete" when delete is null: delete = property.Value; break;
                case "attributes" when attributes is null: attributes = property.Value; break;
                default: throw new DeltaFormatException($"Campo inesperado: {property.Name}.");
            }
        }

        var kinds = (insert is null ? 0 : 1) + (retain is null ? 0 : 1) + (delete is null ? 0 : 1);
        if (kinds != 1)
        {
            throw new DeltaFormatException("Operação precisa ter exatamente um de insert, retain ou delete.");
        }

        if (delete is { } deleteValue)
        {
            if (attributes is not null)
            {
                throw new DeltaFormatException("Delete não leva atributos.");
            }
            return DeltaOp.Delete(ReadCount(deleteValue));
        }

        var attributeMap = attributes is { } attributesValue ? ReadAttributes(attributesValue) : null;

        if (retain is { } retainValue)
        {
            return DeltaOp.Retain(ReadCount(retainValue), attributeMap);
        }

        var insertValue = insert!.Value;
        if (insertValue.ValueKind == JsonValueKind.String)
        {
            var text = insertValue.GetString()!;
            if (text.Length == 0)
            {
                throw new DeltaFormatException("Insert de texto vazio.");
            }
            return DeltaOp.InsertText(text, attributeMap);
        }

        if (insertValue.ValueKind == JsonValueKind.Object)
        {
            using var properties = insertValue.EnumerateObject();
            if (!properties.MoveNext())
            {
                throw new DeltaFormatException("Embed vazio.");
            }
            var embed = properties.Current;
            if (properties.MoveNext() || !AttributeValue.TryRead(embed.Value, out var embedValue) || embedValue.IsNull)
            {
                throw new DeltaFormatException("Embed precisa de uma única chave com valor escalar.");
            }
            return DeltaOp.InsertEmbed(new DeltaEmbed(embed.Name, embedValue), attributeMap);
        }

        throw new DeltaFormatException("Insert precisa ser texto ou embed.");
    }

    private static int ReadCount(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count <= 0)
        {
            throw new DeltaFormatException("Tamanho precisa ser um inteiro positivo.");
        }
        return count;
    }

    private static AttributeMap? ReadAttributes(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new DeltaFormatException("Atributos precisam ser um objeto.");
        }
        var entries = new List<KeyValuePair<string, AttributeValue>>();
        foreach (var property in value.EnumerateObject())
        {
            if (!AttributeValue.TryRead(property.Value, out var attribute))
            {
                throw new DeltaFormatException($"Atributo {property.Name} precisa ser escalar.");
            }
            entries.Add(KeyValuePair.Create(property.Name, attribute));
        }
        return AttributeMap.Create(entries);
    }

    /// <summary>Escreve o Delta como array de operações, no formato do quill-delta.</summary>
    public static string Serialize(Delta delta)
    {
        using var stream = new MemoryStream();
        // Escape relaxado: o JSON vai para o banco e para o editor, nunca para dentro de HTML, e o
        // escape padrão transformaria cada "ç" do texto em seis bytes.
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            Write(writer, delta);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Utf8JsonWriter writer, Delta delta)
    {
        writer.WriteStartArray();
        foreach (var op in delta.Ops)
        {
            writer.WriteStartObject();
            switch (op.Kind)
            {
                case DeltaOpKind.Insert when op.Text is not null:
                    writer.WriteString("insert", op.Text);
                    break;
                case DeltaOpKind.Insert:
                    writer.WritePropertyName("insert");
                    writer.WriteStartObject();
                    writer.WritePropertyName(op.Embed!.Type);
                    op.Embed.Value.WriteTo(writer);
                    writer.WriteEndObject();
                    break;
                case DeltaOpKind.Retain:
                    writer.WriteNumber("retain", op.Count);
                    break;
                case DeltaOpKind.Delete:
                    writer.WriteNumber("delete", op.Count);
                    break;
            }
            if (op.Attributes is not null)
            {
                writer.WritePropertyName("attributes");
                writer.WriteStartObject();
                foreach (var (key, value) in op.Attributes.Values)
                {
                    writer.WritePropertyName(key);
                    value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
