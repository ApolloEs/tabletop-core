using System.Text.Json;
using TabletopCore.Engine;
using TabletopCore.Engine.Events;
using TabletopCore.Engine.Projection;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;
using Xunit;

namespace TabletopCore.Protocol.Tests;

public class RoundTripTests
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, WireJson.Options);

    public static TheoryData<GameMove> EveryRegisteredMove() =>
    [
        new PlayCard(new CardInstanceId(12), AgonyColor.Blue),
        new PlayCard(new CardInstanceId(3)),
        new DrawCard(),
        new PassTurn(),
    ];

    public static TheoryData<GameEvent> EveryRegisteredEvent() =>
    [
        new CardMoved(new CardInstanceId(12), new ZoneId("hand:0"), new ZoneId("discard")),
        new CardFlipped(new CardInstanceId(12), FaceUp: true),
        new ZoneShuffled(new ZoneId("deck"), 108),
        new CardDrawn(new PlayerId(1), new CardInstanceId(5), new ZoneId("deck"), new ZoneId("hand:1")),
        new CardsDrawnHidden(new PlayerId(1), new ZoneId("deck"), new ZoneId("hand:1"), Count: 2),
        new TurnEnded(new PlayerId(0), 4),
        new TurnStarted(new PlayerId(1), 5),
        new TurnSkipped(new PlayerId(1)),
        new TurnDirectionChanged(TurnDirection.Reversed),
        new CounterChanged(new ZoneId("table"), "pendingDraw", 4),
    ];

    [Theory]
    [MemberData(nameof(EveryRegisteredMove))]
    public void MovesSurviveTheWire(GameMove move)
        => Assert.Equal(move, JsonSerializer.Deserialize<GameMove>(Json(move), WireJson.Options));

    [Theory]
    [MemberData(nameof(EveryRegisteredEvent))]
    public void EventsSurviveTheWire(GameEvent gameEvent)
        => Assert.Equal(gameEvent, JsonSerializer.Deserialize<GameEvent>(Json(gameEvent), WireJson.Options));

    [Fact]
    public void MidGamePlayerViewsRoundTripByteStable()
    {
        // Records holding lists compare by list reference, so equality can't
        // assert the round trip; serialize -> deserialize -> serialize and
        // compare bytes instead, for every player's view of a real game.
        var state = new GameState(seed: 42);
        var ava = state.AddPlayer("Ava");
        var bo = state.AddPlayer("Bo");
        new AgonyGame(new AgonyConfig { SwapRotateCards = true }).Setup(state);

        foreach (var player in new[] { ava, bo })
        {
            string first = Json(StateProjector.ProjectFor(state, player.Id));
            var reborn = JsonSerializer.Deserialize<PlayerView>(first, WireJson.Options);
            Assert.Equal(first, Json(reborn));
        }
    }

    [Fact]
    public void CatalogRoundTripsByteStable()
    {
        var state = new GameState(seed: 42);
        state.AddPlayer("Ava");
        state.AddPlayer("Bo");
        new AgonyGame().Setup(state);

        string first = Json(CardCatalog.From(state));
        var reborn = JsonSerializer.Deserialize<CardCatalog>(first, WireJson.Options);
        Assert.Equal(first, Json(reborn));
    }

    [Fact]
    public void UnknownMoveDiscriminatorIsRejected()
        => Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<GameMove>("""{"type":"agony.becomeTheDealer"}""", WireJson.Options));

    [Fact]
    public void DiscriminatorIsAcceptedInAnyPosition()
    {
        var move = JsonSerializer.Deserialize<GameMove>(
            """{"card":12,"declaredColor":"blue","type":"agony.playCard"}""", WireJson.Options);
        Assert.Equal(new PlayCard(new CardInstanceId(12), AgonyColor.Blue), move);
    }
}
