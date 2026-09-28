using Microsoft.AspNetCore.SignalR;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.API.Hubs;

// The open hub connections per user, so they can be cut when the account changes (ADR-0021).
// The bearer token is only checked when a connection opens; without this a disabled user's
// tab would keep receiving reservation events until it happened to reconnect. Aborting makes
// the client reconnect, and the reconnect goes through the token checks again.
//
// One lock for everything: connections open and close rarely, and a lock keeps "remove the
// user once their last connection closes" from racing a new connection being added.
public sealed class HubConnectionRegistry : IRealtimeSessions
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, HubCallerContext>> _byUser = [];

    public void Add(Guid userId, HubCallerContext connection)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var connections))
            {
                _byUser[userId] = connections = [];
            }

            connections[connection.ConnectionId] = connection;
        }
    }

    public void Remove(Guid userId, string connectionId)
    {
        lock (_gate)
        {
            if (_byUser.TryGetValue(userId, out var connections)
                && connections.Remove(connectionId)
                && connections.Count == 0)
            {
                _byUser.Remove(userId);
            }
        }
    }

    public int CountFor(Guid userId)
    {
        lock (_gate)
        {
            return _byUser.TryGetValue(userId, out var connections) ? connections.Count : 0;
        }
    }

    public int UsersConnected
    {
        get
        {
            lock (_gate)
            {
                return _byUser.Count;
            }
        }
    }

    public void End(Guid userId)
    {
        List<HubCallerContext> toAbort;
        lock (_gate)
        {
            if (!_byUser.Remove(userId, out var connections))
            {
                return;
            }

            toAbort = [.. connections.Values];
        }

        // Outside the lock: Abort runs the connection's close handlers, which call Remove.
        foreach (var connection in toAbort)
        {
            connection.Abort();
        }
    }
}
