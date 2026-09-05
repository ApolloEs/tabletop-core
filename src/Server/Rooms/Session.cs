using TabletopCore.Engine;

namespace TabletopCore.Server.Rooms;

/// <summary>
/// A seat at the table — identity that OUTLIVES connections. The token is
/// what a client stores and presents to Resume after a drop; the connection
/// id is merely where that seat's messages go right now (null while
/// disconnected). All mutable members are touched only inside the owning
/// room's actor loop, so no locking is needed or wanted here.
/// </summary>
public sealed class Session
{
    public string Token { get; } = Guid.NewGuid().ToString("N");

    public required int Seat { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Assigned when the game starts (players are added to the
    /// GameState in seat order); default until then.</summary>
    public PlayerId PlayerId { get; set; }

    /// <summary>Seat 0 is the creator; host powers (config, start) follow it.</summary>
    public bool IsHost => Seat == 0;

    public string? ConnectionId { get; set; }
    public bool Connected => ConnectionId is not null;

    /// <summary>Ack bookkeeping: move ids are client-chosen, monotonic from 1.
    /// A resubmitted id is answered with the stored reply and never re-applied
    /// — this is what makes "resend after reconnect" safe (exactly-once).</summary>
    public long LastMoveId { get; set; }
    public object? LastMoveReply { get; set; }
}
