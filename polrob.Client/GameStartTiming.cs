using System.Diagnostics;

namespace polrob.Client;

/// <summary>Records one process-local game-start trace using monotonic timestamps.</summary>
internal static class GameStartTiming
{
    private static readonly object Sync = new();
    private static Stopwatch? _trace;
    private static long _lastCheckpoint;
    private static string _traceId = "none";

    public static void BeginTrace(string message)
    {
        lock (Sync)
        {
            _trace = Stopwatch.StartNew();
            _lastCheckpoint = Stopwatch.GetTimestamp();
            _traceId = Guid.NewGuid().ToString("N")[..8];
            WriteCore(message, 0, 0);
        }
    }

    public static void EnsureTraceStarted(string message)
    {
        lock (Sync)
        {
            if (_trace != null)
            {
                return;
            }

            _trace = Stopwatch.StartNew();
            _lastCheckpoint = Stopwatch.GetTimestamp();
            _traceId = Guid.NewGuid().ToString("N")[..8];
            WriteCore(message, 0, 0);
        }
    }

    public static long StartSegment() => Stopwatch.GetTimestamp();

    public static void CompleteSegment(string message, long startedAt) =>
        Write(message, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

    public static void Mark(string message) => Write(message, null);

    private static void Write(string message, double? segmentMilliseconds)
    {
        lock (Sync)
        {
            if (_trace == null)
            {
                _trace = Stopwatch.StartNew();
                _lastCheckpoint = Stopwatch.GetTimestamp();
                _traceId = Guid.NewGuid().ToString("N")[..8];
            }

            var now = Stopwatch.GetTimestamp();
            var sinceLastMilliseconds = Stopwatch.GetElapsedTime(_lastCheckpoint, now).TotalMilliseconds;
            _lastCheckpoint = now;
            WriteCore(message, segmentMilliseconds, sinceLastMilliseconds);
        }
    }

    private static void WriteCore(string message, double? segmentMilliseconds, double sinceLastMilliseconds)
    {
        var segment = segmentMilliseconds.HasValue
            ? $" segment={segmentMilliseconds.Value:F1}ms"
            : string.Empty;
        var line = $"[GameStartTiming {_traceId}] total={_trace?.Elapsed.TotalMilliseconds ?? 0:F1}ms" +
                   $" sinceLast={sinceLastMilliseconds:F1}ms{segment} | {message}";
        Debug.WriteLine(line);
        Console.WriteLine(line);
    }
}
