using TabletopCore.Protocol;
using TabletopCore.Server.Rooms;

namespace TabletopCore.Server.Tests;

/// <summary>
/// The fake client edge: records every (connection, method, payload) a room
/// pushes. Because a room command's Task completes only after its handler
/// finished sending, a test that awaited the command sees all its sends here
/// — no sleeps, no races.
/// </summary>
internal sealed class RecordingSender : IClientSender
{
    private readonly List<(string Connection, string Method, object Payload)> _sent = [];

    public Task SendAsync(string connectionId, string method, object payload, CancellationToken cancellationToken = default)
    {
        lock (_sent)
            _sent.Add((connectionId, method, payload));
        return Task.CompletedTask;
    }

    public IReadOnlyList<(string Connection, string Method, object Payload)> Sent
    {
        get { lock (_sent) return _sent.ToArray(); }
    }

    public T Last<T>(string connection) where T : class
        => LastOrNull<T>(connection)
           ?? throw new InvalidOperationException($"No {typeof(T).Name} was sent to '{connection}'.");

    public T? LastOrNull<T>(string connection) where T : class
    {
        lock (_sent)
            return _sent.Where(m => m.Connection == connection)
                        .Select(m => m.Payload as T)
                        .LastOrDefault(p => p is not null);
    }

    public IReadOnlyList<T> All<T>(string connection) where T : class
    {
        lock (_sent)
            return _sent.Where(m => m.Connection == connection)
                        .Select(m => m.Payload as T)
                        .Where(p => p is not null)
                        .Select(p => p!)
                        .ToList();
    }
}
