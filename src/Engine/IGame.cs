using TabletopCore.Engine.Events;

namespace TabletopCore.Engine;

/// <summary>
/// A move in some game's own vocabulary (PlayCard, Bet, MoveToken…) —
/// serializable, and the thing a client actually sends. The acting player is
/// never part of the move; the server derives it from the connection, so a
/// client cannot act as someone else.
/// </summary>
public abstract record GameMove;

/// <summary>
/// The contract a game class implements (L6). Games live outside the engine
/// assembly and drive state exclusively through the public action pipeline:
/// TryMove validates a move against the game's rules, then expands it into
/// engine actions applied via <see cref="GameState.Apply"/>.
/// </summary>
public interface IGame
{
    string Name { get; }

    /// <summary>Builds zones, definitions, and cards, shuffles and deals.
    /// Players must already be added. Must be deterministic given the
    /// state's seed — setup is part of the replay contract.</summary>
    void Setup(GameState state);

    MoveResult TryMove(GameState state, PlayerId player, GameMove move);

    /// <summary>The concrete moves this player could legally make right now —
    /// what the server sends so clients never guess.</summary>
    IReadOnlyList<GameMove> GetLegalMoves(GameState state, PlayerId player);

    /// <summary>Null while the game is running.</summary>
    PlayerId? GetWinner(GameState state);
}

public sealed record MoveResult
{
    private MoveResult(bool success, string? error, IReadOnlyList<GameEvent> events)
    {
        Success = success;
        Error = error;
        Events = events;
    }

    public bool Success { get; }
    public string? Error { get; }
    public IReadOnlyList<GameEvent> Events { get; }

    public static MoveResult Applied(IReadOnlyList<GameEvent> events) => new(true, null, events);
    public static MoveResult Rejected(string error) => new(false, error, []);
}
