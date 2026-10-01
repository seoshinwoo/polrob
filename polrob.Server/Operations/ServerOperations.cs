namespace polrob.Server.Operations;

public sealed class ServerOperations
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _unpersisted = new();
    private int _draining;
    private int _started;
    private int _networkRunning;
    public string BootId { get; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    public bool? PreviousShutdownClean { get; set; }
    public bool IsDraining => Volatile.Read(ref _draining) != 0;
    public bool HasStarted => Volatile.Read(ref _started) != 0;
    public bool NetworkRunning => Volatile.Read(ref _networkRunning) != 0;
    public void MarkNetworkRunning(bool running) => Interlocked.Exchange(ref _networkRunning, running ? 1 : 0);
    public bool HasUnpersistedResults => !_unpersisted.IsEmpty;
    public void SetUnpersisted(string gameId, bool pending)
    {
        if (pending) _unpersisted[gameId] = 0;
        else _unpersisted.TryRemove(gameId, out _);
    }
    public void MarkStarted() => Interlocked.Exchange(ref _started, 1);
    public void BeginDrain() => Interlocked.Exchange(ref _draining, 1);
}
