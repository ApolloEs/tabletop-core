using TabletopCore.Engine.Events;

namespace TabletopCore.Engine.Actions;

/// <summary>
/// Every state change, without exception, is a serializable GameAction with
/// a Validate/Apply contract. Validate/Apply are internal on purpose: the
/// action vocabulary is closed and lives in the engine assembly; games
/// compose these actions and add their own validation at the game layer.
/// Widening this later is non-breaking; the reverse is not.
/// </summary>
public abstract record GameAction(PlayerId Actor)
{
    /// <summary>Returns null when legal, otherwise a rejection reason.
    /// Must not mutate state.</summary>
    internal abstract string? Validate(GameState state);

    /// <summary>Mutates state and appends the typed events describing what
    /// happened. Only ever called after a successful Validate.</summary>
    internal abstract void Apply(GameState state, List<GameEvent> events);
}

public sealed record ActionResult
{
    private ActionResult(bool success, string? error, IReadOnlyList<GameEvent> events)
    {
        Success = success;
        Error = error;
        Events = events;
    }

    public bool Success { get; }
    public string? Error { get; }
    public IReadOnlyList<GameEvent> Events { get; }

    public static ActionResult Applied(IReadOnlyList<GameEvent> events) => new(true, null, events);
    public static ActionResult Rejected(string error) => new(false, error, []);
}
