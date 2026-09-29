using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nexus.Web.Services.Backplane;
using Nexus.Web.Services.Collab;

namespace Nexus.Web.Tests;

/// <summary>
/// Avisos de mudança dos Docs entre instâncias e presença de quem está editando.
/// </summary>
public class DocBackplaneTests
{
    [Theory]
    [InlineData(DocChangeKind.Operation)]
    [InlineData(DocChangeKind.Content)]
    [InlineData(DocChangeKind.Presence)]
    public void Envelope_RoundTrips(DocChangeKind kind)
    {
        var envelope = new DocChangeEnvelope(Guid.NewGuid(), new DocChange(Guid.NewGuid(), 42, Guid.NewGuid(), kind));

        Assert.True(DocChangeEnvelope.TryParse(envelope.ToString(), out var parsed));
        Assert.Equal(envelope, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("a|b|c|d|e")]
    [InlineData("00000000000000000000000000000001|00000000000000000000000000000002|-1|00000000000000000000000000000003|0")]
    [InlineData("00000000000000000000000000000001|00000000000000000000000000000002|5|00000000000000000000000000000003|9")]
    [InlineData("00000000000000000000000000000001|00000000000000000000000000000002|5|00000000000000000000000000000003")]
    public void Envelope_RejectsMalformedFrames(string? frame)
    {
        // Outro programa usando o mesmo Redis, ou uma versão antiga durante o deploy, não pode
        // derrubar o assinante: o quadro é só descartado.
        Assert.False(DocChangeEnvelope.TryParse(frame, out _));
    }

    [Fact]
    public async Task ChangeFromTheBackplane_IsRaisedLocallyButNeverPublishedBack()
    {
        var publisher = new RecordingPublisher();
        var notifier = new DocChangeNotifier(publisher, NullLogger<DocChangeNotifier>.Instance);
        var seen = new List<DocChange>();
        notifier.Changed += seen.Add;

        var local = new DocChange(Guid.NewGuid(), 1, Guid.NewGuid(), DocChangeKind.Operation);
        var remote = new DocChange(Guid.NewGuid(), 7, Guid.NewGuid(), DocChangeKind.Operation);
        await notifier.PublishAsync(local);
        notifier.RaiseFromRemote(remote);

        Assert.Equal([local, remote], seen);
        // Republicar o que veio do backplane faria duas instâncias devolverem o mesmo aviso uma à
        // outra para sempre.
        Assert.Equal([local], publisher.Published);
    }

    [Fact]
    public async Task OneBrokenSubscriber_DoesNotStopTheOthers()
    {
        var notifier = new DocChangeNotifier(new NullDocChangePublisher(), NullLogger<DocChangeNotifier>.Instance);
        var seen = 0;
        notifier.Changed += _ => throw new InvalidOperationException("circuito com problema");
        notifier.Changed += _ => seen++;

        await notifier.PublishAsync(new DocChange(Guid.NewGuid(), 1, Guid.Empty, DocChangeKind.Operation));

        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task Presence_ListsEachPersonOnce_AndForgetsWhoLeft()
    {
        var store = new InMemoryDocPresenceStore(new FakeTimeProvider());
        var doc = Guid.NewGuid();
        var anaTab1 = Guid.NewGuid();
        var anaTab2 = Guid.NewGuid();
        var bia = Guid.NewGuid();

        await store.JoinAsync(doc, anaTab1, new DocEditor("ana", "Ana"));
        await store.JoinAsync(doc, anaTab2, new DocEditor("ana", "Ana"));
        await store.JoinAsync(doc, bia, new DocEditor("bia", "Bia"));

        Assert.Equal(["Ana", "Bia"], (await store.ListAsync(doc)).Select(e => e.Name));

        // Ana fecha uma aba: continua editando pela outra.
        await store.LeaveAsync(doc, anaTab1);
        Assert.Equal(["Ana", "Bia"], (await store.ListAsync(doc)).Select(e => e.Name));

        await store.LeaveAsync(doc, anaTab2);
        Assert.Equal(["Bia"], (await store.ListAsync(doc)).Select(e => e.Name));
        Assert.Empty(await store.ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Presence_ExpiresWithoutRenewal_AndRenewalKeepsItAlive()
    {
        // O circuito que cai sem avisar nunca chama Leave: sem expiração, a pessoa ficaria
        // "editando" para sempre.
        var clock = new FakeTimeProvider();
        var store = new InMemoryDocPresenceStore(clock);
        var doc = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var bia = Guid.NewGuid();

        await store.JoinAsync(doc, ana, new DocEditor("ana", "Ana"));
        await store.JoinAsync(doc, bia, new DocEditor("bia", "Bia"));

        for (var i = 0; i < 4; i++)
        {
            clock.Advance(IDocPresenceStore.Heartbeat);
            await store.JoinAsync(doc, ana, new DocEditor("ana", "Ana")); // só a Ana renova
        }

        Assert.Equal(["Ana"], (await store.ListAsync(doc)).Select(e => e.Name));
    }

    [Fact]
    public async Task PresenceFollowsTheCircuitConnection_NotTheComponentLifetime()
    {
        // Aba fechada: o Blazor mantém o circuito (e o editor) vivo por minutos esperando a
        // reconexão. A presença precisa sair quando a conexão cai, não quando o circuito expira.
        var store = new InMemoryDocPresenceStore(new FakeTimeProvider());
        var notifier = new DocChangeNotifier(new NullDocChangePublisher(), NullLogger<DocChangeNotifier>.Instance);
        var presenceChanges = 0;
        notifier.Changed += c => presenceChanges += c.Kind == DocChangeKind.Presence ? 1 : 0;
        var handler = new DocPresenceCircuitHandler(store, notifier);
        var doc = Guid.NewGuid();
        var session = Guid.NewGuid();
        var ana = new DocEditor("ana", "Ana");

        handler.Register(session, doc, ana);
        await store.JoinAsync(doc, session, ana);

        await handler.OnConnectionDownAsync(null!, CancellationToken.None);
        Assert.False(handler.IsConnected);
        Assert.Empty(await store.ListAsync(doc));

        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.True(handler.IsConnected);
        Assert.Equal(["Ana"], (await store.ListAsync(doc)).Select(e => e.Name));
        Assert.Equal(2, presenceChanges);

        // Depois de sair do documento, uma queda não mexe mais nele.
        handler.Unregister(session);
        await store.LeaveAsync(doc, session);
        await handler.OnConnectionDownAsync(null!, CancellationToken.None);
        await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        Assert.Empty(await store.ListAsync(doc));
    }

    private sealed class RecordingPublisher : IDocChangePublisher
    {
        public List<DocChange> Published { get; } = [];

        public Task PublishAsync(DocChange change, CancellationToken cancellationToken = default)
        {
            Published.Add(change);
            return Task.CompletedTask;
        }
    }
}
