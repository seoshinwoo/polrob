using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace polrob.Server.Network;

public sealed class RuntimeMetricSampler : IDisposable
{
    private static readonly HashSet<string> RuntimeCounterNames = new(StringComparer.Ordinal)
    {
        "dotnet.exceptions",
        "dotnet.gc.collections",
        "dotnet.gc.heap.total_allocated",
        "dotnet.gc.pause.time",
        "dotnet.monitor.lock_contentions",
        "dotnet.process.cpu.time",
        "dotnet.process.memory.working_set",
        "dotnet.thread_pool.queue.length",
        "dotnet.thread_pool.thread.count"
    };

    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, RuntimeMetricSeries> _series = new();
    private readonly Dictionary<string, double> _previousValues = new(StringComparer.Ordinal);
    private DateTime _previousSampleAtUtc = DateTime.UtcNow;

    public RuntimeMetricSampler()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "System.Runtime" && RuntimeCounterNames.Contains(instrument.Name))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<int>(RecordMeasurement);
        _listener.SetMeasurementEventCallback<long>(RecordMeasurement);
        _listener.SetMeasurementEventCallback<float>(RecordMeasurement);
        _listener.SetMeasurementEventCallback<double>(RecordMeasurement);
        _listener.SetMeasurementEventCallback<decimal>(RecordMeasurement);
        _listener.Start();
    }

    public string Sample()
    {
        _listener.RecordObservableInstruments();

        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Max((now - _previousSampleAtUtc).TotalSeconds, 0.001d);
        _previousSampleAtUtc = now;

        var values = _series.Values
            .GroupBy(series => series.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(series => series.Value),
                StringComparer.Ordinal);

        var exceptionsPerSecond = GetDeltaPerSecond(values, "dotnet.exceptions", elapsedSeconds);
        var gcCollectionsPerSecond = GetDeltaPerSecond(values, "dotnet.gc.collections", elapsedSeconds);
        var gcAllocatedBytesPerSecond = GetDeltaPerSecond(values, "dotnet.gc.heap.total_allocated", elapsedSeconds);
        var gcPauseMsPerSecond = GetDeltaPerSecond(values, "dotnet.gc.pause.time", elapsedSeconds) * 1000d;
        var lockContentionsPerSecond = GetDeltaPerSecond(values, "dotnet.monitor.lock_contentions", elapsedSeconds);
        var cpuSecondsPerSecond = GetDeltaPerSecond(values, "dotnet.process.cpu.time", elapsedSeconds);
        var workingSetBytes = GetCurrent(values, "dotnet.process.memory.working_set");
        var threadPoolQueueLength = GetCurrent(values, "dotnet.thread_pool.queue.length");
        var threadPoolThreadCount = GetCurrent(values, "dotnet.thread_pool.thread.count");

        return
            $"exceptions/s={exceptionsPerSecond:F1} " +
            $"gc_collections/s={gcCollectionsPerSecond:F1} " +
            $"gc_alloc_mb/s={BytesToMegabytes(gcAllocatedBytesPerSecond):F2} " +
            $"gc_pause_ms/s={gcPauseMsPerSecond:F2} " +
            $"lock_contentions/s={lockContentionsPerSecond:F1} " +
            $"cpu_s/s={cpuSecondsPerSecond:F2} " +
            $"working_set_mb={BytesToMegabytes(workingSetBytes):F1} " +
            $"tp_queue={threadPoolQueueLength:F0} " +
            $"tp_threads={threadPoolThreadCount:F0}";
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void RecordMeasurement<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where T : struct
    {
        var seriesKey = CreateSeriesKey(instrument, tags);
        var value = Convert.ToDouble(measurement);
        _series.AddOrUpdate(
            seriesKey,
            _ => new RuntimeMetricSeries(instrument.Name, value),
            (_, series) =>
            {
                series.Value = value;
                return series;
            });
    }

    private double GetDeltaPerSecond(
        IReadOnlyDictionary<string, double> values,
        string name,
        double elapsedSeconds)
    {
        var current = GetCurrent(values, name);
        _previousValues.TryGetValue(name, out var previous);
        _previousValues[name] = current;

        return Math.Max(0d, current - previous) / elapsedSeconds;
    }

    private static double GetCurrent(IReadOnlyDictionary<string, double> values, string name)
    {
        return values.TryGetValue(name, out var value) ? value : 0d;
    }

    private static double BytesToMegabytes(double bytes)
    {
        return bytes / 1024d / 1024d;
    }

    private static string CreateSeriesKey(
        Instrument instrument,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (tags.IsEmpty)
        {
            return instrument.Name;
        }

        var key = instrument.Name;
        foreach (var tag in tags)
        {
            key += $"|{tag.Key}={tag.Value}";
        }

        return key;
    }

    private sealed class RuntimeMetricSeries(string name, double value)
    {
        public string Name { get; } = name;
        public double Value { get; set; } = value;
    }
}
