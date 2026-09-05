using TabletopCore.Games.Agony;
using TabletopCore.Protocol;

namespace TabletopCore.Server.Rooms;

/// <summary>
/// The room actor's inbox vocabulary. Hub methods (and disconnect handling)
/// do nothing but wrap their arguments in one of these and await Done; the
/// single consumer task applies them in arrival order, which is what gives
/// the room single-threaded game logic and a total order of moves without a
/// lock anywhere. Done completes when the command has been fully processed
/// (replies sent). ConnectionId lives on the base so failures can always be
/// answered to whoever asked.
/// </summary>
internal abstract record RoomCommand(string ConnectionId, TaskCompletionSource<bool> Done);

internal sealed record JoinCommand(string ConnectionId, string DisplayName, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

internal sealed record ResumeCommand(string ConnectionId, string Token, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

internal sealed record SetConfigCommand(string ConnectionId, AgonyConfig Config, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

internal sealed record StartCommand(string ConnectionId, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

internal sealed record SubmitMoveCommand(string ConnectionId, MovePayload Payload, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

internal sealed record DisconnectCommand(string ConnectionId, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);
