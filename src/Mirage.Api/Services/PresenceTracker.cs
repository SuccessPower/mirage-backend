using System.Collections.Concurrent;

namespace Mirage.Api.Services;

/// <summary>
/// Tracks which users currently hold at least one live ChatHub connection.
/// </summary>
/// <remarks>
/// State is per-process and deliberately in-memory: presence is ephemeral, and a restart
/// simply reports everyone offline until their client reconnects (SignalR does this within
/// seconds). If the API is ever scaled past a single instance this needs to move behind the
/// SignalR backplane — until then a dictionary is both correct and free.
/// </remarks>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<Guid, HashSet<string>> connectionsByUser = new();

    /// <summary>Registers a connection. Returns true when the user just came online.</summary>
    public bool Connect(Guid userId, string connectionId)
    {
        while (true)
        {
            var connections = connectionsByUser.GetOrAdd(userId, static _ => []);
            lock (connections)
            {
                // Disconnect evicts an emptied set from the dictionary. If that happened between
                // the GetOrAdd above and this lock, the set is orphaned and adding to it would
                // silently lose the connection — take a fresh one instead.
                if (!connectionsByUser.TryGetValue(userId, out var current) || !ReferenceEquals(current, connections))
                    continue;

                var wasOffline = connections.Count == 0;
                connections.Add(connectionId);
                return wasOffline;
            }
        }
    }

    /// <summary>Removes a connection. Returns true when the user's last connection went away.</summary>
    public bool Disconnect(Guid userId, string connectionId)
    {
        if (!connectionsByUser.TryGetValue(userId, out var connections)) return false;
        lock (connections)
        {
            connections.Remove(connectionId);
            if (connections.Count > 0) return false;

            // Drop the empty set so the dictionary does not grow with every user who ever signed in.
            connectionsByUser.TryRemove(new KeyValuePair<Guid, HashSet<string>>(userId, connections));
            return true;
        }
    }

    public bool IsOnline(Guid userId) =>
        connectionsByUser.TryGetValue(userId, out var connections) && connections.Count > 0;

    public IReadOnlySet<Guid> OnlineAmong(IEnumerable<Guid> userIds) =>
        userIds.Where(IsOnline).ToHashSet();

    /// <summary>Returns a point-in-time snapshot for relevance ranking; callers must not cache it.</summary>
    public IReadOnlySet<Guid> OnlineUserIds() => connectionsByUser.Keys.Where(IsOnline).ToHashSet();
}
