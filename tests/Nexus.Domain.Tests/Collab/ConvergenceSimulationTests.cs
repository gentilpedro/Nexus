using Nexus.Domain.Collab;

namespace Nexus.Domain.Tests.Collab;

/// <summary>
/// Prova, por simulação, que o protocolo converge: vários editores alterando o mesmo documento
/// ao mesmo tempo, sobre uma rede que atrasa, reordena, perde e duplica mensagens, e que cai e
/// volta, terminam todos com o mesmo documento que o servidor guardou.
/// </summary>
/// <remarks>
/// <para>
/// O servidor da simulação usa a regra real (<see cref="DocSequencer"/>); o cliente segue o mesmo
/// algoritmo de <c>doc-collab-core.js</c>: uma alteração em voo por vez, o resto em buffer, e ao
/// receber uma operação de outra pessoa, transforma as próprias pendências sobre ela.
/// </para>
/// <para>
/// Além da convergência no fim, um invariante é checado a cada passo: o documento local de cada
/// cliente é sempre o documento do servidor na revisão que ele conhece, seguido da alteração em
/// voo e do buffer. Se ele quebrar em algum momento, o cliente está mostrando algo que nenhuma
/// sequência de operações do servidor explica — é o bug de divergência antes de ele aparecer na
/// tela.
/// </para>
/// <para>
/// A semente é fixa por caso, então uma falha é reproduzível.
/// </para>
/// </remarks>
public class ConvergenceSimulationTests
{
    private const int SeedCount = 60;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= SeedCount; seed++)
        {
            data.Add(seed);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void ConcurrentEditors_OnAnUnreliableNetwork_Converge(int seed) => RunScenario(seed);

    [Fact]
    public void Simulation_ActuallyExercisesTheConcurrentPaths()
    {
        // Sem isto, uma simulação que por acaso nunca gerasse concorrência passaria convergindo
        // trivialmente. Somadas as sementes, cada caminho difícil precisa ter acontecido de fato.
        var totals = Enumerable.Range(1, SeedCount).Select(RunScenario).Aggregate((a, b) => a + b);

        Assert.True(totals.Accepted > 500, $"aceitas: {totals.Accepted}");
        Assert.True(totals.Behind > 500, $"clientes atrasados: {totals.Behind}");
        Assert.True(totals.Duplicates > 500, $"reenvios reconhecidos: {totals.Duplicates}");
        Assert.True(totals.Rebases > 500, $"rebases de pendências: {totals.Rebases}");
    }

    private sealed record Stats(int Accepted, int Behind, int Duplicates, int Rebases)
    {
        public static Stats operator +(Stats a, Stats b) =>
            new(a.Accepted + b.Accepted, a.Behind + b.Behind, a.Duplicates + b.Duplicates, a.Rebases + b.Rebases);
    }

    private static Stats RunScenario(int seed)
    {
        var random = new Random(seed);
        var server = new SimServer();
        var network = new SimNetwork(random, server);
        var clients = Enumerable.Range(1, random.Next(2, 5)).Select(id => new SimClient(id, network)).ToList();

        for (var step = 0; step < 300; step++)
        {
            var client = clients[random.Next(clients.Count)];
            switch (random.Next(10))
            {
                case 0 or 1 or 2:
                    client.EditRandomly(random);
                    break;
                case 3 or 4 or 5:
                    network.DeliverOne(dropChance: 0.15);
                    break;
                case 6:
                    client.RequestCatchUp();
                    break;
                case 7:
                    // Resposta demorou demais: reenvia a mesma alteração, com o mesmo número.
                    client.ResendInFlight();
                    break;
                case 8:
                    // Queda de conexão: tudo o que estava na rede para esse cliente se perde.
                    network.DropEverythingFor(client.Id);
                    client.ResendInFlight();
                    break;
                default:
                    network.DuplicateOne();
                    break;
            }

            foreach (var c in clients)
            {
                c.AssertInvariant(server);
            }
        }

        Drain(network, clients, server);

        var expected = DeltaJson.Serialize(server.Document);
        foreach (var client in clients)
        {
            Assert.Equal(server.Head, client.Revision);
            Assert.Equal(expected, DeltaJson.Serialize(client.Document));
        }

        // O log do servidor, reaplicado do zero, reconstrói o documento: nada foi aplicado fora dele.
        var replayed = server.Log.Aggregate(DocDeltaPolicy.EmptyDocument(), (doc, entry) => doc.Compose(entry.Change));
        Assert.Equal(expected, DeltaJson.Serialize(replayed));

        // Reenvios e duplicatas não aplicam a mesma alteração duas vezes.
        Assert.Equal(server.Log.Count, server.Log.Select(e => (e.ClientId, e.Seq)).Distinct().Count());

        return new Stats(server.Log.Count, server.BehindCount, server.DuplicateCount, clients.Sum(c => c.RebaseCount));
    }

    [Fact]
    public void TwoEditorsTypingAtTheSamePlace_BothTextsSurvive()
    {
        // O caso que o last-write-wins perdia: os dois escrevem no mesmo ponto ao mesmo tempo.
        var random = new Random(0);
        var server = new SimServer();
        var network = new SimNetwork(random, server);
        var ana = new SimClient(1, network);
        var bia = new SimClient(2, network);

        ana.Edit(new Delta().Insert("Ana"));
        bia.Edit(new Delta().Insert("Bia"));

        Drain(network, [ana, bia], server);

        var text = string.Concat(server.Document.Ops.Select(op => op.Text));
        Assert.Contains("Ana", text);
        Assert.Contains("Bia", text);
        Assert.Equal(DeltaJson.Serialize(server.Document), DeltaJson.Serialize(ana.Document));
        Assert.Equal(DeltaJson.Serialize(server.Document), DeltaJson.Serialize(bia.Document));
    }

    private static void Drain(SimNetwork network, List<SimClient> clients, SimServer server)
    {
        for (var round = 0; round < 1000; round++)
        {
            while (network.DeliverOne(dropChance: 0))
            {
            }

            if (clients.All(c => c.IsIdle && c.Revision == server.Head))
            {
                return;
            }

            foreach (var client in clients)
            {
                client.ResendInFlight();
                client.RequestCatchUp();
            }
        }

        Assert.Fail("A simulação não estabilizou: algum cliente ficou preso com alteração pendente.");
    }

    // ---------------------------------------------------------------------------------------
    // Servidor: a regra real do DocSequencer, com log e reconhecimento de reenvio.
    // ---------------------------------------------------------------------------------------

    private sealed record LogEntry(long Revision, int ClientId, long Seq, Delta Change);

    private sealed class SimServer
    {
        private readonly Dictionary<(int, long), long> accepted = [];
        private readonly List<Delta> snapshots = [DocDeltaPolicy.EmptyDocument()];

        public int BehindCount { get; private set; }
        public int DuplicateCount { get; private set; }

        public Delta Document => snapshots[^1];
        public long Head => snapshots.Count - 1;
        public List<LogEntry> Log { get; } = [];

        public Delta SnapshotAt(long revision) => snapshots[(int)revision];

        public Message Submit(SubmitRequest request)
        {
            if (accepted.TryGetValue((request.ClientId, request.Seq), out var revision))
            {
                DuplicateCount++;
                return new AckMessage(request.ClientId, request.Seq, revision);
            }

            switch (DocSequencer.Decide(Document, Head, request.BaseRevision, request.Change, isSeed: false))
            {
                case SubmitDecision.Accepted ok:
                    snapshots.Add(ok.Document);
                    Log.Add(new LogEntry(ok.Revision, request.ClientId, request.Seq, request.Change));
                    accepted[(request.ClientId, request.Seq)] = ok.Revision;
                    return new AckMessage(request.ClientId, request.Seq, ok.Revision);
                case SubmitDecision.Behind:
                    BehindCount++;
                    return new OpsMessage(request.ClientId, Since(request.BaseRevision));
                case SubmitDecision.Rejected rejected:
                    throw new InvalidOperationException($"O servidor recusou uma alteração de um cliente correto: {rejected.Reason}");
                default:
                    throw new InvalidOperationException("Decisão inesperada.");
            }
        }

        public List<LogEntry> Since(long revision) => Log.Where(e => e.Revision > revision).ToList();
    }

    // ---------------------------------------------------------------------------------------
    // Rede: entrega fora de ordem, perde, duplica, e derruba a conexão de um cliente.
    // ---------------------------------------------------------------------------------------

    private abstract record Message(int ClientId);
    private sealed record SubmitRequest(int ClientId, long Seq, long BaseRevision, Delta Change) : Message(ClientId);
    private sealed record CatchUpRequest(int ClientId, long Since) : Message(ClientId);
    private sealed record AckMessage(int ClientId, long Seq, long Revision) : Message(ClientId);
    private sealed record OpsMessage(int ClientId, List<LogEntry> Entries) : Message(ClientId);

    private sealed class SimNetwork(Random random, SimServer server)
    {
        private readonly List<Message> inFlight = [];
        private readonly Dictionary<int, SimClient> clients = [];

        public void Register(SimClient client) => clients[client.Id] = client;

        public void Send(Message message) => inFlight.Add(message);

        public bool DeliverOne(double dropChance)
        {
            if (inFlight.Count == 0)
            {
                return false;
            }

            var index = random.Next(inFlight.Count);
            var message = inFlight[index];
            inFlight.RemoveAt(index);

            if (random.NextDouble() < dropChance)
            {
                return true;
            }

            switch (message)
            {
                case SubmitRequest submit:
                    inFlight.Add(server.Submit(submit));
                    break;
                case CatchUpRequest catchUp:
                    inFlight.Add(new OpsMessage(catchUp.ClientId, server.Since(catchUp.Since)));
                    break;
                case AckMessage ack:
                    clients[ack.ClientId].OnAck(ack.Seq, ack.Revision);
                    break;
                case OpsMessage ops:
                    clients[ops.ClientId].OnOps(ops.Entries);
                    break;
            }
            return true;
        }

        public void DuplicateOne()
        {
            if (inFlight.Count > 0)
            {
                inFlight.Add(inFlight[random.Next(inFlight.Count)]);
            }
        }

        public void DropEverythingFor(int clientId) => inFlight.RemoveAll(m => m.ClientId == clientId);
    }

    // ---------------------------------------------------------------------------------------
    // Cliente: o mesmo algoritmo de doc-collab-core.js.
    // ---------------------------------------------------------------------------------------

    private sealed record Pending(long Seq, Delta Change, long BaseRevision);

    private sealed class SimClient
    {
        private readonly SimNetwork network;
        private Pending? inFlight;
        private Delta? buffer;
        private long nextSeq = 1;

        public SimClient(int id, SimNetwork network)
        {
            Id = id;
            this.network = network;
            network.Register(this);
        }

        public int Id { get; }
        public Delta Document { get; private set; } = DocDeltaPolicy.EmptyDocument();
        public long Revision { get; private set; }
        public bool IsIdle => inFlight is null && buffer is null;
        public int RebaseCount { get; private set; }

        public void EditRandomly(Random random) => Edit(RandomChange(random, Document));

        public void Edit(Delta change)
        {
            Document = Document.Compose(change);
            buffer = buffer is null ? change : buffer.Compose(change);
            Flush();
        }

        public void RequestCatchUp() => network.Send(new CatchUpRequest(Id, Revision));

        public void ResendInFlight()
        {
            if (inFlight is not null)
            {
                network.Send(new SubmitRequest(Id, inFlight.Seq, inFlight.BaseRevision, inFlight.Change));
            }
        }

        public void OnAck(long seq, long revision)
        {
            if (inFlight is null || inFlight.Seq != seq)
            {
                return; // resposta antiga: essa alteração já foi reconhecida pelo log
            }

            // Aceita só na revisão em que foi escrita, então vem logo depois da que conhecemos.
            Assert.Equal(Revision + 1, revision);
            Revision = revision;
            inFlight = null;
            Flush();
        }

        public void OnOps(List<LogEntry> entries)
        {
            foreach (var entry in entries.OrderBy(e => e.Revision))
            {
                if (entry.Revision <= Revision)
                {
                    continue;
                }
                if (entry.Revision != Revision + 1)
                {
                    break; // buraco: o resto chega num próximo catch-up
                }

                if (entry.ClientId == Id)
                {
                    // A própria alteração, vista pelo log antes do ack.
                    Assert.NotNull(inFlight);
                    Assert.Equal(inFlight!.Seq, entry.Seq);
                    Revision = entry.Revision;
                    inFlight = null;
                    continue;
                }

                var remote = entry.Change;
                if (!IsIdle)
                {
                    RebaseCount++;
                }
                if (inFlight is not null)
                {
                    var mine = inFlight.Change;
                    var rebased = remote.Transform(mine, priority: true);
                    remote = mine.Transform(remote, priority: false);
                    // Uma alteração pode sumir no rebase (apagava o que outra pessoa já apagou);
                    // vazia não vai ao servidor.
                    inFlight = rebased.Ops.Count == 0 ? null : inFlight with { Change = rebased, BaseRevision = entry.Revision };
                }
                if (buffer is not null)
                {
                    var mine = buffer;
                    var rebased = remote.Transform(mine, priority: true);
                    remote = mine.Transform(remote, priority: false);
                    buffer = rebased.Ops.Count == 0 ? null : rebased;
                }

                Document = Document.Compose(remote);
                Revision = entry.Revision;
            }

            ResendInFlight();
            Flush();
        }

        private void Flush()
        {
            if (inFlight is null && buffer is not null)
            {
                inFlight = new Pending(nextSeq++, buffer, Revision);
                buffer = null;
                ResendInFlight();
            }
        }

        public void AssertInvariant(SimServer server)
        {
            var expected = server.SnapshotAt(Revision);
            if (inFlight is not null)
            {
                Assert.Equal(Revision, inFlight.BaseRevision);
                expected = expected.Compose(inFlight.Change);
            }
            if (buffer is not null)
            {
                expected = expected.Compose(buffer);
            }
            Assert.Equal(DeltaJson.Serialize(expected), DeltaJson.Serialize(Document));
        }
    }

    // ---------------------------------------------------------------------------------------
    // Edição aleatória válida: o que uma pessoa faria no editor.
    // ---------------------------------------------------------------------------------------

    private static readonly string[] Words = ["a", "ção", "Nexus", " ", "😀", "\n", "doc", "x"];

    private static Delta RandomChange(Random random, Delta document)
    {
        // Texto plano (embed ocupa uma posição) para nunca parar no meio de um emoji, onde o
        // cursor do editor também não para.
        var text = string.Concat(document.Ops.Select(op => op.Text ?? "￼"));
        var length = text.Length;
        int Boundary(int position) =>
            position > 0 && position < length && char.IsLowSurrogate(text[position]) ? position + 1 : position;

        var editable = length - 1; // a quebra de linha final não é apagada nem ultrapassada
        var change = new Delta();
        var cursor = 0;

        var steps = random.Next(1, 3);
        for (var i = 0; i < steps && cursor <= editable; i++)
        {
            var skip = Boundary(cursor + random.Next(0, editable - cursor + 1)) - cursor;
            change.Retain(skip);
            cursor += skip;

            switch (random.Next(4))
            {
                case 0 or 1:
                    change.Insert(Words[random.Next(Words.Length)] + Words[random.Next(Words.Length)], RandomAttributes(random, allowNull: false));
                    break;
                case 2 when editable - cursor > 0:
                    var size = Boundary(cursor + random.Next(1, Math.Min(editable - cursor, 4) + 1)) - cursor;
                    change.Delete(size);
                    cursor += size;
                    break;
                default:
                    var span = Boundary(cursor + random.Next(1, length - cursor + 1)) - cursor;
                    change.Retain(span, RandomAttributes(random, allowNull: true) ?? AttributeMap.Of(("bold", AttributeValue.True)));
                    cursor += span;
                    break;
            }
        }

        change.Chop();
        return change.Ops.Count > 0 ? change : new Delta().Insert("!");
    }

    private static AttributeMap? RandomAttributes(Random random, bool allowNull)
    {
        var entries = new List<(string, AttributeValue)>();
        var count = random.Next(0, 3);
        for (var i = 0; i < count; i++)
        {
            (string key, AttributeValue value) = random.Next(5) switch
            {
                0 => ("bold", AttributeValue.True),
                1 => ("italic", AttributeValue.True),
                2 => ("header", AttributeValue.From(random.Next(1, 4))),
                3 => ("list", AttributeValue.From(random.Next(2) == 0 ? "ordered" : "bullet")),
                _ => ("link", AttributeValue.From("https://nexus.example/" + random.Next(3))),
            };
            if (allowNull && random.Next(3) == 0)
            {
                value = AttributeValue.Null;
            }
            entries.Add((key, value));
        }
        return AttributeMap.Of([.. entries]);
    }
}
