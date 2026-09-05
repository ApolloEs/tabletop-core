using System.Threading.Channels;
using TabletopCore.Engine;
using TabletopCore.Engine.Events;
using TabletopCore.Engine.Projection;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;

namespace TabletopCore.Server.Rooms;

public enum RoomPhase { Lobby, Playing, Finished }

/// <summary>
/// One table, actor-style: every request becomes a RoomCommand in an
/// unbounded channel, and ONE task consumes them in order. All game logic is
/// therefore single-threaded — no lock discipline to get wrong, a natural
/// total order of moves, and broadcasts that can never interleave. The room
/// owns the GameState and the IGame; nothing outside this class ever touches
/// either. Everything it tells clients goes out per-recipient through
/// IClientSender, already filtered by StateProjector/EventProjector.
/// </summary>
public sealed class Room
{
    /// <summary>A dropped seat waits this long before others are told it's
    /// abandoned; a room where every seat is disconnected this long is
    /// garbage-collected.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan SweepPeriod = TimeSpan.FromSeconds(10);

    private readonly Channel<RoomCommand> _commands = Channel.CreateUnbounded<RoomCommand>();
    private readonly IClientSender _sender;
    private readonly Action<string, Room> _registerToken;
    private readonly Func<ulong> _seedSource;
    private readonly TimeProvider _time;
    private readonly Action<Room>? _onDefunct;
    private readonly ITimer _sweepTimer;
    private readonly List<Session> _sessions = [];

    private AgonyConfig _config = new();
    private AgonyGame? _game;
    private GameState? _state;
    private long _version;

    public string Code { get; }
    public RoomPhase Phase { get; private set; }

    /// <param name="seedSource">Overridable so tests can pin the deal;
    /// production rooms draw a random seed. Either way the seed never
    /// crosses the wire.</param>
    /// <param name="time">Injectable clock — the grace sweep is tested with
    /// a fake TimeProvider, never with real waiting.</param>
    /// <param name="onDefunct">Called after this room garbage-collects
    /// itself so the registry can drop its maps.</param>
    public Room(
        string code,
        IClientSender sender,
        Action<string, Room> registerToken,
        Func<ulong>? seedSource = null,
        TimeProvider? time = null,
        Action<Room>? onDefunct = null)
    {
        Code = code;
        _sender = sender;
        _registerToken = registerToken;
        _seedSource = seedSource ?? (() => (ulong)Random.Shared.NextInt64());
        _time = time ?? TimeProvider.System;
        _onDefunct = onDefunct;
        // The timer only posts a command; the sweep itself runs in the actor
        // loop with the same single-threaded guarantees as every handler.
        _sweepTimer = _time.CreateTimer(
            _ => _commands.Writer.TryWrite(new SweepCommand(NewDone())),
            null, SweepPeriod, SweepPeriod);
        _ = Task.Run(RunAsync);
    }

    private static TaskCompletionSource<bool> NewDone()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ---- The public surface: wrap, post, await. Nothing more. ----

    public Task<bool> Join(string connectionId, string displayName)
        => Post(done => new JoinCommand(connectionId, displayName, done));

    public Task<bool> Resume(string connectionId, string token)
        => Post(done => new ResumeCommand(connectionId, token, done));

    public Task<bool> SetConfig(string connectionId, AgonyConfig config)
        => Post(done => new SetConfigCommand(connectionId, config, done));

    public Task<bool> Start(string connectionId)
        => Post(done => new StartCommand(connectionId, done));

    public Task<bool> SubmitMove(string connectionId, MovePayload payload)
        => Post(done => new SubmitMoveCommand(connectionId, payload, done));

    public Task<bool> Disconnect(string connectionId)
        => Post(done => new DisconnectCommand(connectionId, done));

    public Task<bool> EndGame(string connectionId)
        => Post(done => new EndGameCommand(connectionId, done));

    private Task<bool> Post(Func<TaskCompletionSource<bool>, RoomCommand> make)
    {
        var done = NewDone();
        if (!_commands.Writer.TryWrite(make(done)))
            done.TrySetResult(false);
        return done.Task;
    }

    // ---- The actor loop: the only code that touches room state. ----

    private async Task RunAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync())
        {
            try
            {
                bool handled = command switch
                {
                    JoinCommand c => await HandleJoin(c),
                    ResumeCommand c => await HandleResume(c),
                    SetConfigCommand c => await HandleSetConfig(c),
                    StartCommand c => await HandleStart(c),
                    SubmitMoveCommand c => await HandleSubmitMove(c),
                    DisconnectCommand c => await HandleDisconnect(c),
                    EndGameCommand c => await HandleEndGame(c),
                    SweepCommand => await HandleSweep(),
                    _ => false,
                };
                command.Done.TrySetResult(handled);
            }
            catch (Exception ex)
            {
                await TrySendError(command.ConnectionId, "internal", ex.Message);
                command.Done.TrySetResult(false);
            }
        }
    }

    private async Task<bool> HandleJoin(JoinCommand c)
    {
        if (Phase != RoomPhase.Lobby)
            return await Fail(c, "gameStarted", "This game has already started — resume your seat or join another room.");
        if (_sessions.Count >= 10)
            return await Fail(c, "roomFull", "This room is full (10 players).");

        string name = string.IsNullOrWhiteSpace(c.DisplayName)
            ? $"Player {_sessions.Count + 1}"
            : c.DisplayName.Trim();

        var session = new Session
        {
            Seat = _sessions.Count,
            DisplayName = name,
            ConnectionId = c.ConnectionId,
            // Seats become PlayerIds in seat order at start; pre-start this
            // is the value it will get, so Welcome can already carry it.
            PlayerId = new PlayerId(_sessions.Count),
        };
        _sessions.Add(session);
        _registerToken(session.Token, this);

        await SendWelcome(session);
        await BroadcastLobby();
        return true;
    }

    private async Task<bool> HandleResume(ResumeCommand c)
    {
        var session = _sessions.FirstOrDefault(s => s.Token == c.Token);
        if (session is null)
            return await Fail(c, "unknownSession", "That session token doesn't belong to this room.");

        if (session.Connected && session.ConnectionId != c.ConnectionId)
            await TrySendError(session.ConnectionId!, "sessionTakenOver",
                "This seat was resumed from another connection.");

        session.ConnectionId = c.ConnectionId;
        session.DisconnectedAt = null;
        session.Abandoned = false;
        await SendWelcome(session);

        if (Phase == RoomPhase.Lobby)
        {
            await BroadcastLobby();
        }
        else
        {
            await _sender.SendAsync(c.ConnectionId, Wire.Catalog, CardCatalog.From(_state!));
            await _sender.SendAsync(c.ConnectionId, Wire.State, BuildState(session, []));
            await BroadcastPlayerStatus(session);
        }
        return true;
    }

    private async Task<bool> HandleSetConfig(SetConfigCommand c)
    {
        if (FindByConnection(c.ConnectionId) is not { } session)
            return await Fail(c, "notInRoom", "Join a room first.");
        if (Phase != RoomPhase.Lobby)
            return await Fail(c, "gameStarted", "House rules are lobby-only.");
        if (!session.IsHost)
            return await Fail(c, "hostOnly", "Only the host changes house rules.");
        if (c.Config.JumpIn)
            return await Fail(c, "unsupported", "Jump-in isn't implemented yet.");

        _config = c.Config;
        await BroadcastLobby();
        return true;
    }

    private async Task<bool> HandleStart(StartCommand c)
    {
        if (FindByConnection(c.ConnectionId) is not { } session)
            return await Fail(c, "notInRoom", "Join a room first.");
        if (Phase != RoomPhase.Lobby)
            return await Fail(c, "gameStarted", "The game is already running.");
        if (!session.IsHost)
            return await Fail(c, "hostOnly", "Only the host starts the game.");
        if (_sessions.Count < 2)
            return await Fail(c, "needPlayers", "Agony needs at least 2 players.");

        _state = new GameState(seed: _seedSource());
        foreach (var s in _sessions)
            s.PlayerId = _state.AddPlayer(s.DisplayName).Id;

        _game = new AgonyGame(_config);
        _game.Setup(_state);
        Phase = RoomPhase.Playing;
        _version = 1;

        var catalog = CardCatalog.From(_state);
        foreach (var s in ConnectedSessions())
        {
            await _sender.SendAsync(s.ConnectionId!, Wire.Catalog, catalog);
            await _sender.SendAsync(s.ConnectionId!, Wire.State, BuildState(s, []));
        }
        return true;
    }

    private async Task<bool> HandleSubmitMove(SubmitMoveCommand c)
    {
        if (FindByConnection(c.ConnectionId) is not { } session)
            return await Fail(c, "notInRoom", "Join a room first.");
        if (Phase == RoomPhase.Lobby)
            return await Fail(c, "notPlaying", "The game hasn't started yet.");

        long moveId = c.Payload.MoveId;

        // Dedupe: a move id at or below the last one seen is a resend (the
        // client re-posting after a reconnect). Answer with the stored reply
        // and a fresh snapshot; never apply twice.
        if (moveId <= session.LastMoveId)
        {
            if (session.LastMoveReply is { } stored)
                await _sender.SendAsync(c.ConnectionId, StoredReplyMethod(stored), stored);
            await _sender.SendAsync(c.ConnectionId, Wire.State, BuildState(session, []));
            return true;
        }
        session.LastMoveId = moveId;

        if (Phase == RoomPhase.Finished)
            return await RejectMove(session, moveId, "The game is over.");

        var result = _game!.TryMove(_state!, session.PlayerId, c.Payload.Move);
        if (!result.Success)
            return await RejectMove(session, moveId, result.Error ?? "Illegal move.");

        _version++;
        var reply = new MoveAcceptedPayload(moveId, _version);
        session.LastMoveReply = reply;
        await _sender.SendAsync(c.ConnectionId, Wire.MoveAccepted, reply);

        if (_game.GetWinner(_state!) is not null)
            Phase = RoomPhase.Finished;

        foreach (var s in ConnectedSessions())
            await _sender.SendAsync(s.ConnectionId!, Wire.State, BuildState(s, result.Events));
        return true;
    }

    private async Task<bool> HandleDisconnect(DisconnectCommand c)
    {
        var session = FindByConnection(c.ConnectionId);
        if (session is null)
            return true; // never joined, or already replaced by a resume

        session.ConnectionId = null;
        session.DisconnectedAt = _time.GetUtcNow();
        if (Phase == RoomPhase.Lobby)
            await BroadcastLobby();
        else
            await BroadcastPlayerStatus(session);
        return true;
    }

    private async Task<bool> HandleEndGame(EndGameCommand c)
    {
        if (FindByConnection(c.ConnectionId) is not { } session)
            return await Fail(c, "notInRoom", "Join a room first.");
        if (!session.IsHost)
            return await Fail(c, "hostOnly", "Only the host can end the game.");
        if (Phase != RoomPhase.Playing)
            return await Fail(c, "notPlaying", "There is no running game to end.");

        Phase = RoomPhase.Finished;
        foreach (var s in ConnectedSessions())
            await _sender.SendAsync(s.ConnectionId!, Wire.State, BuildState(s, []));
        return true;
    }

    private async Task<bool> HandleSweep()
    {
        var now = _time.GetUtcNow();

        // Lobby seats that never came back simply leave the table (later
        // joiners keep their seats; the host seat never expires, so seat 0
        // stays meaningful).
        if (Phase == RoomPhase.Lobby)
        {
            int removed = _sessions.RemoveAll(s =>
                !s.IsHost && !s.Connected && now - s.DisconnectedAt > GracePeriod);
            if (removed > 0)
            {
                for (int i = 0; i < _sessions.Count; i++)
                    _sessions[i].Seat = i;
                await BroadcastLobby();
            }
        }
        else
        {
            // Mid-game seats are never evicted — the hand must survive a
            // resume — but the grace expiring is announced once, and the
            // host may end a game stalled on an abandoned turn.
            foreach (var session in _sessions.Where(s =>
                !s.Connected && !s.Abandoned && now - s.DisconnectedAt > GracePeriod))
            {
                session.Abandoned = true;
                await BroadcastPlayerStatus(session);
            }
        }

        // A room nobody has touched for a whole grace period shuts down.
        if (_sessions.Count == 0 ||
            _sessions.All(s => !s.Connected && now - s.DisconnectedAt > GracePeriod))
        {
            _commands.Writer.TryComplete();
            _sweepTimer.Dispose();
            _onDefunct?.Invoke(this);
        }
        return true;
    }

    // ---- Helpers (actor-loop only). ----

    private Session? FindByConnection(string connectionId)
        => _sessions.FirstOrDefault(s => s.ConnectionId == connectionId);

    private IEnumerable<Session> ConnectedSessions()
        => _sessions.Where(s => s.Connected);

    private StatePayload BuildState(Session viewer, IReadOnlyList<GameEvent> rawEvents)
        => new(
            _version,
            StateProjector.ProjectFor(_state!, viewer.PlayerId),
            Phase == RoomPhase.Finished ? [] : _game!.GetLegalMoves(_state!, viewer.PlayerId),
            EventProjector.ProjectFor(rawEvents, viewer.PlayerId),
            _game!.GetWinner(_state!),
            Finished: Phase == RoomPhase.Finished);

    private Task SendWelcome(Session session)
        => _sender.SendAsync(session.ConnectionId!, Wire.Welcome,
            new WelcomePayload(session.Token, session.PlayerId, session.Seat, Code, Wire.ProtocolVersion));

    private async Task BroadcastLobby()
    {
        var payload = new LobbyPayload(
            _sessions.Select(s => new LobbyPlayer(s.Seat, s.DisplayName, s.Connected, s.IsHost)).ToList(),
            _config,
            CanStart: Phase == RoomPhase.Lobby && _sessions.Count >= 2);
        foreach (var s in ConnectedSessions())
            await _sender.SendAsync(s.ConnectionId!, Wire.Lobby, payload);
    }

    private async Task BroadcastPlayerStatus(Session about)
    {
        var payload = new PlayerStatusPayload(about.Seat, about.Connected, about.Abandoned);
        foreach (var s in ConnectedSessions().Where(s => s != about))
            await _sender.SendAsync(s.ConnectionId!, Wire.PlayerStatus, payload);
    }

    private async Task<bool> RejectMove(Session session, long moveId, string error)
    {
        var reply = new MoveRejectedPayload(moveId, error);
        session.LastMoveReply = reply;
        if (session.Connected)
            await _sender.SendAsync(session.ConnectionId!, Wire.MoveRejected, reply);
        return true;
    }

    private static string StoredReplyMethod(object reply)
        => reply is MoveAcceptedPayload ? Wire.MoveAccepted : Wire.MoveRejected;

    private async Task<bool> Fail(RoomCommand c, string code, string message)
    {
        await TrySendError(c.ConnectionId, code, message);
        return false;
    }

    private async Task TrySendError(string connectionId, string code, string message)
    {
        try
        {
            await _sender.SendErrorAsync(connectionId, code, message);
        }
        catch
        {
            // The connection may already be gone; the error is best-effort.
        }
    }
}
