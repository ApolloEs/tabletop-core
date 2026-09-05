using Microsoft.AspNetCore.SignalR;
using TabletopCore.Games.Agony;
using TabletopCore.Protocol;
using TabletopCore.Server.Rooms;

namespace TabletopCore.Server;

/// <summary>
/// The SignalR edge — deliberately the thinnest file in the server. Every
/// method forwards to the registry, which forwards to a room actor; no game
/// logic, no state, no decisions live here. The one thing the hub owns is
/// identity plumbing: Context.ConnectionId is how the room knows who asked,
/// and the acting player is always derived from it server-side — a client
/// never names its player (the anti-spoofing rule from IGame).
/// </summary>
public sealed class GameHub(RoomRegistry rooms) : Hub
{
    public Task CreateRoom(string displayName, string gameId)
        => rooms.Create(Context.ConnectionId, displayName, gameId);

    public Task JoinRoom(string roomCode, string displayName)
        => rooms.Join(Context.ConnectionId, roomCode, displayName);

    public Task Resume(string sessionToken)
        => rooms.Resume(Context.ConnectionId, sessionToken);

    public Task SetConfig(AgonyConfig config)
        => rooms.SetConfig(Context.ConnectionId, config);

    public Task Start()
        => rooms.Start(Context.ConnectionId);

    public Task SubmitMove(MovePayload payload)
        => rooms.SubmitMove(Context.ConnectionId, payload);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await rooms.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
