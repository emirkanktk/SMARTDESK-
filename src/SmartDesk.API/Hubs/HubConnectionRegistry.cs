using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.API.Hubs;

// The open hub connections per user, so they can be cut when the account changes (ADR-0021).
// The bearer token is only checked when a connection opens; without this a disabled user's
// tab would keep receiving reservation events until it happened to reconnect. Aborting makes
// the client reconnect, and the reconnect goes through the token checks again.
public sealed class HubConnectionRegistry : IRealtimeSessions
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, HubCallerContext>> _byUser = new();

    public void Add(Guid userId, HubCallerContext connection) =>
        _byUser.GetOrAdd(userId, _ => new()).TryAdd(connection.ConnectionId, connection);

    public void Remove(Guid userId, string connectionId)
    {
        if (_byUser.TryGetValue(userId, out var connections))
        {
            connections.TryRemove(connectionId, out _);
        }
    }

    public int CountFor(Guid userId) => _byUser.TryGetValue(userId, out var connections) ? connections.Count : 0;

    public void End(Guid userId)
    {
        if (!_byUser.TryRemove(userId, out var connections))
        {
            return;
        }

        foreach (var connection in connections.Values)
        {
            connection.Abort();
        }
    }
}
