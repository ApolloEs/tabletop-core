using TabletopCore.Engine;
using TabletopCore.Engine.Projection;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;
using TabletopCore.Server.Rooms;
using Xunit;

namespace TabletopCore.Server.Tests;

/// <summary>
/// Below-the-hub tests: a real Room actor driven directly, with a recording
/// fake in place of SignalR. Deterministic (the seed is pinned), so the
/// scripted-game test plays the exact same match every run.
/// </summary>
public class RoomTests
{
    private const ulong Seed = 424242;

    private static (Room Room, RecordingSender Sender, Dictionary<string, Room> Tokens) NewRoom()
    {
        var sender = new RecordingSender();
        var tokens = new Dictionary<string, Room>();
        var room = new Room("TEST", sender, (token, r) => { lock (tokens) tokens[token] = r; }, () => Seed);
        return (room, sender, tokens);
    }

    private static async Task<(Room Room, RecordingSender Sender)> StartedGame()
    {
        var (room, sender, _) = NewRoom();
        Assert.True(await room.Join("A", "Ava"));
        Assert.True(await room.Join("B", "Bo"));
        Assert.True(await room.Start("A"));
        return (room, sender);
    }

    [Fact]
    public async Task JoiningDeliversWelcomeAndBroadcastsLobby()
    {
        var (room, sender, tokens) = NewRoom();

        Assert.True(await room.Join("A", "Ava"));
        var welcome = sender.Last<WelcomePayload>("A");
        Assert.Equal(0, welcome.Seat);
        Assert.Equal("TEST", welcome.RoomCode);
        Assert.Contains(welcome.SessionToken, tokens.Keys);

        Assert.True(await room.Join("B", "Bo"));
        var lobby = sender.Last<LobbyPayload>("A");
        Assert.Equal(2, lobby.Players.Count);
        Assert.True(lobby.CanStart);
        Assert.True(lobby.Players[0].IsHost);
        Assert.False(lobby.Players[1].IsHost);
    }

    [Fact]
    public async Task OnlyTheHostMayConfigureOrStart()
    {
        var (room, sender, _) = NewRoom();
        await room.Join("A", "Ava");
        await room.Join("B", "Bo");

        Assert.False(await room.SetConfig("B", new AgonyConfig { StackDrawCards = true }));
        Assert.Equal("hostOnly", sender.Last<ErrorPayload>("B").Code);

        Assert.False(await room.Start("B"));
        Assert.Equal("hostOnly", sender.Last<ErrorPayload>("B").Code);

        Assert.True(await room.SetConfig("A", new AgonyConfig { StackDrawCards = true }));
        Assert.True(sender.Last<LobbyPayload>("B").Config.StackDrawCards);
    }

    [Fact]
    public async Task JumpInConfigIsRefusedNotCrashedOn()
    {
        var (room, sender, _) = NewRoom();
        await room.Join("A", "Ava");

        Assert.False(await room.SetConfig("A", new AgonyConfig { JumpIn = true }));
        Assert.Equal("unsupported", sender.Last<ErrorPayload>("A").Code);
    }

    [Fact]
    public async Task StartSendsEachPlayerTheirOwnFilteredState()
    {
        var (_, sender) = await StartedGame();

        foreach (var connection in new[] { "A", "B" })
        {
            Assert.NotNull(sender.LastOrNull<CardCatalog>(connection));
            var state = sender.Last<StatePayload>(connection);
            var me = state.View.Viewer;

            var myHand = state.View.Zones.Single(z => z.Owner == me);
            Assert.NotNull(myHand.Cards);
            Assert.Equal(7, myHand.Cards!.Count);
            Assert.All(myHand.Cards, card => Assert.NotNull(card.Definition));

            var otherHand = state.View.Zones.Single(z => z.Owner is not null && z.Owner != me);
            Assert.Null(otherHand.Cards);
            Assert.Equal(7, otherHand.CardCount);
        }
    }

    [Fact]
    public async Task MovesFromThePlayerWhoseTurnItIsNotAreRejected()
    {
        var (room, sender) = await StartedGame();

        var stateA = sender.Last<StatePayload>("A");
        string idle = stateA.View.Turn.ActivePlayer == stateA.View.Viewer ? "B" : "A";

        await room.SubmitMove(idle, new MovePayload(1, new DrawCard()));
        var rejected = sender.Last<MoveRejectedPayload>(idle);
        Assert.Equal(1, rejected.MoveId);
    }

    [Fact]
    public async Task AScriptedGameRunsToAWinnerWithoutEverLeakingAHand()
    {
        var (room, sender) = await StartedGame();
        var moveIds = new Dictionary<string, long> { ["A"] = 0, ["B"] = 0 };

        for (int step = 0; step < 1000; step++)
        {
            var latest = sender.Last<StatePayload>("A");
            if (latest.Finished)
                break;

            string active = ActiveConnection(sender);
            var mine = sender.Last<StatePayload>(active);
            Assert.NotEmpty(mine.LegalMoves);

            Assert.True(
                await room.SubmitMove(active, new MovePayload(++moveIds[active], mine.LegalMoves[0])),
                $"step {step}: move was not accepted");
        }

        Assert.Single(sender.Last<StatePayload>("A").Standings);
        Assert.Equal(
            sender.Last<StatePayload>("A").Standings,
            sender.Last<StatePayload>("B").Standings);

        // Sweep every state ever sent: a hand zone with cards visible must
        // belong to the connection's own viewer.
        foreach (var connection in new[] { "A", "B" })
            foreach (var state in sender.All<StatePayload>(connection))
                foreach (var zone in state.View.Zones.Where(z => z.Owner is not null && z.Cards is not null))
                    Assert.Equal(state.View.Viewer, zone.Owner);
    }

    [Fact]
    public async Task ADuplicateMoveIdIsAcknowledgedButNeverReapplied()
    {
        var (room, sender) = await StartedGame();
        string active = ActiveConnection(sender);
        var move = sender.Last<StatePayload>(active).LegalMoves[0];

        Assert.True(await room.SubmitMove(active, new MovePayload(1, move)));
        var firstAck = sender.Last<MoveAcceptedPayload>(active);
        var versionAfterFirst = sender.Last<StatePayload>(active).Version;

        // The resend: same id, same move — as a client would after a drop.
        Assert.True(await room.SubmitMove(active, new MovePayload(1, move)));
        var secondAck = sender.Last<MoveAcceptedPayload>(active);

        Assert.Equal(firstAck, secondAck);
        Assert.Equal(versionAfterFirst, sender.Last<StatePayload>(active).Version);
    }

    [Fact]
    public async Task ResumeRestoresTheSeatAndTheExactHand()
    {
        var (room, sender) = await StartedGame();

        var before = sender.Last<StatePayload>("B");
        string token = sender.Last<WelcomePayload>("B").SessionToken;
        var handBefore = OwnHandCardIds(before);

        Assert.True(await room.Disconnect("B"));
        var status = sender.Last<PlayerStatusPayload>("A");
        Assert.False(status.Connected);

        Assert.True(await room.Resume("B2", token));
        var welcome = sender.Last<WelcomePayload>("B2");
        Assert.Equal(1, welcome.Seat);
        Assert.NotNull(sender.LastOrNull<CardCatalog>("B2"));
        Assert.Equal(handBefore, OwnHandCardIds(sender.Last<StatePayload>("B2")));
        Assert.True(sender.Last<PlayerStatusPayload>("A").Connected);
    }

    [Fact]
    public async Task WelcomeCarriesTheMoveIdSequenceSoARestartedClientContinuesIt()
    {
        var (room, sender) = await StartedGame();
        string active = ActiveConnection(sender);
        var move = sender.Last<StatePayload>(active).LegalMoves[0];
        Assert.True(await room.SubmitMove(active, new MovePayload(1, move)));

        string token = sender.Last<WelcomePayload>(active).SessionToken;
        await room.Disconnect(active);
        Assert.True(await room.Resume("R", token));

        // A restarted client reads this and starts at lastMoveId + 1 —
        // starting back at 1 would read as a resend and dedupe-deadlock.
        Assert.Equal(1, sender.Last<WelcomePayload>("R").LastMoveId);
    }

    [Fact]
    public async Task ResumingWithAForeignTokenFails()
    {
        var (room, sender) = await StartedGame();
        Assert.False(await room.Resume("X", "not-a-real-token"));
        Assert.Equal("unknownSession", sender.Last<ErrorPayload>("X").Code);
    }

    private static string ActiveConnection(RecordingSender sender)
    {
        var state = sender.Last<StatePayload>("A");
        bool aIsActive = state.View.Turn.ActivePlayer == state.View.Viewer;
        // "B2" is only ever used post-resume in tests that don't call this.
        return aIsActive ? "A" : "B";
    }

    private static IReadOnlyList<CardInstanceId> OwnHandCardIds(StatePayload state)
        => state.View.Zones.Single(z => z.Owner == state.View.Viewer).Cards!
            .Select(c => c.Id)
            .ToList();
}
