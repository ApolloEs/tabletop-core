using System.Collections.Concurrent;

using TabletopCore.Games.Agony;
using TabletopCore.Protocol;

namespace TabletopCore.Server.Rooms;

/// <summary>
/// The front desk: routes every hub call to the right room. Three maps —
/// code → room (join), session token → room (resume), connection → room
/// (everything else). The registry itself holds no game state and makes no
/// game decisions; concurrency here is only map bookkeeping, which is why
/// ConcurrentDictionary suffices while rooms get a full actor.
/// </summary>
public sealed class RoomRegistry(IClientSender sender, TimeProvider? time = null)
{
    // No 0/O/1/I/L: codes get read aloud across a table.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly ConcurrentDictionary<string, Room> _byCode = new();
    private readonly ConcurrentDictionary<string, Room> _byToken = new();
    private readonly ConcurrentDictionary<string, Room> _byConnection = new();
    private readonly Lock _createLock = new();

    public async Task Create(string connectionId, string displayName, string gameId)
    {
        if (!string.Equals(gameId, "agony", StringComparison.OrdinalIgnoreCase))
        {
            await sender.SendErrorAsync(connectionId, "unknownGame", $"Unknown game '{gameId}'.");
            return;
        }

        Room room;
        lock (_createLock)
        {
            string code;
            do
            {
                code = NewCode();
            } while (_byCode.ContainsKey(code));
            room = new Room(code, sender,
                registerToken: (token, r) => _byToken[token] = r,
                time: time,
                onDefunct: Evict);
            _byCode[code] = room;
        }

        if (await room.Join(connectionId, displayName))
            _byConnection[connectionId] = room;
    }

    public async Task Join(string connectionId, string roomCode, string displayName)
    {
        if (!_byCode.TryGetValue(roomCode.Trim().ToUpperInvariant(), out var room))
        {
            await sender.SendErrorAsync(connectionId, "roomNotFound", $"No room '{roomCode}'.");
            return;
        }
        if (await room.Join(connectionId, displayName))
            _byConnection[connectionId] = room;
    }

    public async Task Resume(string connectionId, string sessionToken)
    {
        if (!_byToken.TryGetValue(sessionToken, out var room))
        {
            await sender.SendErrorAsync(connectionId, "unknownSession", "No seat matches that session token.");
            return;
        }
        if (await room.Resume(connectionId, sessionToken))
            _byConnection[connectionId] = room;
    }

    public Task SetConfig(string connectionId, AgonyConfig config)
        => WithRoom(connectionId, room => room.SetConfig(connectionId, config));

    public Task Start(string connectionId)
        => WithRoom(connectionId, room => room.Start(connectionId));

    public Task SubmitMove(string connectionId, MovePayload payload)
        => WithRoom(connectionId, room => room.SubmitMove(connectionId, payload));

    public Task EndGame(string connectionId)
        => WithRoom(connectionId, room => room.EndGame(connectionId));

    public async Task Disconnect(string connectionId)
    {
        if (_byConnection.TryRemove(connectionId, out var room))
            await room.Disconnect(connectionId);
    }

    /// <summary>A room announced its own shutdown (everyone gone past the
    /// grace period): drop every map entry that points at it.</summary>
    private void Evict(Room defunct)
    {
        foreach (var (code, room) in _byCode)
            if (room == defunct)
                _byCode.TryRemove(code, out _);
        foreach (var (token, room) in _byToken)
            if (room == defunct)
                _byToken.TryRemove(token, out _);
        foreach (var (connection, room) in _byConnection)
            if (room == defunct)
                _byConnection.TryRemove(connection, out _);
    }

    private async Task WithRoom(string connectionId, Func<Room, Task<bool>> action)
    {
        if (_byConnection.TryGetValue(connectionId, out var room))
            await action(room);
        else
            await sender.SendErrorAsync(connectionId, "notInRoom", "Join a room first.");
    }

    private static string NewCode()
        => string.Create(4, CodeAlphabet, static (span, alphabet) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        });
}
