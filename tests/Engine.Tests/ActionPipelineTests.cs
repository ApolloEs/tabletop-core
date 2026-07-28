using TabletopCore.Engine;
using TabletopCore.Engine.Actions;
using TabletopCore.Engine.Events;
using Xunit;

namespace TabletopCore.Engine.Tests;

public class ActionPipelineTests
{
    [Fact]
    public void DrawMovesTheTopCardFromDeckToHand()
    {
        var state = TestGame.Create(seed: 42, out var alice, out _);
        var hand = TestGame.HandOf(alice);
        var topCard = state.GetZone(TestGame.Deck).Cards[^1];

        var result = state.Apply(new DrawAction(alice.Id, TestGame.Deck, hand));

        Assert.True(result.Success);
        Assert.Equal(19, state.GetZone(TestGame.Deck).Count);
        Assert.Equal(1, state.GetZone(hand).Count);

        var card = state.GetCard(topCard);
        Assert.Equal(hand, card.Zone);
        Assert.Equal(alice.Id, card.Owner);

        var drawn = Assert.IsType<CardDrawn>(Assert.Single(result.Events));
        Assert.Equal(alice.Id, drawn.Player);
        Assert.Equal(topCard, drawn.Card);
    }

    [Fact]
    public void OverdrawingIsRejectedAndChangesNothing()
    {
        var state = TestGame.Create(seed: 42, out var alice, out _);

        var result = state.Apply(new DrawAction(alice.Id, TestGame.Deck, TestGame.HandOf(alice), Count: 21));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(20, state.GetZone(TestGame.Deck).Count);
        Assert.Empty(state.EventLog);
    }

    [Fact]
    public void MoveCardAppendsToTheTopOfTheTargetZone()
    {
        var state = TestGame.Create(seed: 42, out var alice, out _);
        var card = state.GetZone(TestGame.Deck).Cards[0];

        var result = state.Apply(new MoveCardAction(alice.Id, card, TestGame.Discard));

        Assert.True(result.Success);
        Assert.Equal(card, state.GetZone(TestGame.Discard).Cards[^1]);
        Assert.Equal(TestGame.Discard, state.GetCard(card).Zone);
        var moved = Assert.IsType<CardMoved>(Assert.Single(result.Events));
        Assert.Equal(TestGame.Deck, moved.From);
        Assert.Equal(TestGame.Discard, moved.To);
    }

    [Fact]
    public void MovingAnUnknownCardIsRejected()
    {
        var state = TestGame.Create(seed: 42, out var alice, out _);

        var result = state.Apply(new MoveCardAction(alice.Id, new CardInstanceId(9999), TestGame.Discard));

        Assert.False(result.Success);
        Assert.Empty(state.EventLog);
    }

    [Fact]
    public void ActionsFromUnknownPlayersAreRejected()
    {
        var state = TestGame.Create(seed: 42, out _, out _);

        var result = state.Apply(new ShuffleAction(new PlayerId(99), TestGame.Deck));

        Assert.False(result.Success);
        Assert.Empty(state.EventLog);
    }

    [Fact]
    public void FlipSetsFaceUpAndEmitsCardFlipped()
    {
        var state = TestGame.Create(seed: 42, out var alice, out _);
        var card = state.GetZone(TestGame.Deck).Cards[0];

        var result = state.Apply(new FlipCardAction(alice.Id, card, FaceUp: true));

        Assert.True(state.GetCard(card).FaceUp);
        var flipped = Assert.IsType<CardFlipped>(Assert.Single(result.Events));
        Assert.True(flipped.FaceUp);
    }

    [Fact]
    public void OnlyTheActivePlayerMayEndTheTurn()
    {
        var state = TestGame.Create(seed: 42, out var alice, out var bob);

        var rejected = state.Apply(new EndTurnAction(bob.Id));
        Assert.False(rejected.Success);
        Assert.Equal(alice.Id, state.Turn.ActivePlayer);

        var applied = state.Apply(new EndTurnAction(alice.Id));
        Assert.True(applied.Success);
        Assert.Equal(bob.Id, state.Turn.ActivePlayer);
        Assert.Equal(2, state.Turn.TurnNumber);
        Assert.Collection(applied.Events,
            e => Assert.Equal(new TurnEnded(alice.Id, 1), e),
            e => Assert.Equal(new TurnStarted(bob.Id, 2), e));
    }

    [Fact]
    public void RoundRobinWrapsAroundTheTable()
    {
        var state = TestGame.Create(seed: 42, out var alice, out var bob);

        state.Apply(new EndTurnAction(alice.Id));
        state.Apply(new EndTurnAction(bob.Id));

        Assert.Equal(alice.Id, state.Turn.ActivePlayer);
        Assert.Equal(3, state.Turn.TurnNumber);
    }

    [Fact]
    public void ReversedDirectionWalksSeatsBackwards()
    {
        var state = new GameState(seed: 1);
        var p0 = state.AddPlayer("P0");
        state.AddPlayer("P1");
        var p2 = state.AddPlayer("P2");

        state.Turn.Direction = TurnDirection.Reversed;
        state.Apply(new EndTurnAction(p0.Id));

        Assert.Equal(p2.Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void EventLogAccumulatesInOrder()
    {
        var state = TestGame.Create(seed: 42, out var alice, out _);
        var hand = TestGame.HandOf(alice);

        state.Apply(new ShuffleAction(alice.Id, TestGame.Deck));
        state.Apply(new DrawAction(alice.Id, TestGame.Deck, hand, Count: 2));
        state.Apply(new EndTurnAction(alice.Id));

        Assert.Collection(state.EventLog,
            e => Assert.IsType<ZoneShuffled>(e),
            e => Assert.IsType<CardDrawn>(e),
            e => Assert.IsType<CardDrawn>(e),
            e => Assert.IsType<TurnEnded>(e),
            e => Assert.IsType<TurnStarted>(e));
    }

    [Fact]
    public void GetValueReadsDefinitionPropertiesThroughTheQueryLayer()
    {
        var state = TestGame.Create(seed: 42, out _, out _);
        var card = state.Cards.Values.First(c => c.Definition == new CardDefinitionId("red-7"));

        Assert.Equal(PropertyValue.Of("red"), state.GetValue(card.Id, "color"));
        Assert.Equal(PropertyValue.Of(7), state.GetValue(card.Id, "number"));
        Assert.True(state.GetValue(card.Id, "no-such-property").IsNone);
    }

    [Fact]
    public void ReplayFromTheSameSeedAndActionsReachesTheSameState()
    {
        var first = TestGame.Create(seed: 314, out var a1, out var b1);
        var second = TestGame.Create(seed: 314, out var a2, out var b2);

        foreach (var (state, alice, bob) in new[] { (first, a1, b1), (second, a2, b2) })
        {
            state.Apply(new ShuffleAction(alice.Id, TestGame.Deck));
            state.Apply(new DrawAction(alice.Id, TestGame.Deck, TestGame.HandOf(alice), Count: 3));
            state.Apply(new EndTurnAction(alice.Id));
            state.Apply(new DrawAction(bob.Id, TestGame.Deck, TestGame.HandOf(bob), Count: 3));
        }

        Assert.Equal(
            first.Cards.Values.Select(c => (c.Id, c.Definition, c.Zone, c.Owner)),
            second.Cards.Values.Select(c => (c.Id, c.Definition, c.Zone, c.Owner)));
        Assert.Equal(first.EventLog, second.EventLog);
        Assert.Equal(first.Rng.State, second.Rng.State);
    }
}
