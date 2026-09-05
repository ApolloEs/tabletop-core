using TabletopCore.Games.Agony;
using TabletopCore.Protocol;
using TabletopCore.Server.Rooms;
using Xunit;

namespace TabletopCore.Server.Tests;

public class RegistryTests
{
    [Fact]
    public async Task UnknownGameRoomAndSessionAllGetTypedErrors()
    {
        var sender = new RecordingSender();
        var registry = new RoomRegistry(sender);

        await registry.Create("A", "Ava", "poker");
        Assert.Equal("unknownGame", sender.Last<ErrorPayload>("A").Code);

        await registry.Join("A", "QQQQ", "Ava");
        Assert.Equal("roomNotFound", sender.Last<ErrorPayload>("A").Code);

        await registry.Resume("A", "no-such-token");
        Assert.Equal("unknownSession", sender.Last<ErrorPayload>("A").Code);

        await registry.Start("A");
        Assert.Equal("notInRoom", sender.Last<ErrorPayload>("A").Code);
    }

    [Fact]
    public async Task CreateJoinsTheCreatorAndIssuesAReadableCode()
    {
        var sender = new RecordingSender();
        var registry = new RoomRegistry(sender);

        await registry.Create("A", "Ava", "agony");
        var welcome = sender.Last<WelcomePayload>("A");
        Assert.Matches("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{4}$", welcome.RoomCode);

        await registry.Join("B", welcome.RoomCode.ToLowerInvariant(), "Bo");
        Assert.Equal(welcome.RoomCode, sender.Last<WelcomePayload>("B").RoomCode);

        await registry.SetConfig("A", new AgonyConfig { StackDrawTwo = true });
        Assert.True(sender.Last<LobbyPayload>("B").Config.StackDrawTwo);
    }
}
