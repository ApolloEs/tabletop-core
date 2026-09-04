using TabletopCore.Engine.Events;

namespace TabletopCore.Engine.Actions;

/// <summary>Moves a card to a zone. Position null appends to the top;
/// otherwise the card is inserted at that index (clamped).
/// Ownership follows the destination: an owned zone adopts the card,
/// an unowned zone (discard, table) clears the previous owner.</summary>
public sealed record MoveCardAction(PlayerId Actor, CardInstanceId Card, ZoneId To, int? Position = null)
    : GameAction(Actor)
{
    internal override string? Validate(GameState state)
    {
        if (!state.Cards.ContainsKey(Card))
            return $"Unknown card '{Card}'.";
        if (!state.Zones.ContainsKey(To))
            return $"Unknown zone '{To}'.";
        return null;
    }

    internal override void Apply(GameState state, List<GameEvent> events)
    {
        var card = state.GetCard(Card);
        var from = card.Zone;
        var to = state.GetZone(To);
        state.MoveCard(card, to, Position);
        card.Owner = to.Owner;
        events.Add(new CardMoved(Card, from, To));
    }
}

public sealed record FlipCardAction(PlayerId Actor, CardInstanceId Card, bool FaceUp) : GameAction(Actor)
{
    internal override string? Validate(GameState state)
        => state.Cards.ContainsKey(Card) ? null : $"Unknown card '{Card}'.";

    internal override void Apply(GameState state, List<GameEvent> events)
    {
        state.GetCard(Card).FaceUp = FaceUp;
        events.Add(new CardFlipped(Card, FaceUp));
    }
}

/// <summary>Fisher–Yates over the zone's card order, driven by the state's
/// seeded RNG — server-side, deterministic per seed, clients never see it.</summary>
public sealed record ShuffleAction(PlayerId Actor, ZoneId Zone) : GameAction(Actor)
{
    internal override string? Validate(GameState state)
        => state.Zones.ContainsKey(Zone) ? null : $"Unknown zone '{Zone}'.";

    internal override void Apply(GameState state, List<GameEvent> events)
    {
        var cards = state.GetZone(Zone).CardsInternal;
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = state.Rng.NextInt(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        events.Add(new ZoneShuffled(Zone, cards.Count));
    }
}

/// <summary>Takes cards off the top of one zone onto the top of another.
/// Ownership follows the destination zone, as with MoveCardAction.</summary>
public sealed record DrawAction(PlayerId Actor, ZoneId From, ZoneId To, int Count = 1) : GameAction(Actor)
{
    internal override string? Validate(GameState state)
    {
        if (!state.Zones.ContainsKey(From))
            return $"Unknown zone '{From}'.";
        if (!state.Zones.ContainsKey(To))
            return $"Unknown zone '{To}'.";
        if (Count < 1)
            return "Draw count must be at least 1.";
        if (state.GetZone(From).Count < Count)
            return $"Zone '{From}' has {state.GetZone(From).Count} card(s), cannot draw {Count}.";
        return null;
    }

    internal override void Apply(GameState state, List<GameEvent> events)
    {
        var from = state.GetZone(From);
        var to = state.GetZone(To);
        for (int i = 0; i < Count; i++)
        {
            var card = state.GetCard(from.CardsInternal[^1]);
            state.MoveCard(card, to, position: null);
            card.Owner = to.Owner;
            events.Add(new CardDrawn(Actor, card.Id, From, To));
        }
    }
}

/// <summary>Ends the actor's turn, skipping the next <paramref name="Skip"/>
/// players in turn order (skip cards, +2 victims).</summary>
public sealed record EndTurnAction(PlayerId Actor, int Skip = 0) : GameAction(Actor)
{
    internal override string? Validate(GameState state)
    {
        if (state.Turn.ActivePlayer != Actor)
            return $"It is not {Actor}'s turn.";
        if (Skip < 0)
            return "Skip count cannot be negative.";
        return null;
    }

    internal override void Apply(GameState state, List<GameEvent> events)
    {
        events.Add(new TurnEnded(Actor, state.Turn.TurnNumber));
        for (int i = 0; i <= Skip; i++)
        {
            state.Turn.ActivePlayer = state.TurnSystem.GetNextPlayer(state);
            if (i < Skip)
                events.Add(new TurnSkipped(state.Turn.ActivePlayer));
        }
        state.Turn.TurnNumber++;
        events.Add(new TurnStarted(state.Turn.ActivePlayer, state.Turn.TurnNumber));
    }
}

/// <summary>Sets a named counter on a zone — the general home for table
/// state games need (current color, pending draws, scores, chips).</summary>
public sealed record SetCounterAction(PlayerId Actor, ZoneId Zone, string Name, int Value) : GameAction(Actor)
{
    internal override string? Validate(GameState state)
    {
        if (!state.Zones.ContainsKey(Zone))
            return $"Unknown zone '{Zone}'.";
        if (string.IsNullOrWhiteSpace(Name))
            return "Counter name cannot be empty.";
        return null;
    }

    internal override void Apply(GameState state, List<GameEvent> events)
    {
        state.GetZone(Zone).SetCounter(Name, Value);
        events.Add(new CounterChanged(Zone, Name, Value));
    }
}

public sealed record SetTurnDirectionAction(PlayerId Actor, TurnDirection Direction) : GameAction(Actor)
{
    internal override string? Validate(GameState state) => null;

    internal override void Apply(GameState state, List<GameEvent> events)
    {
        state.Turn.Direction = Direction;
        events.Add(new TurnDirectionChanged(Direction));
    }
}
