using TabletopCore.Engine.Events;

namespace TabletopCore.Engine.Projection;

/// <summary>
/// Per-viewer event filtering — the event-stream counterpart of
/// <see cref="StateProjector"/>. Raw <see cref="GameEvent"/>s from an
/// ActionResult/MoveResult are the server's private record; anything sent to
/// a client must pass through here first.
///
/// The one redaction rule today: a <see cref="CardDrawn"/> names the card,
/// so only the player who drew receives it — every other viewer gets the
/// identityless <see cref="CardsDrawnHidden"/> instead. No other event type
/// carries card identity: instance ids are safe to send because their
/// binding to definitions is seed-randomized at setup (games are responsible
/// for that — see AgonyDeck.Populate). If a future event ever carries a
/// definition or a face-down card's meaning, its rule is added here and the
/// projection leak tests must cover it.
/// </summary>
public static class EventProjector
{
    public static IReadOnlyList<GameEvent> ProjectFor(IReadOnlyList<GameEvent> events, PlayerId viewer)
    {
        var projected = new List<GameEvent>(events.Count);
        foreach (var gameEvent in events)
            projected.Add(Project(gameEvent, viewer));
        return projected;
    }

    private static GameEvent Project(GameEvent gameEvent, PlayerId viewer)
        => gameEvent switch
        {
            CardDrawn drawn when drawn.Player != viewer
                => new CardsDrawnHidden(drawn.Player, drawn.From, drawn.To, Count: 1),
            _ => gameEvent,
        };
}
