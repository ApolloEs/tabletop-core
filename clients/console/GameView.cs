using TabletopCore.Engine;
using TabletopCore.Engine.Events;
using TabletopCore.Engine.Projection;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;

namespace TabletopCore.ConsoleClient;

/// <summary>Pure formatting: StatePayload + catalog in, strings out. No
/// connection state, no Console calls — the console twin of a renderer.</summary>
public static class GameView
{
    public static string Render(
        StatePayload state,
        IReadOnlyDictionary<CardDefinitionId, CardCatalogEntry> catalog)
    {
        var view = state.View;
        var lines = new List<string> { "" };

        var discard = view.Zones.First(z => z.Id == AgonyGame.DiscardZone);
        var deck = view.Zones.First(z => z.Id == AgonyGame.DeckZone);
        var table = view.Zones.First(z => z.Id == AgonyGame.TableZone);

        var topCard = discard.Cards is { Count: > 0 } cards ? cards[^1] : null;
        string top = topCard?.Definition is { } definition ? Name(catalog, definition) : "—";
        var color = (AgonyColor)table.Counters.GetValueOrDefault(AgonyGame.ActiveColorCounter);
        int pending = table.Counters.GetValueOrDefault(AgonyGame.PendingDrawCounter);
        string arrow = view.Turn.Direction == TurnDirection.Forward ? "→" : "←";

        lines.Add($"  Discard: {top}   color: {AgonyColors.Name(color)}   direction: {arrow}   deck: {deck.CardCount}");
        if (pending > 0)
            lines.Add($"  !! pending draw debt: +{pending}");

        foreach (var player in view.Players)
        {
            var hand = view.Zones.First(z => z.Owner == player.Id);
            string who = player.Id == view.Viewer ? "you" : player.DisplayName;
            string turn = player.Id == view.Turn.ActivePlayer ? "  ◄ turn" : "";
            lines.Add($"  {who,-12} {hand.CardCount} card(s){turn}");
        }

        foreach (var gameEvent in state.Events)
        {
            string? described = Describe(gameEvent, view);
            if (described is not null)
                lines.Add($"  · {described}");
        }

        if (state.Winner is { } winner)
            lines.Add($"  ***** {NameOf(view, winner)} WINS *****");
        else if (state.Finished)
            lines.Add("  ***** game ended by the host *****");

        var myHand = view.Zones.First(z => z.Owner == view.Viewer);
        lines.Add($"  your hand: {string.Join("  ", (myHand.Cards ?? []).Select(c => Name(catalog, c.Definition!.Value)))}");

        if (state.LegalMoves.Count > 0)
        {
            lines.Add("  your moves:");
            for (int i = 0; i < state.LegalMoves.Count; i++)
                lines.Add($"    {i + 1}) {Describe(state.LegalMoves[i], view, catalog)}");
        }
        else if (!state.Finished)
        {
            lines.Add($"  waiting for {NameOf(view, view.Turn.ActivePlayer)}…");
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static string Describe(
        GameMove move,
        PlayerView view,
        IReadOnlyDictionary<CardDefinitionId, CardCatalogEntry> catalog) => move switch
    {
        PlayCard play => $"play {NameOfCard(view, catalog, play.Card)}"
            + (play.DeclaredColor is { } declared ? $" declaring {AgonyColors.Name(declared)}" : ""),
        DrawCard => "draw",
        PassTurn => "pass",
        _ => move.GetType().Name,
    };

    private static string? Describe(GameEvent gameEvent, PlayerView view) => gameEvent switch
    {
        CardsDrawnHidden drawn => $"{NameOf(view, drawn.Player)} drew {drawn.Count} card(s)",
        TurnSkipped skipped => $"{NameOf(view, skipped.Player)} was skipped",
        TurnDirectionChanged changed => $"direction is now {(changed.Direction == TurnDirection.Forward ? "→" : "←")}",
        _ => null, // everything else is visible in the snapshot itself
    };

    private static string Name(IReadOnlyDictionary<CardDefinitionId, CardCatalogEntry> catalog, CardDefinitionId id)
        => catalog.TryGetValue(id, out var entry) ? entry.Name : id.Value;

    private static string NameOfCard(
        PlayerView view,
        IReadOnlyDictionary<CardDefinitionId, CardCatalogEntry> catalog,
        CardInstanceId card)
    {
        var mine = view.Zones.First(z => z.Owner == view.Viewer);
        var found = mine.Cards?.FirstOrDefault(c => c.Id == card);
        return found?.Definition is { } definition ? Name(catalog, definition) : $"card #{card.Value}";
    }

    private static string NameOf(PlayerView view, PlayerId player)
        => player == view.Viewer ? "you" : view.Players.First(p => p.Id == player).DisplayName;
}
