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

/// <summary>Distinct from CardMoved: carries the drawing player, and the
/// projection layer redacts the card identity from everyone else
/// ("player 2 drew 1 from deck").</summary>
public sealed record CardDrawn(PlayerId Player, CardInstanceId Card, ZoneId From, ZoneId To) : GameEvent;

public sealed record TurnEnded(PlayerId Player, int TurnNumber) : GameEvent;

public sealed record TurnStarted(PlayerId Player, int TurnNumber) : GameEvent;

public sealed record TurnSkipped(PlayerId Player) : GameEvent;

public sealed record TurnDirectionChanged(TurnDirection Direction) : GameEvent;

public sealed record CounterChanged(ZoneId Zone, string Name, int Value) : GameEvent;
