using System.Text.Json;
using TabletopCore.Engine;
using TabletopCore.Engine.Events;
using TabletopCore.Engine.Projection;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;
using Xunit;

namespace TabletopCore.Protocol.Tests;

/// <summary>
/// These strings ARE the wire contract. A failure here means the JSON shape
/// changed — which breaks every client that isn't recompiled in lockstep.
/// If a change is intentional, update the golden string consciously and
/// treat it as a protocol version event, not a test fix.
/// </summary>
public class GoldenJsonTests
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, WireJson.Options);

    [Fact]
    public void PlayCardWithDeclaredColor()
        => Assert.Equal(
            """{"type":"agony.playCard","card":12,"declaredColor":"red"}""",
            Json<GameMove>(new PlayCard(new CardInstanceId(12), AgonyColor.Red)));

    [Fact]
    public void PlayCardWithoutDeclaredColorOmitsIt()
        => Assert.Equal(
            """{"type":"agony.playCard","card":3}""",
            Json<GameMove>(new PlayCard(new CardInstanceId(3))));

    [Fact]
    public void DrawCardAndPassTurnAreBareDiscriminators()
    {
        Assert.Equal("""{"type":"agony.drawCard"}""", Json<GameMove>(new DrawCard()));
        Assert.Equal("""{"type":"agony.passTurn"}""", Json<GameMove>(new PassTurn()));
    }

    [Fact]
    public void CardMovedShape()
        => Assert.Equal(
            """{"type":"cardMoved","card":12,"from":"hand:0","to":"discard"}""",
            Json<GameEvent>(new CardMoved(new CardInstanceId(12), new ZoneId("hand:0"), new ZoneId("discard"))));

    [Fact]
    public void CardDrawnVersusItsRedactedTwin()
    {
        Assert.Equal(
            """{"type":"cardDrawn","player":1,"card":5,"from":"deck","to":"hand:1"}""",
            Json<GameEvent>(new CardDrawn(new PlayerId(1), new CardInstanceId(5), new ZoneId("deck"), new ZoneId("hand:1"))));
        Assert.Equal(
            """{"type":"cardsDrawnHidden","player":1,"from":"deck","to":"hand:1","count":1}""",
            Json<GameEvent>(new CardsDrawnHidden(new PlayerId(1), new ZoneId("deck"), new ZoneId("hand:1"), Count: 1)));
    }

    [Fact]
    public void TurnAndCounterEvents()
    {
        Assert.Equal(
            """{"type":"turnStarted","player":0,"turnNumber":3}""",
            Json<GameEvent>(new TurnStarted(new PlayerId(0), 3)));
        Assert.Equal(
            """{"type":"turnDirectionChanged","direction":"reversed"}""",
            Json<GameEvent>(new TurnDirectionChanged(TurnDirection.Reversed)));
        Assert.Equal(
            """{"type":"counterChanged","zone":"table","name":"activeColor","value":2}""",
            Json<GameEvent>(new CounterChanged(new ZoneId("table"), "activeColor", 2)));
    }

    [Fact]
    public void PlayerViewShape()
    {
        var view = new PlayerView(
            new PlayerId(0),
            [new PlayerSummary(new PlayerId(0), "Ava", 0), new PlayerSummary(new PlayerId(1), "Bo", 1)],
            [
                new ZoneView(new ZoneId("deck"), ZoneKind.Pile, ZoneVisibility.Hidden,
                    Owner: null, CardCount: 2, Cards: null, Counters: new Dictionary<string, int>()),
                new ZoneView(new ZoneId("hand:0"), ZoneKind.Hand, ZoneVisibility.OwnerOnly,
                    Owner: new PlayerId(0), CardCount: 1,
                    Cards: [new CardView(new CardInstanceId(7), new CardDefinitionId("red-7"), FaceUp: false)],
                    Counters: new Dictionary<string, int> { ["activeColor"] = 0 }),
            ],
            new TurnInfo(new PlayerId(0), TurnDirection.Forward, 1));

        Assert.Equal(
            """{"viewer":0,"players":[{"id":0,"displayName":"Ava","seat":0},{"id":1,"displayName":"Bo","seat":1}],"zones":[{"id":"deck","kind":"pile","visibility":"hidden","cardCount":2,"counters":{}},{"id":"hand:0","kind":"hand","visibility":"ownerOnly","owner":0,"cardCount":1,"cards":[{"id":7,"definition":"red-7","faceUp":false}],"counters":{"activeColor":0}}],"turn":{"activePlayer":0,"direction":"forward","turnNumber":1}}""",
            Json(view));
    }

    [Fact]
    public void HubPayloadShapes()
    {
        Assert.Equal(
            """{"sessionToken":"tok123","playerId":1,"seat":1,"roomCode":"KWXZ","protocolVersion":1}""",
            Json(new WelcomePayload("tok123", new PlayerId(1), 1, "KWXZ", 1)));
        Assert.Equal(
            """{"moveId":4,"move":{"type":"agony.drawCard"}}""",
            Json(new MovePayload(4, new DrawCard())));
        Assert.Equal(
            """{"moveId":4,"version":17}""",
            Json(new MoveAcceptedPayload(4, 17)));
        Assert.Equal(
            """{"moveId":5,"error":"It is not your turn."}""",
            Json(new MoveRejectedPayload(5, "It is not your turn.")));
        Assert.Equal(
            """{"seat":1,"connected":false}""",
            Json(new PlayerStatusPayload(1, false)));
        // The Web defaults escape HTML-sensitive characters (' → ').
        // Pinned deliberately: JS JSON.parse decodes these transparently,
        // and the strict encoder is the safe default for text that may be
        // injected into pages. Player-typed strings cross the wire escaped.
        string errorJson = Json(new ErrorPayload("roomNotFound", "No room 'QQQQ'."));
        Assert.Equal("{\"code\":\"roomNotFound\",\"message\":\"No room \\u0027QQQQ\\u0027.\"}", errorJson);
        Assert.Equal(
            """{"players":[{"seat":0,"name":"Ava","connected":true,"isHost":true}],"config":{"stackDrawTwo":true,"swapRotateCards":false,"jumpIn":false},"canStart":false}""",
            Json(new LobbyPayload(
                [new LobbyPlayer(0, "Ava", Connected: true, IsHost: true)],
                new AgonyConfig { StackDrawTwo = true },
                CanStart: false)));
    }

    [Fact]
    public void CatalogEntryShape()
    {
        var entry = new CardCatalogEntry(
            new CardDefinitionId("red-7"), "Red 7",
            new Dictionary<string, PropertyValue>
            {
                ["color"] = "red",
                ["symbol"] = "7",
                ["number"] = 7,
            },
            FaceImage: null);

        Assert.Equal(
            """{"id":"red-7","name":"Red 7","properties":{"color":"red","symbol":"7","number":7}}""",
            Json(entry));
    }
}
