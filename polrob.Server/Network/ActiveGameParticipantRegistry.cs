using System.Collections.Concurrent;

namespace polrob.Server.Network;

public sealed class ActiveGameParticipantRegistry
{
    private static readonly TimeSpan DefaultActiveLease = TimeSpan.FromSeconds(45);

    private readonly ConcurrentDictionary<(string RoomId, string UserId), ConnectionLease>
        _connections = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _activeLease;

    public ActiveGameParticipantRegistry()
        : this(TimeProvider.System, DefaultActiveLease)
    {
    }

    public ActiveGameParticipantRegistry(
        TimeProvider timeProvider,
        TimeSpan activeLease)
    {
        _timeProvider = timeProvider;
        _activeLease = activeLease;
    }

    public void Register(string roomId, string userId, string connectionId)
    {
        _connections[(roomId, userId)] = new ConnectionLease(
            connectionId,
            _timeProvider.GetUtcNow());
    }

    public bool Refresh(string roomId, string userId, string connectionId)
    {
        var key = (roomId, userId);
        while (_connections.TryGetValue(key, out var current))
        {
            if (!string.Equals(current.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return false;
            }

            var refreshed = current with { LastSeenUtc = _timeProvider.GetUtcNow() };
            if (_connections.TryUpdate(key, refreshed, current))
            {
                return true;
            }
        }

        return false;
    }

    public void Unregister(string roomId, string userId, string connectionId)
    {
        if (!_connections.TryGetValue((roomId, userId), out var current) ||
            !string.Equals(current.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            return;
        }

        var entry = new KeyValuePair<(string RoomId, string UserId), ConnectionLease>(
            (roomId, userId),
            current);
        ((ICollection<KeyValuePair<(string RoomId, string UserId), ConnectionLease>>)_connections)
            .Remove(entry);
    }

    public bool IsActive(string roomId, string userId)
    {
        return TryGetConnectionId(roomId, userId, out _);
    }

    public bool TryGetConnectionId(string roomId, string userId, out string connectionId)
    {
        if (_connections.TryGetValue((roomId, userId), out var lease) &&
            _timeProvider.GetUtcNow() - lease.LastSeenUtc <= _activeLease)
        {
            connectionId = lease.ConnectionId;
            return true;
        }

        connectionId = string.Empty;
        return false;
    }

    private sealed record ConnectionLease(string ConnectionId, DateTimeOffset LastSeenUtc);
}
