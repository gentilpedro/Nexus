using Nexus.Web.Services;

namespace Nexus.Web.Tests;

/// <summary>
/// Cobre a leitura da identificação da build exposta em /version e no rodapé.
/// </summary>
/// <remarks>
/// O valor vem de atributos que só a CI preenche, então o caminho fácil de quebrar é o inverso:
/// uma build sem esses atributos (a local, ou uma publicação que perdeu as propriedades) precisa
/// degradar para algo honesto em vez de inventar uma versão.
/// </remarks>
public class AppVersionTests
{
    private const string Sha = "a3f9c21d4e5b6a7c8d9e0f1a2b3c4d5e6f708192";

    [Fact]
    public void Parse_SplitsVersionAndCommit()
    {
        var info = AppVersion.Parse($"1.4.0+{Sha}", null);

        Assert.Equal("1.4.0", info.Version);
        Assert.Equal(Sha, info.Commit);
        Assert.Equal("a3f9c21", info.ShortCommit);
    }

    [Fact]
    public void Parse_WithoutCommitMetadata_KeepsTheVersion()
    {
        var info = AppVersion.Parse("1.4.0", null);

        Assert.Equal("1.4.0", info.Version);
        Assert.Null(info.Commit);
        Assert.Null(info.ShortCommit);
    }

    /// <summary>
    /// Uma build local não passa -p:Version, e é importante que ela não se disfarce de release:
    /// se /version respondesse "1.0.0" num binário compilado na máquina de alguém, a resposta
    /// seria pior que nenhuma.
    /// </summary>
    [Fact]
    public void Parse_WithoutAnyMetadata_IdentifiesItselfAsALocalBuild()
    {
        var info = AppVersion.Parse(null, null);

        Assert.Equal("0.0.0-local", info.Version);
        Assert.Null(info.Commit);
        Assert.Null(info.BuiltAt);
    }

    [Theory]
    [InlineData("1.4.0+")]     // SourceRevisionId vazio
    [InlineData("1.4.0+   ")]  // e só espaços
    public void Parse_WithEmptyCommitMetadata_ReportsNoCommit(string informational)
    {
        var info = AppVersion.Parse(informational, null);

        Assert.Equal("1.4.0", info.Version);
        Assert.Null(info.Commit);
    }

    [Fact]
    public void Parse_ReadsTheBuildTimestampAsUtc()
    {
        var info = AppVersion.Parse("1.4.0", "2026-08-09T14:22:10.0000000Z");

        Assert.NotNull(info.BuiltAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 14, 22, 10, TimeSpan.Zero), info.BuiltAt);
    }

    [Theory]
    [InlineData("nao e uma data")]
    [InlineData("")]
    public void Parse_WithAnUnreadableTimestamp_ReportsNoBuildDate(string timestamp)
    {
        var info = AppVersion.Parse("1.4.0", timestamp);

        Assert.Equal("1.4.0", info.Version);
        Assert.Null(info.BuiltAt);
    }

    [Fact]
    public void DetailedLabel_CarriesCommitAndBuildDate()
    {
        var info = AppVersion.Parse($"1.4.0+{Sha}", "2026-08-09T14:22:10.0000000Z");

        Assert.Equal("1.4.0 — commit a3f9c21, build de 09/08/2026 14:22 UTC", info.DetailedLabel);
    }

    [Fact]
    public void DetailedLabel_WithoutMetadata_IsJustTheVersion()
    {
        Assert.Equal("0.0.0-local", AppVersion.Parse(null, null).DetailedLabel);
    }

    /// <summary>
    /// O caminho real: ler os atributos de um assembly de verdade. Não assere a versão em si (ela
    /// muda a cada release), só que a leitura funciona e devolve algo utilizável.
    /// </summary>
    [Fact]
    public void Current_IsResolvedFromTheRunningAssembly()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current.Version));
    }

    /// <summary>
    /// Um assembly que não passou pelo build do Nexus não tem o metadado BuildTimestamp. A leitura
    /// tem que devolver a versão mesmo assim, sem data e sem explodir.
    /// </summary>
    [Fact]
    public void FromAssembly_WithoutTheBuildTimestampMetadata_ReportsNoBuildDate()
    {
        var info = AppVersion.FromAssembly(typeof(object).Assembly);

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.Null(info.BuiltAt);
    }
}
