using TabletopCore.Engine;
using TabletopCore.Engine.Actions;
using TabletopCore.Engine.Events;
using TabletopCore.Engine.Projection;
using Xunit;

namespace TabletopCore.Engine.Tests;

public class EventProjectionTests
{
    [Fact]
    public void TheDrawingPlayerSeesTheDrawnCardVerbatim()
    {
        var state = TestGame.Create(seed: 1, out var alice, out _);
        var result = state.Apply(new DrawAction(alice.Id, TestGame.Deck, TestGame.HandOf(alice)));

        var projected = EventProjector.ProjectFor(result.Events, alice.Id);

        var drawn = Assert.IsType<CardDrawn>(Assert.Single(projected));
        Assert.Equal(alice.Id, drawn.Player);
    }

    [Fact]
    public void OtherViewersGetTheIdentitylessEventInstead()
    {
        var state = TestGame.Create(seed: 1, out var alice, out var bob);
        var result = state.Apply(new DrawAction(alice.Id, TestGame.Deck, TestGame.HandOf(alice)));

        var projected = EventProjector.ProjectFor(result.Events, bob.Id);

        var hidden = Assert.IsType<CardsDrawnHidden>(Assert.Single(projected));
        Assert.Equal(new CardsDrawnHidden(alice.Id, TestGame.Deck, TestGame.HandOf(alice), Count: 1), hidden);
        Assert.DoesNotContain(projected, e => e is CardDrawn);
    }

    [Fact]
    public void AMultiCardDrawIsRedactedPerCard()
    {
        var state = TestGame.Create(seed: 1, out var alice, out var bob);
        var result = state.Apply(new DrawAction(alice.Id, TestGame.Deck, TestGame.HandOf(alice), Count: 3));

        var projected = EventProjector.ProjectFor(result.Events, bob.Id);

        Assert.Equal(3, projected.Count);
        Assert.All(projected, e => Assert.IsType<CardsDrawnHidden>(e));
    }

    [Fact]
    public void NonDrawEventsPassThroughUnchangedForEveryViewer()
    {
        var state = TestGame.Create(seed: 1, out var alice, out var bob);
        var card = state.GetZone(TestGame.Deck).Cards[0];
        var moved = state.Apply(new MoveCardAction(alice.Id, card, TestGame.Discard));
        var turned = state.Apply(new EndTurnAction(alice.Id));
        var events = moved.Events.Concat(turned.Events).ToList();

        Assert.Equal(events, EventProjector.ProjectFor(events, alice.Id));
        Assert.Equal(events, EventProjector.ProjectFor(events, bob.Id));
    }
}
