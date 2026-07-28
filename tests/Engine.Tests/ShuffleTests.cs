using TabletopCore.Engine;
using TabletopCore.Engine.Actions;
using TabletopCore.Engine.Events;
using Xunit;

namespace TabletopCore.Engine.Tests;

public class ShuffleTests
{
    [Fact]
    public void ShuffleIsDeterministicPerSeed()
    {
        var first = TestGame.Create(seed: 42, out var alice, out _);
        var second = TestGame.Create(seed: 42, out var alice2, out _);

        first.Apply(new ShuffleAction(alice.Id, TestGame.Deck));
        second.Apply(new ShuffleAction(alice2.Id, TestGame.Deck));

        Assert.Equal(
            first.GetZone(TestGame.Deck).Cards,
            second.GetZone(TestGame.Deck).Cards);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentOrders()
    {
        var first = TestGame.Create(seed: 1, out var alice, out _);
        var second = TestGame.Create(seed: 2, out var alice2, out _);

        first.Apply(new ShuffleAction(alice.Id, TestGame.Deck));
        second.Apply(new ShuffleAction(alice2.Id, TestGame.Deck));

        Assert.NotEqual(
            first.GetZone(TestGame.Deck).Cards,
            second.GetZone(TestGame.Deck).Cards);
    }

    [Fact]
    public void ShufflePreservesTheSetOfCards()
    {
        var state = TestGame.Create(seed: 7, out var alice, out _);
        var before = state.GetZone(TestGame.Deck).Cards.ToHashSet();

        var result = state.Apply(new ShuffleAction(alice.Id, TestGame.Deck));

        Assert.True(result.Success);
        var after = state.GetZone(TestGame.Deck).Cards;
        Assert.Equal(before.Count, after.Count);
        Assert.True(before.SetEquals(after));
    }

    [Fact]
    public void ShuffleEmitsZoneShuffledIntoTheLog()
    {
        var state = TestGame.Create(seed: 7, out var alice, out _);

        var result = state.Apply(new ShuffleAction(alice.Id, TestGame.Deck));

        var shuffled = Assert.IsType<ZoneShuffled>(Assert.Single(result.Events));
        Assert.Equal(TestGame.Deck, shuffled.Zone);
        Assert.Equal(20, shuffled.CardCount);
        Assert.Equal(result.Events, state.EventLog);
    }
}
