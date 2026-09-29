using Nexus.Domain.Collab;

namespace Nexus.Domain.Tests.Collab;

/// <summary>
/// A regra que o servidor aplica antes de aceitar uma operação. Cada caso aqui é algo que o
/// editor nunca produz, mas que um cliente adulterado pode mandar pelo circuito.
/// </summary>
public class DocDeltaPolicyTests
{
    private static Delta Doc(string json) => DeltaJson.Parse(json);

    private static readonly Delta Hello = Doc("""[{"insert":"Olá mundo\n"}]""");

    [Fact]
    public void ValidChange_IsApplied()
    {
        var change = Doc("""[{"retain":4},{"insert":", ","attributes":{"bold":true}}]""");

        Assert.True(DocDeltaPolicy.TryApply(Hello, change, out var result, out var error), error);
        Assert.Equal("""[{"insert":"Olá "},{"insert":", ","attributes":{"bold":true}},{"insert":"mundo\n"}]""", DeltaJson.Serialize(result));
    }

    [Fact]
    public void RemovingAFormat_IsAllowed()
    {
        var bold = Doc("""[{"insert":"abc","attributes":{"bold":true}},{"insert":"\n"}]""");
        var change = Doc("""[{"retain":3,"attributes":{"bold":null}}]""");

        Assert.True(DocDeltaPolicy.TryApply(bold, change, out var result, out _));
        Assert.Equal("""[{"insert":"abc\n"}]""", DeltaJson.Serialize(result));
    }

    [Theory]
    [InlineData("""[{"retain":20},{"insert":"x"}]""")] // além do fim do documento
    [InlineData("""[{"retain":9},{"delete":1}]""")] // apaga a quebra de linha final
    [InlineData("""[{"delete":10}]""")] // apaga tudo, inclusive a quebra final
    [InlineData("""[{"retain":2,"attributes":{"font":"serif"}}]""")] // formato fora da lista
    [InlineData("""[{"retain":10,"attributes":{"header":9}}]""")] // título inexistente
    [InlineData("""[{"retain":10,"attributes":{"header":1.5}}]""")]
    [InlineData("""[{"retain":10,"attributes":{"list":"roman"}}]""")]
    [InlineData("""[{"retain":2,"attributes":{"bold":"sim"}}]""")]
    [InlineData("""[{"insert":{"image":"data:image/png;base64,AAAA"}}]""")] // imagem colada
    [InlineData("""[{"insert":{"image":"javascript:alert(1)"}}]""")]
    [InlineData("""[{"insert":{"video":"https://nexus.example/v.mp4"}}]""")]
    public void InvalidChange_IsRejected(string json)
    {
        Assert.False(DocDeltaPolicy.TryApply(Hello, Doc(json), out var result, out var error));
        Assert.NotNull(error);
        Assert.Same(Hello, result);
    }

    [Theory]
    [InlineData("""[{"retain":1},{"delete":1}]""")] // apaga só uma metade do emoji
    [InlineData("""[{"retain":1},{"insert":"x"}]""")] // insere entre as duas metades
    [InlineData("""[{"retain":1,"attributes":{"bold":true}}]""")] // formata meio emoji
    public void ChangeThatSplitsACharacter_IsRejected(string json)
    {
        var emoji = Doc("""[{"insert":"😀\n"}]""");

        Assert.False(DocDeltaPolicy.TryApply(emoji, Doc(json), out _, out var error));
        Assert.Contains("ao meio", error);
    }

    [Fact]
    public void EmptyChange_IsRejected()
    {
        Assert.False(DocDeltaPolicy.TryApply(Hello, new Delta(), out _, out _));
    }

    [Fact]
    public void ChangeThatWouldExceedTheSizeLimit_IsRejected()
    {
        var huge = new Delta().Insert(new string('a', DocDeltaPolicy.MaxDocumentLength));

        Assert.False(DocDeltaPolicy.TryApply(Hello, huge, out _, out var error));
        Assert.Contains("limite", error);
    }

    [Fact]
    public void Sequencer_AcceptsOnlyTheCurrentRevision()
    {
        var change = Doc("""[{"insert":"!"}]""");

        Assert.IsType<SubmitDecision.Behind>(DocSequencer.Decide(Hello, headRevision: 5, baseRevision: 4, change, isSeed: false));
        Assert.IsType<SubmitDecision.Rejected>(DocSequencer.Decide(Hello, headRevision: 5, baseRevision: 6, change, isSeed: false));

        var accepted = Assert.IsType<SubmitDecision.Accepted>(DocSequencer.Decide(Hello, headRevision: 5, baseRevision: 5, change, isSeed: false));
        Assert.Equal(6, accepted.Revision);
    }

    [Fact]
    public void Sequencer_NeverRebasesASeed()
    {
        // Duas pessoas abrindo um documento antigo ao mesmo tempo: as duas convertem o HTML e
        // mandam a semeadura. A segunda não pode ser tratada como "atrasada" — o rebase dela
        // sobre a primeira duplicaria o documento inteiro.
        var seed = Doc("""[{"insert":"conteúdo antigo"}]""");

        Assert.IsType<SubmitDecision.Accepted>(DocSequencer.Decide(DocDeltaPolicy.EmptyDocument(), 0, 0, seed, isSeed: true));
        Assert.IsType<SubmitDecision.AlreadySeeded>(DocSequencer.Decide(Hello, 1, 0, seed, isSeed: true));
    }
}
