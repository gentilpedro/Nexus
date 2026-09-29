using Nexus.Domain.Collab;

namespace Nexus.Domain.Tests.Collab;

/// <summary>
/// O JSON de uma alteração chega do navegador pelo circuito e pode ter sido montado à mão. A
/// leitura precisa recusar tudo que não é um Delta bem formado, em vez de interpretar do jeito
/// que der — um retain de tamanho zero ou uma operação com dois tipos seria composta de forma
/// diferente da que o cliente imaginou.
/// </summary>
public class DeltaJsonTests
{
    [Theory]
    [InlineData("""[{"insert":"a","retain":1}]""")]
    [InlineData("""[{"retain":0}]""")]
    [InlineData("""[{"retain":-2}]""")]
    [InlineData("""[{"retain":1.5}]""")]
    [InlineData("""[{"retain":{"image":"x"}}]""")]
    [InlineData("""[{"delete":2,"attributes":{"bold":true}}]""")]
    [InlineData("""[{"insert":""}]""")]
    [InlineData("""[{"insert":{}}]""")]
    [InlineData("""[{"insert":{"image":"a","video":"b"}}]""")]
    [InlineData("""[{"insert":{"image":{"src":"a"}}}]""")]
    [InlineData("""[{"insert":"a","attributes":{"bold":{"x":1}}}]""")]
    [InlineData("""[{"insert":"a","attributes":[1]}]""")]
    [InlineData("""[{"insert":"a","extra":1}]""")]
    [InlineData("""[{"insert":"\ud83d"}]""")] // metade de um emoji: JSON válido, texto não
    [InlineData("""[{"insert":"a","attributes":{"link":"\ude00"}}]""")]
    [InlineData("""[1]""")]
    [InlineData("""{"notops":[]}""")]
    [InlineData("""não é json""")]
    public void Parse_RejectsMalformedOperations(string json)
    {
        Assert.Throws<DeltaFormatException>(() => DeltaJson.Parse(json));
    }

    [Fact]
    public void Parse_AcceptsBothArrayAndOpsObject()
    {
        var fromArray = DeltaJson.Parse("""[{"insert":"oi\n"}]""");
        var fromObject = DeltaJson.Parse("""{"ops":[{"insert":"oi\n"}]}""");

        Assert.Equal(DeltaJson.Serialize(fromArray), DeltaJson.Serialize(fromObject));
    }

    [Fact]
    public void RoundTrip_PreservesTextEmbedsAttributesAndNulls()
    {
        const string json = """[{"insert":"ação 😀","attributes":{"bold":true,"header":2}},{"insert":{"image":"https://nexus.example/a.png"}},{"retain":3,"attributes":{"bold":null}},{"delete":4}]""";

        var delta = DeltaJson.Parse(json);

        Assert.Equal(
            DeltaVectorTests.Canonical(json),
            DeltaVectorTests.Canonical(DeltaJson.Serialize(delta)));
    }

    [Fact]
    public void Parse_NormalizesAdjacentOperations()
    {
        // Montado à mão, sem a forma canônica: o significado precisa ser o mesmo.
        var delta = DeltaJson.Parse("""[{"retain":1},{"retain":2},{"delete":1},{"insert":"x"}]""");

        Assert.Equal("""[{"retain":3},{"insert":"x"},{"delete":1}]""", DeltaJson.Serialize(delta));
    }
}
