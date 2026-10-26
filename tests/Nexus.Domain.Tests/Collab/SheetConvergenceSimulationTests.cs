using Nexus.Domain.Collab;

namespace Nexus.Domain.Tests.Collab;

/// <summary>
/// Simulação da edição simultânea da planilha (#57), no mesmo espírito da simulação dos Docs:
/// vários editores, cada um olhando uma versão possivelmente velha, mandam alterações que chegam ao
/// servidor numa ordem embaralhada (atraso e reordenação da rede).
/// </summary>
/// <remarks>
/// Prova as duas promessas da planilha colaborativa:
/// <list type="number">
/// <item>edições em células diferentes nunca conflitam, não importa o quão velha era a versão de quem editou;</item>
/// <item>nenhuma edição some em silêncio: toda célula gravada termina com o seu valor, ou com o de
/// uma edição gravada depois na mesma célula, ou com a linha/coluna removida — e todo choque na
/// mesma célula volta para o editor como conflito, com o valor atual.</item>
/// </list>
/// </remarks>
public class SheetConvergenceSimulationTests
{
    private sealed record Sent(int Editor, SheetOperation Operation);

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 60; seed++)
        {
            data.Add(seed);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ConcurrentEditors_NeverLoseAnEditSilently(int seed)
    {
        var random = new Random(seed);
        var editors = random.Next(2, 5);
        var server = SheetDocument.Blank(rows: 6, columns: 4);

        // Cada editor tem a sua visão da planilha, atualizada só de vez em quando.
        var views = Enumerable.Range(0, editors).Select(_ => server).ToArray();
        var inFlight = new List<Sent>();
        var applied = new List<(SheetOperation.SetCell Set, int Order)>();
        var removedLines = new HashSet<string>();
        var order = 0;

        for (var step = 0; step < 120; step++)
        {
            var editor = random.Next(editors);
            var view = views[editor];

            // Ou o editor faz uma alteração sobre o que vê...
            if (random.NextDouble() < 0.6)
            {
                inFlight.Add(new Sent(editor, RandomOperation(random, view)));
            }
            // ...ou recebe a versão atual do servidor (o aviso de mudança chegou).
            else
            {
                views[editor] = server;
            }

            // A rede entrega uma alteração qualquer das que estão em voo (reordenação).
            if (inFlight.Count > 0 && random.NextDouble() < 0.7)
            {
                var index = random.Next(inFlight.Count);
                var sent = inFlight[index];
                inFlight.RemoveAt(index);
                Deliver(sent);
            }
        }

        // Entrega o que sobrou, embaralhado.
        foreach (var sent in inFlight.OrderBy(_ => random.Next()).ToList())
        {
            Deliver(sent);
        }

        // Promessa 2: o valor final de cada célula é o da última edição gravada nela, a menos que a
        // linha ou a coluna tenha sido removida.
        foreach (var group in applied.GroupBy(a => SheetDocument.Key(a.Set.RowId, a.Set.ColumnId)))
        {
            var last = group.OrderBy(a => a.Order).Last().Set;
            if (removedLines.Contains(last.RowId) || removedLines.Contains(last.ColumnId))
            {
                continue;
            }
            Assert.Equal(last.Value, server.Get(last.RowId, last.ColumnId));
        }

        void Deliver(Sent sent)
        {
            var before = server;
            var result = SheetSequencer.Apply(server, sent.Operation);

            switch (result)
            {
                case SheetApplyResult.Applied ok:
                    server = ok.Document;
                    if (sent.Operation is SheetOperation.SetCell written)
                    {
                        applied.Add((written, order++));
                    }
                    if (sent.Operation is SheetOperation.RemoveRow rr)
                    {
                        removedLines.Add(rr.RowId);
                    }
                    if (sent.Operation is SheetOperation.RemoveColumn rc)
                    {
                        removedLines.Add(rc.ColumnId);
                    }
                    break;

                case SheetApplyResult.Conflict conflict when sent.Operation is SheetOperation.SetCell clash:
                    // Promessa 1: só há conflito se a própria célula mudou (ou sumiu) desde a
                    // versão do editor — nunca por causa de outra célula.
                    var stillThere = before.Rows.Contains(clash.RowId) && before.Columns.Contains(clash.ColumnId);
                    if (stillThere)
                    {
                        Assert.NotEqual(clash.Expected, before.Get(clash.RowId, clash.ColumnId));
                        Assert.Equal(before.Get(clash.RowId, clash.ColumnId), conflict.Current);
                    }
                    else
                    {
                        Assert.Null(conflict.Current);
                    }
                    break;

                case SheetApplyResult.Unchanged or SheetApplyResult.Rejected:
                    break;

                default:
                    Assert.Fail($"Resultado inesperado: {result}");
                    break;
            }
        }
    }

    private static SheetOperation RandomOperation(Random random, SheetDocument view)
    {
        var roll = random.NextDouble();
        if (roll < 0.85)
        {
            var row = view.Rows[random.Next(view.Rows.Count)];
            var column = view.Columns[random.Next(view.Columns.Count)];
            return new SheetOperation.SetCell(row, column, view.Get(row, column), $"v{random.Next(1000)}");
        }
        if (roll < 0.90)
        {
            return new SheetOperation.AddRow($"n{random.Next(100_000)}");
        }
        if (roll < 0.95 && view.Rows.Count > 1)
        {
            return new SheetOperation.RemoveRow(view.Rows[random.Next(view.Rows.Count)]);
        }
        if (view.Columns.Count > 1)
        {
            return new SheetOperation.RemoveColumn(view.Columns[random.Next(view.Columns.Count)]);
        }
        return new SheetOperation.AddColumn($"n{random.Next(100_000)}");
    }
}
