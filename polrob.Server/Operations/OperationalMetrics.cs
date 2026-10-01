using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace polrob.Server.Operations;

// Names are fixed in code. Never put player IDs, room IDs, IPs or tokens in metric names.
public sealed class OperationalMetrics
{
    private readonly ConcurrentDictionary<string, double> _values = new();

    public OperationalMetrics()
    {
        foreach (var name in new[]
        {
            "record_accepted_total", "record_rejected_total", "record_saved_total", "record_retry_total",
            "record_quarantined_total", "outbox_worker_failures_total", "game_result_acceptance_failures_total",
            "room_loop_failures_total", "room_ticks_total", "room_tick_duration_seconds_total", "room_tick_overruns_total",
            "games_aborted_total", "unclean_restart_total", "tcp_connections_rejected_total", "tcp_rate_limited_total",
            "tcp_read_timeouts_total", "tcp_slow_or_failed_total", "hub_connections_rejected_total", "hub_rate_limited_total",
            "http_rate_limited_total", "udp_global_rate_limited_total", "udp_send_dropped_total", "udp_send_failures_total"
        }) _values[name] = 0;
    }

    public void Add(string name, double value = 1) => _values.AddOrUpdate(name, value, (_, old) => old + value);
    public void Set(string name, double value) => _values[name] = value;

    public string Export()
    {
        var output = new StringBuilder();
        foreach (var (name, value) in _values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.Append("# TYPE polrob_").Append(name).Append(name.EndsWith("_total", StringComparison.Ordinal)
                ? " counter\n" : " gauge\n");
            output.Append("polrob_").Append(name).Append(' ')
                .Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        return output.ToString();
    }
}
