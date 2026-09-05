using System.Diagnostics;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TabletopCore.Protocol;
using Xunit;

namespace TabletopCore.Server.Tests;

/// <summary>
/// The CP2 exit criterion: whole games over a REAL SignalR connection —
/// negotiate, hub protocol, WireJson payloads, per-recipient pushes — hosted
/// in-memory by WebApplicationFactory. Transport is long polling because the
/// factory's HttpMessageHandler carries HTTP, not raw sockets; every layer
/// above the transport is exactly production.
/// </summary>
public sealed class HubIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory = new();
    private readonly List<HubConnection> _connections = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        _factory.Dispose();
    }

    /// <summary>One test client: a HubConnection plus thread-safe "latest
    /// message" slots the assertions read.</summary>
    private sealed class Player
    {
        public required HubConnection Connection { get; init; }
        public string Name { get; init; } = "";
        private readonly object _gate = new();
        private WelcomePayload? _welcome;
        private StatePayload? _state;
        private MoveRejectedPayload? _rejected;
        private ErrorPayload? _error;
        public long NextMoveId;
        public readonly List<StatePayload> AllStates = [];

        public WelcomePayload? Welcome { get { lock (_gate) return _welcome; } set { lock (_gate) _welcome = value; } }
        public StatePayload? State
        {
            get { lock (_gate) return _state; }
            set { lock (_gate) { _state = value; if (value is not null) AllStates.Add(value); } }
        }
        public MoveRejectedPayload? Rejected { get { lock (_gate) return _rejected; } set { lock (_gate) _rejected = value; } }
        public ErrorPayload? Error { get { lock (_gate) return _error; } set { lock (_gate) _error = value; } }
    }

    private async Task<Player> NewPlayer(string name)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "game"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions = WireJson.Options)
            .Build();

        var player = new Player { Connection = connection, Name = name };
        connection.On<WelcomePayload>(Wire.Welcome, p => player.Welcome = p);
        connection.On<StatePayload>(Wire.State, p => player.State = p);
        connection.On<MoveRejectedPayload>(Wire.MoveRejected, p => player.Rejected = p);
        connection.On<ErrorPayload>(Wire.Error, p => player.Error = p);
        connection.On<CardCatalog>(Wire.Catalog, _ => { });
        connection.On<LobbyPayload>(Wire.Lobby, _ => { });
        connection.On<PlayerStatusPayload>(Wire.PlayerStatus, _ => { });

        await connection.StartAsync(TestContext.Current.CancellationToken);
        _connections.Add(connection);
        return player;
    }

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Timed out waiting for {what}");
            await Task.Delay(15);
        }
    }

    private async Task<(Player Ava, Player Bo)> StartedGame()
    {
        var ava = await NewPlayer("Ava");
        var bo = await NewPlayer("Bo");

        await ava.Connection.InvokeAsync("createRoom", "Ava", "agony", TestContext.Current.CancellationToken);
        await WaitFor(() => ava.Welcome is not null, "Ava's welcome");

        await bo.Connection.InvokeAsync("joinRoom", ava.Welcome!.RoomCode, "Bo", TestContext.Current.CancellationToken);
        await WaitFor(() => bo.Welcome is not null, "Bo's welcome");

        await ava.Connection.InvokeAsync("start", TestContext.Current.CancellationToken);
        await WaitFor(() => ava.State is not null && bo.State is not null, "initial states");
        return (ava, bo);
    }

    [Fact]
    public async Task AFullGameOverTheWireReachesAWinnerAndNeverLeaksAHand()
    {
        var (ava, bo) = await StartedGame();
        var players = new[] { ava, bo };

        for (int step = 0; step < 600 && ava.State!.Winner is null; step++)
        {
            var active = players.Single(p => p.State!.View.Turn.ActivePlayer == p.State.View.Viewer
                                             && p.State.LegalMoves.Count > 0);
            long version = active.State!.Version;

            await active.Connection.InvokeAsync("submitMove",
                new MovePayload(++active.NextMoveId, active.State.LegalMoves[0]),
                TestContext.Current.CancellationToken);

            await WaitFor(() => players.All(p => p.State!.Version > version),
                $"state v>{version} after step {step}");
        }

        Assert.NotNull(ava.State!.Winner);
        Assert.Equal(ava.State.Winner, bo.State!.Winner);

        // The over-the-wire leak sweep: across every state either client ever
        // received, visible hand cards only ever belonged to the viewer.
        foreach (var player in players)
        {
            Assert.True(player.AllStates.Count > 2);
            foreach (var state in player.AllStates)
                foreach (var zone in state.View.Zones.Where(z => z.Owner is not null && z.Cards is not null))
                    Assert.Equal(state.View.Viewer, zone.Owner);
        }
    }

    [Fact]
    public async Task PlayingOutOfTurnIsRejectedOverTheWire()
    {
        var (ava, bo) = await StartedGame();
        var idle = new[] { ava, bo }.Single(p => p.State!.View.Turn.ActivePlayer != p.State.View.Viewer);

        await idle.Connection.InvokeAsync("submitMove",
            new MovePayload(1, new TabletopCore.Games.Agony.DrawCard()), TestContext.Current.CancellationToken);

        await WaitFor(() => idle.Rejected is not null, "rejection");
        Assert.Equal(1, idle.Rejected!.MoveId);
    }

    [Fact]
    public async Task DroppingAndResumingRestoresTheSameSeatAndHand()
    {
        var (ava, bo) = await StartedGame();
        string token = bo.Welcome!.SessionToken;
        var handBefore = OwnHandIds(bo.State!);

        await bo.Connection.StopAsync(TestContext.Current.CancellationToken);

        var reborn = await NewPlayer("Bo (back)");
        await reborn.Connection.InvokeAsync("resume", token, TestContext.Current.CancellationToken);
        await WaitFor(() => reborn.State is not null, "resumed state");

        Assert.Equal(bo.Welcome.Seat, reborn.Welcome!.Seat);
        Assert.Equal(handBefore, OwnHandIds(reborn.State!));
        Assert.Null(reborn.Error);
    }

    private static IReadOnlyList<int> OwnHandIds(StatePayload state)
        => state.View.Zones.Single(z => z.Owner == state.View.Viewer).Cards!
            .Select(c => c.Id.Value)
            .ToList();
}
