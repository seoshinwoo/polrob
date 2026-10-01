using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class GameRecordOutboxOptions
{
    public string Directory { get; set; } = "data/game-record-outbox";
    public int MaxRecords { get; set; } = 10_000;
    public long MaxBytes { get; set; } = 268_435_456;
    public int MaxRecordBytes { get; set; } = 65_536;
    public double AdmissionThreshold { get; set; } = 0.8;
    public int RetrySeconds { get; set; } = 5;
    public int WriteTimeoutSeconds { get; set; } = 15;
}

public sealed record OutboxSnapshot(int Pending, int Quarantined, long Bytes, bool WriteFailed, double OldestAgeSeconds);

// One directory per server instance, on a persistent volume. A lock prevents two workers
// from consuming the same spool. Pending files are the queue; no unbounded RAM channel exists.
public sealed class GameRecordOutbox : IDisposable
{
    private readonly object _gate = new();
    private readonly GameRecordOutboxOptions _options;
    private readonly ILogger<GameRecordOutbox> _logger;
    private readonly FileStream _ownership;
    private int _pending;
    private int _quarantined;
    private long _bytes;
    private bool _writeFailed;
    public string DirectoryPath { get; }

    public GameRecordOutbox(IOptions<GameRecordOutboxOptions> options, ILogger<GameRecordOutbox> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (_options.MaxRecords < 1 || _options.MaxRecordBytes < 1024 ||
            _options.MaxBytes < _options.MaxRecordBytes ||
            _options.AdmissionThreshold is <= 0 or > 1 ||
            _options.RetrySeconds < 1 || _options.WriteTimeoutSeconds < 1)
            throw new InvalidOperationException("Invalid GameRecords outbox limits.");
        DirectoryPath = Path.GetFullPath(_options.Directory);
        Directory.CreateDirectory(DirectoryPath);
        _ownership = new FileStream(Path.Combine(DirectoryPath, ".owner.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            // A complete temporary file may have survived a crash before rename. Replaying
            // it is safe because the Cosmos write is idempotent by game ID.
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.tmp"))
            {
                var destination = Path.ChangeExtension(path, ".json");
                if (File.Exists(destination)) File.Delete(path);
                else File.Move(path, destination);
            }
            foreach (var path in Directory.EnumerateFiles(DirectoryPath))
            {
                if (path.EndsWith(".json", StringComparison.Ordinal)) _pending++;
                else if (path.EndsWith(".failed", StringComparison.Ordinal)) _quarantined++;
                else continue;
                _bytes += new FileInfo(path).Length;
            }
        }
        catch { _ownership.Dispose(); throw; }
    }

    public bool CanAcceptGames
    {
        get
        {
            lock (_gate) return !_writeFailed &&
                _pending + _quarantined < _options.MaxRecords * _options.AdmissionThreshold &&
                _bytes < _options.MaxBytes * _options.AdmissionThreshold;
        }
    }

    public OutboxSnapshot Snapshot()
    {
        lock (_gate)
        {
            var oldest = Directory.EnumerateFiles(DirectoryPath, "*.json")
                .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.UtcNow).Min();
            return new(_pending, _quarantined, _bytes, _writeFailed,
                Math.Max(0, (DateTime.UtcNow - oldest).TotalSeconds));
        }
    }

    public bool TryAppend(CompletedGameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.Id)) throw new ArgumentException("Game ID is required.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        if (bytes.Length > _options.MaxRecordBytes) return false;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Id)));
        var path = Path.Combine(DirectoryPath, name + ".json");
        lock (_gate)
        {
            try
            {
                if (File.Exists(path))
                {
                    // Repeated completion is accepted only for the identical snapshot.
                    return File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
                }
                if (File.Exists(Path.ChangeExtension(path, ".failed"))) return false;
                if (_pending + _quarantined >= _options.MaxRecords || _bytes + bytes.Length > _options.MaxBytes)
                    return false;
                var temporary = Path.ChangeExtension(path, ".tmp");
                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, path);
                _pending++;
                _bytes += bytes.Length;
                _writeFailed = false;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writeFailed = true;
                _logger.LogCritical(ex, "Could not persist completed game {GameRecordId}; new games are blocked.", record.Id);
                return false;
            }
        }
    }

    public IEnumerable<string> PendingFiles() => Directory.EnumerateFiles(DirectoryPath, "*.json");

    public CompletedGameRecord Read(string path)
    {
        if (new FileInfo(path).Length > _options.MaxRecordBytes)
            throw new InvalidDataException("Outbox record exceeds configured size limit.");
        var record = JsonSerializer.Deserialize<CompletedGameRecord>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("Outbox record is empty.");
        if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.RoomId) ||
            record.PolicePlayerIds == null || record.RobberPlayerIds == null || !Enum.IsDefined(record.WinnerRole))
            throw new InvalidDataException("Outbox record is missing required fields.");
        return record;
    }

    public void Acknowledge(string path)
    {
        lock (_gate)
        {
            var length = new FileInfo(path).Length;
            File.Delete(path);
            _pending--;
            _bytes -= length;
        }
    }

    public void Quarantine(string path)
    {
        lock (_gate)
        {
            File.Move(path, Path.ChangeExtension(path, ".failed"));
            _pending--;
            _quarantined++;
        }
    }

    // Readiness must recover after a transient disk problem without admitting a game
    // just to discover whether writes work again.
    public void ProbeWritable()
    {
        lock (_gate)
        {
            try
            {
                var path = Path.Combine(DirectoryPath, ".write-probe");
                using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                { file.WriteByte(1); file.Flush(true); }
                File.Delete(path);
                _writeFailed = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _writeFailed = true;
                _logger.LogError(ex, "Game record outbox is not writable.");
            }
        }
    }

    public void Dispose() => _ownership.Dispose();
}
