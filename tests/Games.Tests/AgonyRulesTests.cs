using TabletopCore.Engine;
using TabletopCore.Games.Agony;
using Xunit;
using static TabletopCore.Games.Tests.AgonyTestHelper;

namespace TabletopCore.Games.Tests;

public class AgonyRulesTests
{
    [Fact]
    public void ColorMatchIsPlayableAndAdvancesTheTurn()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var card = Give(state, players[0], "red-8");

        var result = game.TryMove(state, players[0].Id, new PlayCard(card.Id));

        Assert.True(result.Success);
        Assert.Equal(card.Id, state.GetZone(AgonyGame.DiscardZone).Cards[^1]);
        Assert.True(state.GetCard(card.Id).FaceUp);
        Assert.Equal(players[1].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void SymbolMatchAcrossColorsChangesTheActiveColor()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var card = Give(state, players[0], "blue-3");

        Must(game.TryMove(state, players[0].Id, new PlayCard(card.Id)));

        Assert.Equal((int)AgonyColor.Blue,
            state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.ActiveColorCounter));
    }

    [Fact]
    public void NonMatchingCardIsRejectedWithoutChangingState()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var card = Give(state, players[0], "blue-8");
        int handBefore = Hand(state, players[0]).Count;
        int logBefore = state.EventLog.Count;

        var result = game.TryMove(state, players[0].Id, new PlayCard(card.Id));

        Assert.False(result.Success);
        Assert.Equal(handBefore, Hand(state, players[0]).Count);
        Assert.Equal(logBefore, state.EventLog.Count);
        Assert.Equal(players[0].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void PlayingOutOfTurnIsRejected()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var card = Give(state, players[1], "red-8");

        var result = game.TryMove(state, players[1].Id, new PlayCard(card.Id));

        Assert.False(result.Success);
    }

    [Fact]
    public void WildsRequireADeclaredColorAndSetIt()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var wild = Give(state, players[0], "wild");

        Assert.False(game.TryMove(state, players[0].Id, new PlayCard(wild.Id)).Success);

        Must(game.TryMove(state, players[0].Id, new PlayCard(wild.Id, AgonyColor.Green)));
        Assert.Equal((int)AgonyColor.Green,
            state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.ActiveColorCounter));
    }

    [Fact]
    public void ColoredCardsMayNotDeclareAColor()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var card = Give(state, players[0], "red-8");

        var result = game.TryMove(state, players[0].Id, new PlayCard(card.Id, AgonyColor.Blue));

        Assert.False(result.Success);
    }

    [Fact]
    public void SkipPassesOverTheNextPlayer()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var skip = Give(state, players[0], "red-skip");

        Must(game.TryMove(state, players[0].Id, new PlayCard(skip.Id)));

        Assert.Equal(players[2].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void ReverseFlipsDirectionWithThreePlayers()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var reverse = Give(state, players[0], "red-reverse");

        Must(game.TryMove(state, players[0].Id, new PlayCard(reverse.Id)));

        Assert.Equal(TurnDirection.Reversed, state.Turn.Direction);
        Assert.Equal(players[2].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void ReverseActsAsSkipWithTwoPlayers()
    {
        var state = NewGame(out var players, out var game, playerCount: 2);
        SetTop(state, players[0], "red-3");
        var reverse = Give(state, players[0], "red-reverse");

        Must(game.TryMove(state, players[0].Id, new PlayCard(reverse.Id)));

        Assert.Equal(players[0].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void DrawTwoForcesTheDrawWhenStackingIsOff()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var drawTwo = Give(state, players[0], "red-draw2");
        var victims = Give(state, players[1], "blue-draw2");
        int victimHand = Hand(state, players[1]).Count;

        Must(game.TryMove(state, players[0].Id, new PlayCard(drawTwo.Id)));

        var legal = game.GetLegalMoves(state, players[1].Id);
        Assert.Equal([new DrawCard()], legal);
        Assert.False(game.TryMove(state, players[1].Id, new PlayCard(victims.Id)).Success);

        Must(game.TryMove(state, players[1].Id, new DrawCard()));
        Assert.Equal(victimHand + 2, Hand(state, players[1]).Count);
        Assert.Equal(0, state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.PendingDrawCounter));
        // Taking the debt does not end the turn — the victim plays on.
        Assert.Equal(players[1].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void TakingADebtLeavesANormalTurnToPlayOut()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var drawTwo = Give(state, players[0], "red-draw2");
        Must(game.TryMove(state, players[0].Id, new PlayCard(drawTwo.Id)));

        Must(game.TryMove(state, players[1].Id, new DrawCard()));

        // A full turn remains: the one normal draw is still untouched, and
        // passing is only legal after taking it.
        var legal = game.GetLegalMoves(state, players[1].Id);
        Assert.Contains(new DrawCard(), legal);
        Assert.DoesNotContain(new PassTurn(), legal);
        Assert.False(game.TryMove(state, players[1].Id, new PassTurn()).Success);

        Must(game.TryMove(state, players[1].Id, new DrawCard()));
        Assert.Contains(new PassTurn(), game.GetLegalMoves(state, players[1].Id));
        Must(game.TryMove(state, players[1].Id, new PassTurn()));
        Assert.Equal(players[2].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void DrawTwoStacksWhenTheHouseRuleIsOn()
    {
        var state = NewGame(out var players, out var game, config: new AgonyConfig { StackDrawCards = true });
        SetTop(state, players[0], "red-3");
        var first = Give(state, players[0], "red-draw2");
        var second = Give(state, players[1], "blue-draw2");
        int thirdHand = Hand(state, players[2]).Count;

        Must(game.TryMove(state, players[0].Id, new PlayCard(first.Id)));
        Must(game.TryMove(state, players[1].Id, new PlayCard(second.Id)));

        Assert.Equal(4, state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.PendingDrawCounter));

        Must(game.TryMove(state, players[2].Id, new DrawCard()));
        Assert.Equal(thirdHand + 4, Hand(state, players[2]).Count);
    }

    [Fact]
    public void AWildFourAnswersATwoButNotTheOtherWayAround()
    {
        var state = NewGame(out var players, out var game, config: new AgonyConfig { StackDrawCards = true });
        SetTop(state, players[0], "red-3");
        var drawTwo = Give(state, players[0], "red-draw2");
        var wildFour = Give(state, players[1], "wild4");
        var answeringTwo = Give(state, players[2], "green-draw2");

        // +2 played; the next player answers with a Wild +4, debt 2 -> 6.
        Must(game.TryMove(state, players[0].Id, new PlayCard(drawTwo.Id)));
        Assert.Contains(new PlayCard(wildFour.Id, AgonyColor.Green), game.GetLegalMoves(state, players[1].Id));
        Must(game.TryMove(state, players[1].Id, new PlayCard(wildFour.Id, AgonyColor.Green)));
        Assert.Equal(6, state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.PendingDrawCounter));

        // A mere +2 cannot answer a +4 — only taking it is on offer.
        Assert.Equal([new DrawCard()], game.GetLegalMoves(state, players[2].Id));
        Assert.False(game.TryMove(state, players[2].Id, new PlayCard(answeringTwo.Id)).Success);
    }

    [Fact]
    public void AFruitlessDrawNeverPassesTheTurnByItself()
    {
        // The anti-tell rule: an automatic pass that fires when nothing is
        // playable would be a 1-bit oracle on the hand (firing = "he has
        // nothing", not firing = "he is saving something"). The turn moves
        // only on an explicit Pass, no matter how hopeless the hand.
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");

        Must(game.TryMove(state, players[0].Id, new DrawCard()));

        Assert.Equal(players[0].Id, state.Turn.ActivePlayer);
        Assert.Contains(new PassTurn(), game.GetLegalMoves(state, players[0].Id));

        Must(game.TryMove(state, players[0].Id, new PassTurn()));
        Assert.Equal(players[1].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void DrawingKeepsEveryPlayOptionButForbidsASecondDraw()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");
        var other = Give(state, players[0], "red-8");
        int handBefore = Hand(state, players[0]).Count;

        Must(game.TryMove(state, players[0].Id, new DrawCard()));
        Assert.Equal(handBefore + 1, Hand(state, players[0]).Count);

        // The options from before the draw are all still open…
        Assert.Contains(new PlayCard(other.Id), game.GetLegalMoves(state, players[0].Id));
        Assert.Contains(new PassTurn(), game.GetLegalMoves(state, players[0].Id));
        // …only drawing again is off the table.
        Assert.False(game.TryMove(state, players[0].Id, new DrawCard()).Success);

        Must(game.TryMove(state, players[0].Id, new PlayCard(other.Id)));
        Assert.Equal(players[1].Id, state.Turn.ActivePlayer);
        Assert.Equal(0, state.GetZone(AgonyGame.TableZone).GetCounter(AgonyGame.HasDrawnCounter));
    }

    [Fact]
    public void SwapTradesHandsWithTheNextPlayer()
    {
        var state = NewGame(out var players, out var game, config: new AgonyConfig { SwapRotateCards = true });
        SetTop(state, players[0], "red-3");
        var swap = Give(state, players[0], "red-swap");
        var myRest = Hand(state, players[0]).Where(id => id != swap.Id).ToHashSet();
        var theirs = Hand(state, players[1]).ToHashSet();

        Must(game.TryMove(state, players[0].Id, new PlayCard(swap.Id)));

        Assert.True(theirs.SetEquals(Hand(state, players[0])));
        Assert.True(myRest.SetEquals(Hand(state, players[1])));
        Assert.All(Hand(state, players[0]), id => Assert.Equal(players[0].Id, state.GetCard(id).Owner));
        Assert.Equal(players[1].Id, state.Turn.ActivePlayer);
    }

    [Fact]
    public void RotatePassesEveryHandOneSeatInPlayDirection()
    {
        var state = NewGame(out var players, out var game, config: new AgonyConfig { SwapRotateCards = true });
        SetTop(state, players[0], "red-3");
        var rotate = Give(state, players[0], "red-rotate");
        var hand0 = Hand(state, players[0]).Where(id => id != rotate.Id).ToHashSet();
        var hand1 = Hand(state, players[1]).ToHashSet();
        var hand2 = Hand(state, players[2]).ToHashSet();

        Must(game.TryMove(state, players[0].Id, new PlayCard(rotate.Id)));

        Assert.True(hand0.SetEquals(Hand(state, players[1])));
        Assert.True(hand1.SetEquals(Hand(state, players[2])));
        Assert.True(hand2.SetEquals(Hand(state, players[0])));
    }

    [Fact]
    public void EmptyingYourHandWinsAndFreezesTheGame()
    {
        var state = NewGame(out var players, out var game);
        foreach (var id in Hand(state, players[0]).ToList())
            Must(state.Apply(new TabletopCore.Engine.Actions.MoveCardAction(players[0].Id, id, AgonyGame.DeckZone)));
        SetTop(state, players[0], "red-3");
        var last = Give(state, players[0], "red-8");

        Must(game.TryMove(state, players[0].Id, new PlayCard(last.Id)));

        Assert.Equal([players[0].Id], game.GetStandings(state));
        Assert.True(game.IsFinished(state));
        Assert.Empty(game.GetLegalMoves(state, players[1].Id));
        Assert.False(game.TryMove(state, players[1].Id, new DrawCard()).Success);
    }

    [Fact]
    public void PlayingForPlacingsKeepsGoingUntilOnePlayerIsLeft()
    {
        var state = NewGame(out var players, out var game,
            config: new AgonyConfig { PlayForPlacings = true });
        GoOut(state, game, players[1], "red-3", "red-8");

        // First out takes 1st place, but with three players the game runs on.
        Assert.Equal([players[1].Id], game.GetStandings(state));
        Assert.False(game.IsFinished(state));

        // The seat that went out is stepped over and can no longer move.
        Assert.Empty(game.GetLegalMoves(state, players[1].Id));
        Assert.False(game.TryMove(state, players[1].Id, new DrawCard()).Success);
        Assert.NotEqual(players[1].Id, state.Turn.ActivePlayer);

        // Second one out ends it: one player is left holding cards.
        GoOut(state, game, players[2], "blue-4", "blue-9");
        Assert.Equal([players[1].Id, players[2].Id], game.GetStandings(state));
        Assert.True(game.IsFinished(state));
    }

    /// <summary>Empties a player's hand down to one playable card, hands them
    /// the turn, and plays it — the "goes out" position, built through the
    /// engine's own actions like every other fixture here.</summary>
    private static void GoOut(GameState state, AgonyGame game, Player player, string topId, string lastId)
    {
        foreach (var id in Hand(state, player).ToList())
            Must(state.Apply(new TabletopCore.Engine.Actions.MoveCardAction(player.Id, id, AgonyGame.DeckZone)));
        SetTop(state, player, topId);
        var last = Give(state, player, lastId);
        while (state.Turn.ActivePlayer != player.Id)
            Must(state.Apply(new TabletopCore.Engine.Actions.EndTurnAction(state.Turn.ActivePlayer)));
        Must(game.TryMove(state, player.Id, new PlayCard(last.Id)));
    }

    [Fact]
    public void LegalMovesAreExactlyThePlayableCardsPlusDraw()
    {
        var state = NewGame(out var players, out var game);
        SetTop(state, players[0], "red-3");

        var legal = game.GetLegalMoves(state, players[0].Id);

        Assert.Contains(new DrawCard(), legal);
        var plays = legal.OfType<PlayCard>().ToList();
        Assert.All(plays, p =>
        {
            string color = state.GetValue(p.Card, "color").AsString;
            string symbol = state.GetValue(p.Card, "symbol").AsString;
            bool wild = color == "wild";
            Assert.True(wild || color == "red" || symbol == "3");
            Assert.Equal(wild, p.DeclaredColor is not null);
        });
        // Every wild in hand appears once per declarable color.
        foreach (var wildId in Hand(state, players[0])
                     .Where(id => state.GetValue(id, "color").AsString == "wild"))
            Assert.Equal(4, plays.Count(p => p.Card == wildId));

        Assert.Empty(game.GetLegalMoves(state, players[1].Id));
    }
}
