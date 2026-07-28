using TabletopCore.Engine;
using TabletopCore.Engine.Actions;
using TabletopCore.Engine.Projection;
using Xunit;

namespace TabletopCore.Engine.Tests;

/// <summary>
/// The tests that matter most (L3): the filtered projection must hide the
/// right cards from the right players.
/// </summary>
public class ProjectionTests
{
    private static GameState DealtGame(out Player alice, out Player bob)
    {
        var state = TestGame.Create(seed: 42, out alice, out bob);
        state.Apply(new ShuffleAction(alice.Id, TestGame.Deck));
        state.Apply(new DrawAction(alice.Id, TestGame.Deck, TestGame.HandOf(alice), Count: 5));
        state.Apply(new DrawAction(bob.Id, TestGame.Deck, TestGame.HandOf(bob), Count: 5));
        return state;
    }

    private static ZoneView ZoneOf(PlayerView view, ZoneId id) => view.Zones.Single(z => z.Id == id);

    [Fact]
    public void OwnerSeesTheirOwnHandInFull()
    {
        var state = DealtGame(out var alice, out _);

        var hand = ZoneOf(StateProjector.ProjectFor(state, alice.Id), TestGame.HandOf(alice));

        Assert.NotNull(hand.Cards);
        Assert.Equal(5, hand.Cards.Count);
        Assert.All(hand.Cards, c => Assert.NotNull(c.Definition));
    }

    [Fact]
    public void OpponentHandsAppearAsCountsOnly()
    {
        var state = DealtGame(out var alice, out var bob);

        var bobsHand = ZoneOf(StateProjector.ProjectFor(state, alice.Id), TestGame.HandOf(bob));

        Assert.Null(bobsHand.Cards);
        Assert.Equal(5, bobsHand.CardCount);
    }

    [Fact]
    public void HiddenDeckShowsOnlyItsCountToEveryone()
    {
        var state = DealtGame(out var alice, out var bob);

        foreach (var viewer in new[] { alice.Id, bob.Id })
        {
            var deck = ZoneOf(StateProjector.ProjectFor(state, viewer), TestGame.Deck);
            Assert.Null(deck.Cards);
            Assert.Equal(10, deck.CardCount);
        }
    }

    [Fact]
    public void FaceUpCardsInPublicZonesAreVisibleToEveryone()
    {
        var state = DealtGame(out var alice, out var bob);
        var played = state.GetZone(TestGame.HandOf(alice)).Cards[0];
        state.Apply(new MoveCardAction(alice.Id, played, TestGame.Discard));
        state.Apply(new FlipCardAction(alice.Id, played, FaceUp: true));

        var discard = ZoneOf(StateProjector.ProjectFor(state, bob.Id), TestGame.Discard);

        Assert.NotNull(discard.Cards);
        var card = Assert.Single(discard.Cards);
        Assert.Equal(state.GetCard(played).Definition, card.Definition);
    }

    [Fact]
    public void FaceDownCardsInPublicZonesHideTheirIdentity()
    {
        var state = DealtGame(out var alice, out var bob);
        var played = state.GetZone(TestGame.HandOf(alice)).Cards[0];
        state.Apply(new MoveCardAction(alice.Id, played, TestGame.Discard));

        var discard = ZoneOf(StateProjector.ProjectFor(state, bob.Id), TestGame.Discard);

        Assert.NotNull(discard.Cards);
        var card = Assert.Single(discard.Cards);
        Assert.Null(card.Definition);
        Assert.False(card.FaceUp);
    }

    [Fact]
    public void ProjectionNeverLeaksAnotherPlayersDefinitions()
    {
        var state = DealtGame(out var alice, out var bob);

        var view = StateProjector.ProjectFor(state, alice.Id);
        var bobsCards = state.GetZone(TestGame.HandOf(bob)).Cards.ToHashSet();

        var visibleIds = view.Zones
            .Where(z => z.Cards is not null)
            .SelectMany(z => z.Cards!)
            .Select(c => c.Id);
        Assert.DoesNotContain(visibleIds, id => bobsCards.Contains(id));
    }
}
