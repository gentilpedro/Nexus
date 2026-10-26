using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.Domain.Collab;

/// <summary>
/// Uma planilha como a edição colaborativa a enxerga (#57): linhas e colunas com identificador
/// fixo, e as células endereçadas por esse par — não pela posição.
/// </summary>
/// <remarks>
/// O identificador é o que torna a edição simultânea segura: se uma pessoa remove a linha 3
/// enquanto outra digita na linha 7, a célula editada continua sendo a mesma, mesmo que ela passe
/// a ser exibida na linha 6. Com endereço por posição, a edição cairia na linha errada.
/// </remarks>
public sealed class SheetDocument
{
    public const int MaxRows = 50;
    public const int MaxColumns = 20;
    public const int MaxCellLength = 1000;

    private readonly Dictionary<string, string> cells;

    public SheetDocument(IReadOnlyList<string> rows, IReadOnlyList<string> columns, IReadOnlyDictionary<string, string>? cells = null)
    {
        Rows = rows;
        Columns = columns;
        // Célula vazia não ocupa espaço: "apagar" é só tirar a chave.
        this.cells = (cells ?? new Dictionary<string, string>())
            .Where(kv => kv.Value.Length > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public IReadOnlyList<string> Rows { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyDictionary<string, string> Cells => cells;

    public static string Key(string rowId, string columnId) => $"{rowId}|{columnId}";

    public string Get(string rowId, string columnId) => cells.GetValueOrDefault(Key(rowId, columnId), "");

    public static SheetDocument Blank(int rows = 5, int columns = 4) =>
        new(Enumerable.Range(0, rows).Select(i => $"r{i}").ToList(), Enumerable.Range(0, columns).Select(i => $"c{i}").ToList());

    /// <summary>
    /// Converte o formato antigo (lista de linhas, cada uma uma lista de textos). Os
    /// identificadores saem da posição (<c>r0</c>, <c>c0</c>…), então duas pessoas que abram a mesma
    /// planilha antiga ao mesmo tempo enxergam os mesmos endereços sem precisar gravar nada antes.
    /// </summary>
    public static SheetDocument FromGrid(IReadOnlyList<IReadOnlyList<string>> grid)
    {
        if (grid.Count == 0)
        {
            return Blank();
        }

        var columnCount = Math.Max(1, grid.Max(r => r.Count));
        var rows = Enumerable.Range(0, grid.Count).Select(i => $"r{i}").ToList();
        var columns = Enumerable.Range(0, columnCount).Select(i => $"c{i}").ToList();
        var values = new Dictionary<string, string>();
        for (var r = 0; r < grid.Count; r++)
        {
            for (var c = 0; c < grid[r].Count; c++)
            {
                values[Key(rows[r], columns[c])] = grid[r][c] ?? "";
            }
        }
        return new SheetDocument(rows, columns, values);
    }

    /// <summary>Linhas e colunas na ordem de exibição — o que a exportação .xlsx consome.</summary>
    public List<List<string>> ToGrid() =>
        Rows.Select(r => Columns.Select(c => Get(r, c)).ToList()).ToList();

    internal SheetDocument With(IReadOnlyList<string>? rows = null, IReadOnlyList<string>? columns = null, Dictionary<string, string>? values = null) =>
        new(rows ?? Rows, columns ?? Columns, values ?? cells);

    internal Dictionary<string, string> CopyCells() => new(cells);
}

/// <summary>Uma alteração na planilha, sempre por identificador.</summary>
public abstract record SheetOperation
{
    private SheetOperation() { }

    /// <summary>
    /// Escreve <paramref name="Value"/> na célula. <paramref name="Expected"/> é o valor que a
    /// pessoa via quando começou a editar: se outra pessoa mudou a célula nesse meio-tempo, a
    /// alteração vira conflito em vez de apagar o que a outra escreveu.
    /// </summary>
    public sealed record SetCell(string RowId, string ColumnId, string Expected, string Value) : SheetOperation;

    /// <summary>Acrescenta uma linha no fim. O identificador vem de quem pede, para o reenvio não duplicar.</summary>
    public sealed record AddRow(string RowId) : SheetOperation;

    public sealed record RemoveRow(string RowId) : SheetOperation;

    public sealed record AddColumn(string ColumnId) : SheetOperation;

    public sealed record RemoveColumn(string ColumnId) : SheetOperation;
}

/// <summary>O que aconteceu com uma <see cref="SheetOperation"/>.</summary>
public abstract record SheetApplyResult
{
    private SheetApplyResult() { }

    public sealed record Applied(SheetDocument Document) : SheetApplyResult;

    /// <summary>Nada a mudar: a planilha já está como a operação pedia (inclusive um reenvio).</summary>
    public sealed record Unchanged : SheetApplyResult;

    /// <summary>
    /// A célula mudou desde que a pessoa a viu, ou a linha/coluna dela foi removida.
    /// <paramref name="Current"/> é o valor que está lá agora (nulo se a célula não existe mais).
    /// </summary>
    public sealed record Conflict(string Reason, string? Current) : SheetApplyResult;

    public sealed record Rejected(string Reason) : SheetApplyResult;
}

/// <summary>
/// A regra do servidor para a planilha: aplica cada operação sobre a versão atual, na ordem em
/// que chegam.
/// </summary>
/// <remarks>
/// <para>
/// Diferente do texto dos Docs, aqui não há transformação. Como toda operação aponta para
/// identificadores, e não para posições, ela continua valendo depois de qualquer outra:
/// duas edições em células diferentes se aplicam em qualquer ordem e dão o mesmo resultado. O único
/// choque possível é na mesma célula, e esse vira <see cref="SheetApplyResult.Conflict"/> com o valor
/// atual, para a pessoa decidir — nunca uma edição perdida em silêncio.
/// </para>
/// <para>Função pura: a persistência fica no serviço da aplicação, e a simulação nos testes usa esta mesma regra.</para>
/// </remarks>
public static partial class SheetSequencer
{
    public static SheetApplyResult Apply(SheetDocument document, SheetOperation operation)
    {
        switch (operation)
        {
            case SheetOperation.SetCell set:
                if (!ValidId(set.RowId) || !ValidId(set.ColumnId))
                {
                    return new SheetApplyResult.Rejected("Célula inválida.");
                }
                if (set.Value.Length > SheetDocument.MaxCellLength)
                {
                    return new SheetApplyResult.Rejected($"Uma célula aceita até {SheetDocument.MaxCellLength} caracteres.");
                }
                if (!document.Rows.Contains(set.RowId) || !document.Columns.Contains(set.ColumnId))
                {
                    return new SheetApplyResult.Conflict("A linha ou a coluna desta célula foi removida.", null);
                }

                var current = document.Get(set.RowId, set.ColumnId);
                if (current == set.Value)
                {
                    return new SheetApplyResult.Unchanged();
                }
                if (current != set.Expected)
                {
                    return new SheetApplyResult.Conflict("Outra pessoa alterou esta célula enquanto você editava.", current);
                }

                var values = document.CopyCells();
                values[SheetDocument.Key(set.RowId, set.ColumnId)] = set.Value;
                return new SheetApplyResult.Applied(document.With(values: values));

            case SheetOperation.AddRow add:
                return AddLine(document, add.RowId, isRow: true);

            case SheetOperation.AddColumn add:
                return AddLine(document, add.ColumnId, isRow: false);

            case SheetOperation.RemoveRow remove:
                return RemoveLine(document, remove.RowId, isRow: true);

            case SheetOperation.RemoveColumn remove:
                return RemoveLine(document, remove.ColumnId, isRow: false);

            default:
                return new SheetApplyResult.Rejected("Operação desconhecida.");
        }
    }

    private static SheetApplyResult AddLine(SheetDocument document, string id, bool isRow)
    {
        var lines = isRow ? document.Rows : document.Columns;
        if (!ValidId(id))
        {
            return new SheetApplyResult.Rejected("Identificador inválido.");
        }
        if (lines.Contains(id))
        {
            return new SheetApplyResult.Unchanged();
        }
        if (lines.Count >= (isRow ? SheetDocument.MaxRows : SheetDocument.MaxColumns))
        {
            return new SheetApplyResult.Rejected(isRow
                ? $"A planilha aceita até {SheetDocument.MaxRows} linhas."
                : $"A planilha aceita até {SheetDocument.MaxColumns} colunas.");
        }

        var grown = lines.Append(id).ToList();
        return new SheetApplyResult.Applied(isRow ? document.With(rows: grown) : document.With(columns: grown));
    }

    private static SheetApplyResult RemoveLine(SheetDocument document, string id, bool isRow)
    {
        var lines = isRow ? document.Rows : document.Columns;
        if (!lines.Contains(id))
        {
            // Já removida (por outra pessoa ou por um reenvio): o resultado pedido já vale.
            return new SheetApplyResult.Unchanged();
        }
        if (lines.Count <= 1)
        {
            return new SheetApplyResult.Rejected(isRow ? "A planilha precisa de pelo menos uma linha." : "A planilha precisa de pelo menos uma coluna.");
        }

        var values = document.CopyCells();
        foreach (var key in values.Keys.Where(k => isRow ? k.StartsWith(id + "|", StringComparison.Ordinal) : k.EndsWith("|" + id, StringComparison.Ordinal)).ToList())
        {
            values.Remove(key);
        }

        var shrunk = lines.Where(l => l != id).ToList();
        return new SheetApplyResult.Applied(isRow ? document.With(rows: shrunk, values: values) : document.With(columns: shrunk, values: values));
    }

    private static bool ValidId(string id) => IdPattern().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex IdPattern();
}

/// <summary>
/// Formato gravado em <c>DocPage.GridDataJson</c>. Lê também o formato antigo (lista de linhas),
/// que continua valendo até a primeira alteração colaborativa reescrever o campo.
/// </summary>
public static class SheetJson
{
    private sealed record Stored(List<string> Rows, List<string> Columns, Dictionary<string, string> Cells);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(SheetDocument document) =>
        JsonSerializer.Serialize(new Stored(document.Rows.ToList(), document.Columns.ToList(), document.Cells.ToDictionary()), Options);

    /// <summary>Nunca lança: conteúdo ilegível vira uma planilha em branco, como antes.</summary>
    public static SheetDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return SheetDocument.Blank();
        }

        try
        {
            if (json.TrimStart().StartsWith('['))
            {
                var legacy = JsonSerializer.Deserialize<List<List<string>>>(json) ?? [];
                return SheetDocument.FromGrid(legacy);
            }

            var stored = JsonSerializer.Deserialize<Stored>(json, Options);
            if (stored is null || stored.Rows is not { Count: > 0 } || stored.Columns is not { Count: > 0 })
            {
                return SheetDocument.Blank();
            }
            return new SheetDocument(stored.Rows, stored.Columns, stored.Cells ?? []);
        }
        catch (JsonException)
        {
            return SheetDocument.Blank();
        }
    }
}
