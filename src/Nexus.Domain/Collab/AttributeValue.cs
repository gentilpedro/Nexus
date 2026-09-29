using System.Globalization;
using System.Text.Json;

namespace Nexus.Domain.Collab;

/// <summary>
/// Valor escalar de um atributo do Delta (<c>true</c>, <c>3</c>, <c>"ordered"</c>) ou <c>null</c>,
/// que no Delta significa "remover este formato".
/// </summary>
/// <remarks>
/// Só escalares: os formatos que o editor usa (negrito, título, lista, link, cor…) nunca têm
/// objeto como valor, e aceitar objetos abriria espaço para payload arbitrário sem ganho nenhum.
/// A igualdade é por valor, como o <c>isEqual</c> do quill-delta.
/// </remarks>
public readonly record struct AttributeValue
{
    private AttributeValue(JsonValueKind kind, string? text, double number)
    {
        Kind = kind;
        Text = text;
        Number = number;
    }

    public JsonValueKind Kind { get; }
    public string? Text { get; }
    public double Number { get; }

    public static AttributeValue Null { get; } = new(JsonValueKind.Null, null, 0);
    public static AttributeValue True { get; } = new(JsonValueKind.True, null, 0);
    public static AttributeValue False { get; } = new(JsonValueKind.False, null, 0);

    public static AttributeValue From(string text) => new(JsonValueKind.String, text, 0);
    public static AttributeValue From(double number) => new(JsonValueKind.Number, null, number);

    public bool IsNull => Kind == JsonValueKind.Null;

    /// <summary>Lê um escalar JSON; objetos e arrays são recusados.</summary>
    public static bool TryRead(JsonElement element, out AttributeValue value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = From(element.GetString()!);
                return true;
            case JsonValueKind.Number when element.TryGetDouble(out var number) && double.IsFinite(number):
                value = From(number);
                return true;
            case JsonValueKind.True:
                value = True;
                return true;
            case JsonValueKind.False:
                value = False;
                return true;
            case JsonValueKind.Null:
                value = Null;
                return true;
            default:
                value = default;
                return false;
        }
    }

    public void WriteTo(Utf8JsonWriter writer)
    {
        switch (Kind)
        {
            case JsonValueKind.String:
                writer.WriteStringValue(Text);
                break;
            case JsonValueKind.Number:
                // Inteiros saem sem casa decimal, como no JSON.stringify do navegador.
                if (Number == Math.Floor(Number) && Math.Abs(Number) < 1e15)
                {
                    writer.WriteNumberValue((long)Number);
                }
                else
                {
                    writer.WriteNumberValue(Number);
                }
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    public override string ToString() => Kind switch
    {
        JsonValueKind.String => Text!,
        JsonValueKind.Number => Number.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => "null",
    };
}
