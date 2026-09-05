using Microsoft.AspNetCore.SignalR;
using TabletopCore.Protocol;

namespace TabletopCore.Server.Rooms;

/// <summary>
/// The one seam between room logic and SignalR: everything a room pushes to
/// a client goes through here as (connection, method name, one payload).
/// Room and registry tests substitute a recording fake and never touch a
/// socket — the same trick the engine plays with its test "clients".
/// </summary>
public interface IClientSender
{
    Task SendAsync(string connectionId, string method, object payload, CancellationToken cancellationToken = default);
}

public static class ClientSenderExtensions
{
    public static Task SendErrorAsync(this IClientSender sender, string connectionId, string code, string message)
        => sender.SendAsync(connectionId, Wire.Error, new ErrorPayload(code, message));
}

internal sealed class SignalRClientSender(IHubContext<GameHub> hub) : IClientSender
{
    public Task SendAsync(string connectionId, string method, object payload, CancellationToken cancellationToken = default)
        => hub.Clients.Client(connectionId).SendAsync(method, payload, cancellationToken);
}
