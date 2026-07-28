namespace TabletopCore.Games.Agony;

/// <summary>
/// House-rule toggles (L8) — what friends argue about before a game. Passed
/// to setup; surfaced as lobby toggles later. Defaults are the plain rules.
/// </summary>
public sealed record AgonyConfig
{
    /// <summary>A +2 may be answered with another +2, accumulating the debt.</summary>
    public bool StackDrawTwo { get; init; }

    /// <summary>Adds the dedicated Swap (trade hands with the next player) and
    /// Rotate (all hands move one seat in play direction) cards to the deck.
    /// Deliberately separate card types — the numerals stay plain.</summary>
    public bool SwapRotateCards { get; init; }

    /// <summary>Play an identical card out of turn, stealing the turn.
    /// Not implemented yet — arrives with the wire client.</summary>
    public bool JumpIn { get; init; }
}
