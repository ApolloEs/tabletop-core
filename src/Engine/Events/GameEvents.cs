namespace TabletopCore.Engine.Events;

/// <summary>
/// The typed event vocabulary (L14). Events are what applied actions emit,
/// what the append-only log stores, what renderers animate, and what game
/// rules (and later data-driven card content) hook into. Names are
/// load-bearing — standardise here, never invent ad-hoc strings.
/// </summary>
public abstract record GameEvent;

public sealed record CardMoved(CardInstanceId Card, ZoneId From, ZoneId To) : GameEvent;

public sealed record CardFlipped(CardInstanceId Card, bool FaceUp) : GameEvent;

public sealed record ZoneShuffled(ZoneId Zone, int CardCount) : GameEvent;

/// <summary>Distinct from CardMoved: carries the drawing player. Never sent
/// to other players as-is — EventProjector replaces it with CardsDrawnHidden
/// for every viewer who may not see the card's identity.</summary>
public sealed record CardDrawn(PlayerId Player, CardInstanceId Card, ZoneId From, ZoneId To) : GameEvent;

/// <summary>The redacted stand-in for CardDrawn ("player 2 drew 1 from deck"):
/// same fact, no card identity. A distinct typed event rather than a CardDrawn
/// with a nulled id, so clients can't even ask the wrong question.</summary>
public sealed record CardsDrawnHidden(PlayerId Player, ZoneId From, ZoneId To, int Count) : GameEvent;

public sealed record TurnEnded(PlayerId Player, int TurnNumber) : GameEvent;

public sealed record TurnStarted(PlayerId Player, int TurnNumber) : GameEvent;

public sealed record TurnSkipped(PlayerId Player) : GameEvent;

public sealed record TurnDirectionChanged(TurnDirection Direction) : GameEvent;

public sealed record CounterChanged(ZoneId Zone, string Name, int Value) : GameEvent;
