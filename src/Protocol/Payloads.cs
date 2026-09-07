using TabletopCore.Engine;
using TabletopCore.Engine.Events;
using TabletopCore.Engine.Projection;
using TabletopCore.Games.Agony;

namespace TabletopCore.Protocol;

/// <summary>
/// Wire constants shared by server and clients: the protocol version and the
/// hub→client method names (the strings a client subscribes to). Client→hub
/// method names are the hub's C# method names (SignalR matches those
/// case-insensitively), so only the push direction needs constants.
/// </summary>
public static class Wire
{
    /// <summary>Bump on every incompatible wire change (a renamed field, a
    /// reshaped payload). Clients compare it against the value in `welcome`
    /// and refuse to play quietly on a mismatch: an old server silently
    /// ignoring a renamed field looks exactly like a broken UI otherwise.
    /// v2: AgonyConfig.stackDrawTwo → stackDrawCards + playForPlacings,
    /// StatePayload.winner → standings, welcome gained lastMoveId.</summary>
    public const int ProtocolVersion = 2;

    public const string Welcome = "welcome";
    public const string Lobby = "lobby";
    public const string Catalog = "catalog";
    public const string State = "state";
    public const string MoveAccepted = "moveAccepted";
    public const string MoveRejected = "moveRejected";
    public const string PlayerStatus = "playerStatus";
    public const string Error = "error";
}

/// <summary>Reply to a successful create/join/resume. The session token is
/// the client's identity from here on — it outlives the connection, and
/// presenting it to Resume after a drop restores this seat. LastMoveId is
/// where this seat's move-id sequence stands, so a client restarted from
/// scratch resumes the sequence instead of colliding with the dedupe (a
/// fresh id 1 would read as a resend of an old move).</summary>
public sealed record WelcomePayload(
    string SessionToken, PlayerId PlayerId, int Seat, string RoomCode, int ProtocolVersion, long LastMoveId);

public sealed record LobbyPlayer(int Seat, string Name, bool Connected, bool IsHost);

/// <summary>Broadcast to everyone whenever lobby membership, connection
/// status, or config changes. AgonyConfig as the config type is accepted
/// debt until game #2 forces a generic shape (L5).</summary>
public sealed record LobbyPayload(IReadOnlyList<LobbyPlayer> Players, AgonyConfig Config, bool CanStart);

/// <summary>A move submission. Deliberately an envelope rather than two hub
/// arguments: the Move property is declared as the abstract GameMove, which
/// is what makes System.Text.Json emit the "type" discriminator — a bare
/// argument would serialize as its concrete type, discriminator-less.</summary>
public sealed record MovePayload(long MoveId, GameMove Move);

/// <summary>The per-recipient snapshot pushed after every applied move —
/// a client renders from this alone. Events are that move's redacted slice,
/// optional flavor for animation. LegalMoves are the viewer's own (empty
/// when it isn't their turn). Standings is the finishing order so far —
/// first entry the winner, growing to players − 1 when playing for placings,
/// and empty when the host ends a stalled game. Finished covers every
/// ending, so a client tests that rather than inspecting standings.</summary>
public sealed record StatePayload(
    long Version,
    PlayerView View,
    IReadOnlyList<GameMove> LegalMoves,
    IReadOnlyList<GameEvent> Events,
    IReadOnlyList<PlayerId> Standings,
    bool Finished);

public sealed record MoveAcceptedPayload(long MoveId, long Version);

public sealed record MoveRejectedPayload(long MoveId, string Error);

/// <summary>Abandoned means the disconnect grace period ran out — the seat
/// still resumes if its player returns, but others shouldn't hold their
/// breath, and the host gains the option to end the game.</summary>
public sealed record PlayerStatusPayload(int Seat, bool Connected, bool Abandoned);

public sealed record ErrorPayload(string Code, string Message);
