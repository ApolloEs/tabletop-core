namespace TabletopCore.Engine;

public enum TurnDirection
{
    Forward = 1,
    Reversed = -1,
}

public sealed class TurnState
{
    public PlayerId ActivePlayer { get; internal set; }
    public TurnDirection Direction { get; internal set; } = TurnDirection.Forward;
    public int TurnNumber { get; internal set; } = 1;
}

/// <summary>
/// "Who acts next?" — pluggable, not hardcoded. Games swap implementations
/// (or configure this one) at setup.
/// </summary>
public interface ITurnSystem
{
    PlayerId GetNextPlayer(GameState state);
}

/// <summary>Seat order, honoring <see cref="TurnState.Direction"/> (UNO-shaped).</summary>
public sealed class RoundRobinTurnSystem : ITurnSystem
{
    public PlayerId GetNextPlayer(GameState state)
    {
        var players = state.Players.Values.OrderBy(p => p.Seat).ToList();
        int index = players.FindIndex(p => p.Id == state.Turn.ActivePlayer);
        if (index < 0)
            throw new InvalidOperationException($"Active player '{state.Turn.ActivePlayer}' is not in the game.");
        int step = (int)state.Turn.Direction;
        int next = ((index + step) % players.Count + players.Count) % players.Count;
        return players[next].Id;
    }
}
