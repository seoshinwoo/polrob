using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using polrob.Server.Operations;

public sealed class GameRecordWriter : BackgroundService, IGameRecordQueue
{
    private readonly GameRecordOutbox _outbox;
    private readonly IGameRecordStore _store;
    private readonly GameRecordOutboxOptions _options;
    private readonly OperationalMetrics _metrics;
    private readonly ILogger<GameRecordWriter> _logger;

    public GameRecordWriter(GameRecordOutbox outbox, IGameRecordStore store,
        IOptions<GameRecordOutboxOptions> options, OperationalMetrics metrics,
        ILogger<GameRecordWriter> logger)
    {
        _outbox = outbox;
        _store = store;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    // true means a flushed local record exists, not merely that RAM accepted it.
    public bool TryEnqueue(CompletedGameRecord record)
    {
        var accepted = _outbox.TryAppend(record);
        _metrics.Add(accepted ? "record_accepted_total" : "record_rejected_total");
        return accepted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _outbox.ProbeWritable();
                    await ProcessPendingAsync(stoppingToken);
                    PublishMetrics();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _metrics.Add("outbox_worker_failures_total");
                    _logger.LogError(ex, "Outbox scan failed; persisted records will be retried.");
                }
                await Task.Delay(TimeSpan.FromSeconds(_options.RetrySeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        // Shutdown does not wait for an unavailable database. Files remain for the next boot.
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        foreach (var path in _outbox.PendingFiles().Take(64))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompletedGameRecord record;
            try { record = _outbox.Read(path); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                _logger.LogError(ex, "Quarantining corrupt outbox file {FileName}.", Path.GetFileName(path));
                _outbox.Quarantine(path);
                _metrics.Add("record_quarantined_total");
                continue;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(_options.WriteTimeoutSeconds));
            try
            {
                await _store.SaveGameRecordAsync(record, deadline.Token);
                _outbox.Acknowledge(path);
                _metrics.Add("record_saved_total");
                _logger.LogInformation("Saved game {GameRecordId} from durable outbox.", record.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (IsPermanentRecordError(ex))
            {
                _outbox.Quarantine(path);
                _metrics.Add("record_quarantined_total");
                _logger.LogError(ex, "Game {GameRecordId} needs manual repair; preserved in quarantine.", record.Id);
            }
            catch (Exception ex)
            {
                // Includes credentials/configuration errors: do not discard valid records.
                _metrics.Add("record_retry_total");
                _logger.LogWarning(ex, "Game {GameRecordId} remains on disk; retrying after {DelaySeconds}s.",
                    record.Id, _options.RetrySeconds);
                break; // One failing dependency attempt per cycle, avoiding a retry storm.
            }
        }
    }

    private static bool IsPermanentRecordError(Exception ex) => ex is ArgumentException ||
        ex is CosmosException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge };

    public void PublishMetrics()
    {
        var state = _outbox.Snapshot();
        _metrics.Set("outbox_pending", state.Pending);
        _metrics.Set("outbox_quarantined", state.Quarantined);
        _metrics.Set("outbox_bytes", state.Bytes);
        _metrics.Set("outbox_oldest_age_seconds", state.OldestAgeSeconds);
        _metrics.Set("outbox_write_failed", state.WriteFailed ? 1 : 0);
    }
}
