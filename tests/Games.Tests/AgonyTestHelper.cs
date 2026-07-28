using TabletopCore.Engine;
using TabletopCore.Engine.Actions;
using TabletopCore.Games.Agony;

namespace TabletopCore.Games.Tests;

/// <summary>
/// Test setup goes through the engine's public actions only — the same
/// boundary the game itself is held to. Freeform actions are legal without a
/// game validating them, so tests can engineer any position they need.
/// </summary>
internal static class AgonyTestHelper
{
    public static GameState NewGame(out List<Player> players, out AgonyGame game,
        int playerCount = 3, AgonyConfig? config = null, ulong seed = 42)
    {
        game = new AgonyGame(config);
        var state = new GameState(seed);
        players = [];
        for (int i = 0; i < playerCount; i++)
            players.Add(state.AddPlayer($"Player{i}"));
        game.Setup(state);
        return state;
    }

    /// <summary>Puts an instance of the given definition into the player's
    /// hand (prefers one from the deck).</summary>
    public static CardInstance Give(GameState state, Player player, string definitionId)
    {
        var hand = AgonyGame.HandOf(state, player.Id);
        var instances = state.Cards.Values
            .Where(c => c.Definition == new CardDefinitionId(definitionId))
            .ToList();
        var already = instances.FirstOrDefault(c => c.Zone == hand);
        if (already is not null)
            return already;

        var card = instances.OrderBy(c => c.Zone == AgonyGame.DeckZone ? 0 : 1).First();
        Must(state.Apply(new MoveCardAction(player.Id, card.Id, hand)));
        return card;
    }

    /// <summary>Puts an instance of the given (colored) definition on top of
    /// the discard face-up and makes its color the active color.</summary>
    public static CardInstance SetTop(GameState state, Player actor, string definitionId)
    {
        var card = state.Cards.Values
            .Where(c => c.Definition == new CardDefinitionId(definitionId))
            .OrderBy(c => c.Zone == AgonyGame.DeckZone ? 0 : 1)
            .First();
        Must(state.Apply(new MoveCardAction(actor.Id, card.Id, AgonyGame.DiscardZone)));
        Must(state.Apply(new FlipCardAction(actor.Id, card.Id, FaceUp: true)));
        var color = AgonyColors.Parse(state.GetValue(card.Id, "color").AsString);
        Must(state.Apply(new SetCounterAction(actor.Id, AgonyGame.TableZone, AgonyGame.ActiveColorCounter, (int)color)));
        return card;
    }

    public static IReadOnlyList<CardInstanceId> Hand(GameState state, Player player)
        => state.GetZone(AgonyGame.HandOf(state, player.Id)).Cards;

    public static void Must(ActionResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException($"Test setup action rejected: {result.Error}");
    }

    public static void Must(MoveResult result)
    {
        if (!result.Success)
            throw new InvalidOperationException($"Move unexpectedly rejected: {result.Error}");
    }
}
