namespace TabletopCore.Games.Agony;

/// <summary>
/// House-rule toggles (L8) — what friends argue about before a game. Passed
/// to setup; surfaced as lobby toggles. Defaults are the plain rules.
/// </summary>
public sealed record AgonyConfig
{
    /// <summary>A pending draw may be answered with another draw card
    /// instead of collecting, accumulating the debt. The ladder: a +2 can be
    /// answered with a +2 or a Wild +4, but once a +4 is on the table only
    /// another +4 stops it — so +4 stays the strongest card in the deck.</summary>
    public bool StackDrawCards { get; init; }

    /// <summary>Adds the dedicated Swap (trade hands with the next player) and
    /// Rotate (all hands move one seat in play direction) cards to the deck.
    /// Deliberately separate card types — the numerals stay plain.</summary>
    public bool SwapRotateCards { get; init; }

    /// <summary>Off: the first player to empty their hand wins and the game
    /// ends. On: play continues, each player who goes out taking the next
    /// placing, until only one still holds cards — so 4 players produce a
    /// 1st/2nd/3rd and a loser.</summary>
    public bool PlayForPlacings { get; init; }

    /// <summary>Play an identical card out of turn, stealing the turn.
    /// Not implemented yet.</summary>
    public bool JumpIn { get; init; }
}
