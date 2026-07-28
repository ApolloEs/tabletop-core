using TabletopCore.Engine;
using TabletopCore.Engine.Actions;
using TabletopCore.Engine.Events;
using Xunit;

namespace TabletopCore.Engine.Tests;

public class TableActionTests
{
    [Fact]
    public void SetCounterSetsTheValueAndEmits()
    {
        var state = TestGame.Create(seed: 1, out var alice, out _);

        var result = state.Apply(new SetCounterAction(alice.Id, TestGame.Discard, "score", 5));

        Assert.True(result.Success);
        Assert.Equal(5, state.GetZone(TestGame.Discard).GetCounter("score"));
        Assert.Equal(new CounterChanged(TestGame.Discard, "score", 5), Assert.Single(result.Events));
    }

    [Fact]
    public void UnsetCountersReadAsZero()
    {
        var state = TestGame.Create(seed: 1, out _, out _);
        Assert.Equal(0, state.GetZone(TestGame.Discard).GetCounter("never-set"));
    }

    [Fact]
    public void SetTurnDirectionChangesDirectionAndEmits()
    {
        var state = TestGame.Create(seed: 1, out var alice, out _);

        var result = state.Apply(new SetTurnDirectionAction(alice.Id, TurnDirection.Reversed));

        Assert.Equal(TurnDirection.Reversed, state.Turn.Direction);
        Assert.Equal(new TurnDirectionChanged(TurnDirection.Reversed), Assert.Single(result.Events));
    }

    [Fact]
    public void EndTurnWithSkipPassesOverPlayers()
    {
        var state = new GameState(seed: 1);
        var p0 = state.AddPlayer("P0");
        var p1 = state.AddPlayer("P1");
        var p2 = state.AddPlayer("P2");

        var result = state.Apply(new EndTurnAction(p0.Id, Skip: 1));

        Assert.True(result.Success);
        Assert.Equal(p2.Id, state.Turn.ActivePlayer);
        Assert.Collection(result.Events,
            e => Assert.Equal(new TurnEnded(p0.Id, 1), e),
            e => Assert.Equal(new TurnSkipped(p1.Id), e),
            e => Assert.Equal(new TurnStarted(p2.Id, 2), e));
    }

    [Fact]
    public void MovingIntoAnOwnedZoneAdoptsTheOwner()
    {
        var state = TestGame.Create(seed: 1, out var alice, out _);
        var card = state.GetZone(TestGame.Deck).Cards[0];

        state.Apply(new MoveCardAction(alice.Id, card, TestGame.HandOf(alice)));

        Assert.Equal(alice.Id, state.GetCard(card).Owner);
    }
}
