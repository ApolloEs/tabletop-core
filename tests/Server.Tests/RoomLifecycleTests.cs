using Microsoft.Extensions.Time.Testing;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;
using TabletopCore.Server.Rooms;
using Xunit;

namespace TabletopCore.Server.Tests;

/// <summary>
/// The failure-behavior tests: time is a FakeTimeProvider, so "two minutes
/// pass" is one method call and zero waiting. The pattern for every test:
/// advance the clock (which fires the sweep timer synchronously), then await
/// any room command — the channel is FIFO, so when that command's task
/// completes, the sweep before it has fully run.
/// </summary>
public class RoomLifecycleTests
{
    private static readonly TimeSpan PastGrace = Room.GracePeriod + TimeSpan.FromSeconds(30);

    private sealed class Fixture
    {
        public required Room Room;
        public required RecordingSender Sender;
        public required FakeTimeProvider Time;
        public bool Defunct;
    }

    private static Fixture NewRoom()
    {
        var sender = new RecordingSender();
        var time = new FakeTimeProvider();
        var fixture = new Fixture { Room = null!, Sender = sender, Time = time };
        fixture.Room = new Room("TEST", sender, (_, _) => { }, () => 424242UL, time,
            onDefunct: _ => fixture.Defunct = true);
        return fixture;
    }

    private static async Task Drain(Room room)
        => await room.Disconnect("__nobody__"); // FIFO barrier: sweeps queued before this have run

    private static async Task<Fixture> StartedGame()
    {
        var fixture = NewRoom();
        await fixture.Room.Join("A", "Ava");
        await fixture.Room.Join("B", "Bo");
        await fixture.Room.Start("A");
        return fixture;
    }

    [Fact]
    public async Task AGraceExpiryIsAnnouncedAsAbandonmentExactlyOnce()
    {
        var f = await StartedGame();
        await f.Room.Disconnect("B");
        Assert.False(f.Sender.Last<PlayerStatusPayload>("A").Abandoned);

        f.Time.Advance(PastGrace);
        await Drain(f.Room);
        var status = f.Sender.Last<PlayerStatusPayload>("A");
        Assert.False(status.Connected);
        Assert.True(status.Abandoned);

        int announcements = f.Sender.All<PlayerStatusPayload>("A").Count(p => p.Abandoned);
        f.Time.Advance(PastGrace);
        await Drain(f.Room);
        Assert.Equal(announcements, f.Sender.All<PlayerStatusPayload>("A").Count(p => p.Abandoned));
    }

    [Fact]
    public async Task AnAbandonedSeatStillResumesWithItsHand()
    {
        var f = await StartedGame();
        string token = f.Sender.Last<WelcomePayload>("B").SessionToken;
        var handBefore = f.Sender.Last<StatePayload>("B").View.Zones
            .Single(z => z.Owner == f.Sender.Last<StatePayload>("B").View.Viewer).Cards!.Select(c => c.Id).ToList();

        await f.Room.Disconnect("B");
        f.Time.Advance(PastGrace);
        await Drain(f.Room);

        Assert.True(await f.Room.Resume("B2", token));
        var state = f.Sender.Last<StatePayload>("B2");
        Assert.Equal(handBefore, state.View.Zones.Single(z => z.Owner == state.View.Viewer).Cards!.Select(c => c.Id).ToList());
        Assert.True(f.Sender.Last<PlayerStatusPayload>("A") is { Connected: true, Abandoned: false });
    }

    [Fact]
    public async Task TheHostCanEndAGameStalledOnAnAbandonedTurn()
    {
        var f = await StartedGame();
        await f.Room.Disconnect("B");
        f.Time.Advance(PastGrace);
        await Drain(f.Room);

        Assert.True(await f.Room.EndGame("A"));
        var state = f.Sender.Last<StatePayload>("A");
        Assert.True(state.Finished);
        Assert.Empty(state.Standings);   // ended, but nobody actually went out
        Assert.Empty(state.LegalMoves);

        // A move after the end is still *handled* — with a rejection.
        Assert.True(await f.Room.SubmitMove("A", new MovePayload(99, new DrawCard())));
        Assert.Equal(99, f.Sender.Last<MoveRejectedPayload>("A").MoveId);
    }

    [Fact]
    public async Task AFinishedGameCanBeDealtAgainWithTheSameSeats()
    {
        var f = await StartedGame();
        await f.Room.EndGame("A");
        long finishedVersion = f.Sender.Last<StatePayload>("A").Version;

        Assert.True(await f.Room.Rematch("A"));

        var fresh = f.Sender.Last<StatePayload>("A");
        Assert.False(fresh.Finished);
        Assert.Empty(fresh.Standings);
        Assert.Equal(7, fresh.View.Zones.Single(z => z.Owner == fresh.View.Viewer).Cards!.Count);
        // The version must keep climbing across games: clients ignore states
        // older than the one they rendered, so a reset would make the new
        // deal invisible to everyone still looking at the old result.
        Assert.True(fresh.Version > finishedVersion, "rematch version did not advance");
        Assert.Equal(1, f.Sender.Last<WelcomePayload>("B").Seat);
    }

    [Fact]
    public async Task AFinishedGameCanReopenTheLobbyWhereRulesChange()
    {
        var f = await StartedGame();
        await f.Room.EndGame("A");

        Assert.True(await f.Room.BackToLobby("A"));
        Assert.Equal(2, f.Sender.Last<LobbyPayload>("B").Players.Count);

        // House rules are lobby-only, so reopening it is what makes them
        // reachable between hands.
        Assert.True(await f.Room.SetConfig("A", new AgonyConfig { PlayForPlacings = true }));
        Assert.True(f.Sender.Last<LobbyPayload>("B").Config.PlayForPlacings);

        Assert.True(await f.Room.Start("A"));
        Assert.False(f.Sender.Last<StatePayload>("A").Finished);
    }

    [Fact]
    public async Task RematchAndLobbyAreHostOnlyAndOnlyOnceFinished()
    {
        var f = await StartedGame();

        Assert.False(await f.Room.Rematch("A"));       // still running
        Assert.Equal("notFinished", f.Sender.Last<ErrorPayload>("A").Code);
        Assert.False(await f.Room.BackToLobby("A"));
        Assert.Equal("notFinished", f.Sender.Last<ErrorPayload>("A").Code);

        await f.Room.EndGame("A");
        Assert.False(await f.Room.Rematch("B"));       // not the host
        Assert.Equal("hostOnly", f.Sender.Last<ErrorPayload>("B").Code);
        Assert.False(await f.Room.BackToLobby("B"));
        Assert.Equal("hostOnly", f.Sender.Last<ErrorPayload>("B").Code);
    }

    [Fact]
    public async Task OnlyTheHostEndsGames()
    {
        var f = await StartedGame();
        Assert.False(await f.Room.EndGame("B"));
        Assert.Equal("hostOnly", f.Sender.Last<ErrorPayload>("B").Code);
    }

    [Fact]
    public async Task AnExpiredLobbySeatIsFreedAndLaterSeatsSlideDown()
    {
        var f = NewRoom();
        await f.Room.Join("A", "Ava");
        await f.Room.Join("B", "Bo");
        await f.Room.Join("C", "Cy");
        await f.Room.Disconnect("B");

        f.Time.Advance(PastGrace);
        await Drain(f.Room);

        var lobby = f.Sender.Last<LobbyPayload>("A");
        Assert.Equal(["Ava", "Cy"], lobby.Players.Select(p => p.Name));
        Assert.Equal([0, 1], lobby.Players.Select(p => p.Seat));
    }

    [Fact]
    public async Task ARoomEveryoneLeftGarbageCollectsItself()
    {
        var f = await StartedGame();
        await f.Room.Disconnect("A");
        await f.Room.Disconnect("B");

        f.Time.Advance(PastGrace);
        // No Drain here: the channel may already be completed by the sweep.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.True(f.Defunct);
        Assert.False(await f.Room.Join("Z", "Latecomer"));
    }
}
