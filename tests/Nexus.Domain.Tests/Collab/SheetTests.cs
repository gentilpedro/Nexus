using Nexus.Domain.Collab;

namespace Nexus.Domain.Tests.Collab;

/// <summary>
/// As regras da planilha colaborativa (#57): endereço por identificador, conflito só na mesma
/// célula e o formato gravado, incluindo o antigo.
/// </summary>
public class SheetTests
{
    private static SheetDocument Sample() => SheetDocument.FromGrid([["a", "b"], ["c", "d"]]);

    private static SheetDocument Applied(SheetApplyResult result) =>
        Assert.IsType<SheetApplyResult.Applied>(result).Document;

    [Fact]
    public void LegacyGrid_GetsPositionalIds_SoEveryReaderSeesTheSameAddresses()
    {
        var sheet = SheetJson.Parse("""[["a","b"],["c","d"]]""");

        Assert.Equal(["r0", "r1"], sheet.Rows);
        Assert.Equal(["c0", "c1"], sheet.Columns);
        Assert.Equal("d", sheet.Get("r1", "c1"));
        Assert.Equal([["a", "b"], ["c", "d"]], sheet.ToGrid());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ isto não é json")]
    [InlineData("""{"rows":[],"columns":["c0"],"cells":{}}""")]
    public void UnreadableOrEmptyContent_BecomesABlankSheet(string? json)
    {
        var sheet = SheetJson.Parse(json);

        Assert.Equal(5, sheet.Rows.Count);
        Assert.Equal(4, sheet.Columns.Count);
    }

    [Fact]
    public void Serialize_RoundTrips_AndDropsEmptyCells()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.SetCell("r0", "c0", "a", "")));

        var back = SheetJson.Parse(SheetJson.Serialize(sheet));

        Assert.Equal(sheet.ToGrid(), back.ToGrid());
        Assert.DoesNotContain(SheetDocument.Key("r0", "c0"), back.Cells.Keys);
    }

    [Fact]
    public void SetCell_FromTheValueThePersonSaw_IsApplied()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.SetCell("r0", "c1", "b", "B!")));

        Assert.Equal("B!", sheet.Get("r0", "c1"));
    }

    [Fact]
    public void SetCell_AfterSomeoneElseChangedTheCell_IsAConflictCarryingTheCurrentValue()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.SetCell("r0", "c1", "b", "da Ana")));

        var result = SheetSequencer.Apply(sheet, new SheetOperation.SetCell("r0", "c1", "b", "do Pedro"));

        var conflict = Assert.IsType<SheetApplyResult.Conflict>(result);
        Assert.Equal("da Ana", conflict.Current);
    }

    [Fact]
    public void SetCell_ToTheValueAlreadyThere_IsUnchanged_EvenFromAStaleView()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.SetCell("r0", "c1", "b", "igual")));

        Assert.IsType<SheetApplyResult.Unchanged>(SheetSequencer.Apply(sheet, new SheetOperation.SetCell("r0", "c1", "b", "igual")));
    }

    [Fact]
    public void SetCell_OnARemovedRow_IsAConflictWithoutCurrentValue()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.RemoveRow("r1")));

        var conflict = Assert.IsType<SheetApplyResult.Conflict>(SheetSequencer.Apply(sheet, new SheetOperation.SetCell("r1", "c0", "c", "x")));
        Assert.Null(conflict.Current);
    }

    // O motivo dos identificadores: uma linha removida acima não desloca a edição de outra pessoa.
    [Fact]
    public void RemovingARowAbove_DoesNotMoveAnEditBelow()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.RemoveRow("r0")));

        sheet = Applied(SheetSequencer.Apply(sheet, new SheetOperation.SetCell("r1", "c0", "c", "editada")));

        Assert.Equal([["editada", "d"]], sheet.ToGrid());
    }

    [Fact]
    public void RemoveRow_DropsItsCells_AndRepeatingItIsUnchanged()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.RemoveRow("r0")));

        Assert.DoesNotContain(sheet.Cells.Keys, k => k.StartsWith("r0|", StringComparison.Ordinal));
        Assert.IsType<SheetApplyResult.Unchanged>(SheetSequencer.Apply(sheet, new SheetOperation.RemoveRow("r0")));
    }

    [Fact]
    public void AddRow_IsIdempotentByIdentifier()
    {
        var sheet = Applied(SheetSequencer.Apply(Sample(), new SheetOperation.AddRow("nova")));

        Assert.Equal(["r0", "r1", "nova"], sheet.Rows);
        Assert.IsType<SheetApplyResult.Unchanged>(SheetSequencer.Apply(sheet, new SheetOperation.AddRow("nova")));
    }

    [Fact]
    public void Limits_AreEnforced()
    {
        var full = new SheetDocument(Enumerable.Range(0, SheetDocument.MaxRows).Select(i => $"r{i}").ToList(),
            Enumerable.Range(0, SheetDocument.MaxColumns).Select(i => $"c{i}").ToList());

        Assert.IsType<SheetApplyResult.Rejected>(SheetSequencer.Apply(full, new SheetOperation.AddRow("mais")));
        Assert.IsType<SheetApplyResult.Rejected>(SheetSequencer.Apply(full, new SheetOperation.AddColumn("mais")));
        Assert.IsType<SheetApplyResult.Rejected>(SheetSequencer.Apply(full,
            new SheetOperation.SetCell("r0", "c0", "", new string('x', SheetDocument.MaxCellLength + 1))));
        Assert.IsType<SheetApplyResult.Rejected>(SheetSequencer.Apply(SheetDocument.FromGrid([["só"]]), new SheetOperation.RemoveRow("r0")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("r0|c0")]
    [InlineData("com espaço")]
    public void InvalidIdentifiers_AreRejected(string id)
    {
        Assert.IsType<SheetApplyResult.Rejected>(SheetSequencer.Apply(Sample(), new SheetOperation.AddRow(id)));
        Assert.IsType<SheetApplyResult.Rejected>(SheetSequencer.Apply(Sample(), new SheetOperation.SetCell(id, "c0", "a", "x")));
    }
}
