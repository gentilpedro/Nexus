using System.Text;
using System.Text.Json;
using Nexus.Domain.Collab;

namespace Nexus.Domain.Tests.Collab;

/// <summary>
/// O <see cref="Delta"/> em C# precisa produzir exatamente o mesmo resultado que o quill-delta do
/// navegador. O servidor compõe as alterações que os editores mandam: se uma única composição
/// divergir, o documento salvo deixa de ser o que as pessoas estão vendo, e o próximo transform
/// no navegador trabalha sobre posições erradas.
/// </summary>
/// <remarks>
/// Os casos saem de <c>tests/collab-js/gen-vectors.mjs</c>, que roda o pacote oficial
/// (<c>quill-delta@5.1.0</c>, a versão embutida no Quill 2.0.3) com semente fixa. A comparação é
/// sobre as operações cruas, sem normalizar a saída do C#: normalizar esconderia justamente a
/// diferença de forma que faria os dois lados discordarem numa próxima composição.
/// </remarks>
public class DeltaVectorTests
{
    private static readonly JsonDocument Vectors = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Collab", "delta-vectors.json")));

    [Fact]
    public void Vectors_AreLoaded()
    {
        Assert.True(Vectors.RootElement.GetProperty("compose").GetArrayLength() > 500);
        Assert.True(Vectors.RootElement.GetProperty("transform").GetArrayLength() > 400);
    }

    [Fact]
    public void Compose_MatchesQuillDelta()
    {
        var failures = new List<string>();
        var index = 0;
        foreach (var vector in Vectors.RootElement.GetProperty("compose").EnumerateArray())
        {
            var a = DeltaJson.Parse(vector.GetProperty("a"));
            var b = DeltaJson.Parse(vector.GetProperty("b"));

            var actual = Canonical(DeltaJson.Serialize(a.Compose(b)));
            var expected = Canonical(vector.GetProperty("expected").GetRawText());
            if (actual != expected)
            {
                failures.Add($"#{index}: a={vector.GetProperty("a")} b={vector.GetProperty("b")}\n  esperado {expected}\n  obtido   {actual}");
            }
            index++;
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
    }

    [Fact]
    public void Transform_MatchesQuillDelta()
    {
        var failures = new List<string>();
        var index = 0;
        foreach (var vector in Vectors.RootElement.GetProperty("transform").EnumerateArray())
        {
            var a = DeltaJson.Parse(vector.GetProperty("a"));
            var b = DeltaJson.Parse(vector.GetProperty("b"));
            var priority = vector.GetProperty("priority").GetBoolean();

            var actual = Canonical(DeltaJson.Serialize(a.Transform(b, priority)));
            var expected = Canonical(vector.GetProperty("expected").GetRawText());
            if (actual != expected)
            {
                failures.Add($"#{index} (priority={priority}): a={vector.GetProperty("a")} b={vector.GetProperty("b")}\n  esperado {expected}\n  obtido   {actual}");
            }
            index++;
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
    }

    [Fact]
    public void Transform_SatisfiesTp1OnEveryVector()
    {
        // a ∘ a.transform(b, false) = b ∘ b.transform(a, true): aplicar as duas alterações
        // concorrentes em qualquer ordem, ajustando a segunda, leva ao mesmo resultado. É a
        // propriedade de que o protocolo depende para convergir.
        foreach (var vector in Vectors.RootElement.GetProperty("transform").EnumerateArray())
        {
            var a = DeltaJson.Parse(vector.GetProperty("a"));
            var b = DeltaJson.Parse(vector.GetProperty("b"));

            var left = a.Compose(a.Transform(b, priority: false));
            var right = b.Compose(b.Transform(a, priority: true));

            Assert.Equal(Canonical(DeltaJson.Serialize(right)), Canonical(DeltaJson.Serialize(left)));
        }
    }

    /// <summary>JSON com as chaves de cada objeto em ordem, para comparar sem depender da ordem
    /// em que cada lado escreveu os atributos.</summary>
    internal static string Canonical(string json)
    {
        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder();
        Write(document.RootElement, builder);
        return builder.ToString();
    }

    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    first = false;
                    builder.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    Write(property.Value, builder);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }
                    firstItem = false;
                    Write(item, builder);
                }
                builder.Append(']');
                break;
            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(element.GetString()));
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }
}
