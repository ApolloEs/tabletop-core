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
        Assert.Null(state.Winner);
        Assert.Empty(state.LegalMoves);

        // A move after the end is still *handled* — with a rejection.
        Assert.True(await f.Room.SubmitMove("A", new MovePayload(99, new DrawCard())));
        Assert.Equal(99, f.Sender.Last<MoveRejectedPayload>("A").MoveId);
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
