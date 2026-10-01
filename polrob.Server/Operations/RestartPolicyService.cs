using System.Text.Json;

namespace polrob.Server.Operations;

// Registered before record/network workers, so it stops after their cleanup completes.
public sealed class RestartPolicyService(GameRecordOutbox outbox, ServerOperations operations,
    OperationalMetrics metrics, ILogger<RestartPolicyService> logger) : IHostedService
{
    private string StatePath => Path.Combine(outbox.DirectoryPath, ".runtime-state");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(StatePath))
        {
            try
            {
                var previous = JsonSerializer.Deserialize<RuntimeState>(File.ReadAllText(StatePath));
                operations.PreviousShutdownClean = previous?.CleanShutdown;
                if (previous?.CleanShutdown != true)
                {
                    metrics.Add("unclean_restart_total");
                    logger.LogWarning("Previous server boot {BootId} did not shut down cleanly. Pending records will replay; unfinished games are abandoned without a result.", previous?.BootId);
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Previous shutdown marker is unreadable; treating restart state as unknown.");
            }
        }
        WriteState(false);
        logger.LogInformation("Boot {BootId}: restart policy=abandon-unfinished; login sessions reset; completed records replay from persistent storage.", operations.BootId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        operations.BeginDrain();
        WriteState(!operations.HasUnpersistedResults && !operations.NetworkRunning);
        return Task.CompletedTask;
    }

    private void WriteState(bool clean)
    {
        var temporary = StatePath + ".next";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new RuntimeState(operations.BootId, clean, DateTime.UtcNow));
            stream.Flush(true);
        }
        File.Move(temporary, StatePath, overwrite: true);
    }

    private sealed record RuntimeState(string BootId, bool CleanShutdown, DateTime UpdatedAtUtc);
}
