using System.Diagnostics;

namespace polrob.Server.Network;

public partial class GameNetworkServer
{
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _operations?.BeginDrain();
        Interlocked.Exchange(ref _draining, 1);
        var deadline = Stopwatch.StartNew();
        _logger.LogInformation("Draining game server for up to {Seconds}s; new games are blocked.", _drainSeconds);
        try
        {
            while (deadline.Elapsed < TimeSpan.FromSeconds(_drainSeconds) &&
                   _gameSessions.Values.Any(room => room.GamePhase == polrob.Shared.GamePhase.Playing ||
                       room.PendingGameRecord != null))
                await Task.Delay(100, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

        await base.StopAsync(cancellationToken);
    }

    private async Task ObserveTcpClientAsync(long id, Task task)
    {
        try { await task; }
        catch (Exception ex) { _logger.LogError(ex, "TCP connection task failed."); }
        finally { _tcpClientTasks.TryRemove(id, out _); }
    }
}
