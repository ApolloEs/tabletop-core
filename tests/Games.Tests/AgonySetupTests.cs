using TabletopCore.Engine;
using TabletopCore.Games.Agony;
using Xunit;
using static TabletopCore.Games.Tests.AgonyTestHelper;

namespace TabletopCore.Games.Tests;

public class AgonySetupTests
{
    [Fact]
    public void SetupDealsSevenEachAndFlipsANumberCard()
    {
        var state = NewGame(out var players, out _, playerCount: 3);

        foreach (var player in players)
            Assert.Equal(7, Hand(state, player).Count);

        var discard = state.GetZone(AgonyGame.DiscardZone);
        var top = state.GetCard(discard.Cards[^1]);
        Assert.True(top.FaceUp);
        Assert.False(state.GetValue(top.Id, "number").IsNone);

        var activeColor = (AgonyColor)state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.ActiveColorCounter);
        Assert.Equal(state.GetValue(top.Id, "color").AsString, AgonyColors.Name(activeColor));

        // 108 cards minus 21 dealt minus the flipped starter(s).
        Assert.Equal(108 - 21 - discard.Count, state.GetZone(AgonyGame.DeckZone).Count);
        Assert.Equal(0, state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.PendingDrawCounter));
        Assert.Equal(players[0].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void SwapAndRotateCardsExistOnlyWithTheHouseRule()
    {
        var plain = NewGame(out _, out _, playerCount: 2);
        Assert.Equal(108, plain.Cards.Count);
        Assert.DoesNotContain(new CardDefinitionId("red-swap"), plain.Definitions.Keys);

        var withRule = NewGame(out _, out _, playerCount: 2, config: new AgonyConfig { SwapRotateCards = true });
        Assert.Equal(116, withRule.Cards.Count);
        Assert.Contains(new CardDefinitionId("red-swap"), withRule.Definitions.Keys);
        Assert.Contains(new CardDefinitionId("blue-rotate"), withRule.Definitions.Keys);
    }

    [Fact]
    public void SetupRequiresAtLeastTwoPlayers()
    {
        var game = new AgonyGame();
        var state = new GameState(seed: 1);
        state.AddPlayer("Loner");
        Assert.Throws<ArgumentException>(() => game.Setup(state));
    }

    [Fact]
    public void JumpInIsHonestlyUnsupported()
    {
        Assert.Throws<NotSupportedException>(() => new AgonyGame(new AgonyConfig { JumpIn = true }));
    }

    [Fact]
    public void CardIdToDefinitionBindingDiffersPerSeed()
    {
        // Guards the id-leak fix: instance ids must not decode to definitions
        // via a fixed creation order. If a refactor reverts AgonyDeck to
        // creating cards in generation order, both games below would get the
        // identical id->definition mapping and this test fails.
        var first = NewGame(out _, out _, seed: 7);
        var second = NewGame(out _, out _, seed: 8);

        bool anyDiffers = first.Cards.Keys.Any(
            id => first.GetCard(id).Definition != second.GetCard(id).Definition);
        Assert.True(anyDiffers, "id->definition binding is identical across seeds — creation order is fixed again");
    }

    [Fact]
    public void SetupIsDeterministicPerSeed()
    {
        var first = NewGame(out var playersA, out _, seed: 7);
        var second = NewGame(out var playersB, out _, seed: 7);

        foreach (var (a, b) in playersA.Zip(playersB))
            Assert.Equal(
                Hand(first, a).Select(id => first.GetCard(id).Definition),
                Hand(second, b).Select(id => second.GetCard(id).Definition));
    }
}
