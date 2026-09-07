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

internal sealed record EndGameCommand(string ConnectionId, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

/// <summary>Deal again immediately with the same seats and house rules.</summary>
internal sealed record RematchCommand(string ConnectionId, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

/// <summary>Return everyone to the lobby, where house rules can change
/// before the next deal.</summary>
internal sealed record BackToLobbyCommand(string ConnectionId, TaskCompletionSource<bool> Done)
    : RoomCommand(ConnectionId, Done);

/// <summary>Posted by the room's own timer, not a client — hence no real
/// connection. Runs the grace/GC sweep inside the actor like everything
/// else, so time-driven logic gets the same single-threaded guarantees.</summary>
internal sealed record SweepCommand(TaskCompletionSource<bool> Done) : RoomCommand("", Done);
