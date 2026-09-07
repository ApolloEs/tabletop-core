using TabletopCore.Engine;

namespace TabletopCore.Games.Agony;

/// <summary>
/// Seat order like the engine's round robin, but players who have gone out
/// are stepped over — needed the moment PlayForPlacings lets a game run past
/// the first empty hand. Swapped in at Setup through GameState.TurnSystem,
/// which exists precisely so a game can answer "who acts next?" its own way
/// instead of the engine guessing.
/// </summary>
public sealed class AgonyTurnSystem : ITurnSystem
{
    public PlayerId GetNextPlayer(GameState state)
    {
        var players = state.Players.Values.OrderBy(p => p.Seat).ToList();
        int index = players.FindIndex(p => p.Id == state.Turn.ActivePlayer);
        if (index < 0)
            throw new InvalidOperationException($"Active player '{state.Turn.ActivePlayer}' is not in the game.");

        int step = (int)state.Turn.Direction;
        for (int hop = 1; hop <= players.Count; hop++)
        {
            int next = ((index + step * hop) % players.Count + players.Count) % players.Count;
            if (StillHolding(state, players[next].Id))
                return players[next].Id;
        }

        // Nobody else holds cards — the game is over anyway, so the turn
        // stays put rather than looping forever looking for a taker.
        return state.Turn.ActivePlayer;
    }

    private static bool StillHolding(GameState state, PlayerId player)
        => state.Zones.TryGetValue(AgonyGame.HandOf(state, player), out var hand) && hand.Count > 0;
}
