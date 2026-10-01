using System.Text.Json;
using polrob.Shared;

namespace polrob.Server.Network;

public partial class GameNetworkServer
{
    private int _samplingMetrics;

    // 부하 테스트에서 필요한 초당 패킷/직렬화 수와 현재 서버 상태를 터미널에 출력합니다.
    private void LogLoadMetricsCallback(object? state)
    {
        if (Interlocked.Exchange(ref _samplingMetrics, 1) != 0) return;
        try { SampleLoadMetrics(); }
        catch (Exception ex) { _logger.LogError(ex, "Runtime metric sampling failed."); }
        finally { Volatile.Write(ref _samplingMetrics, 0); }
    }

    private void SampleLoadMetrics()
    {
        var udpReceived = Interlocked.Exchange(ref _udpPacketsReceivedThisSecond, 0);
        var udpSent = Interlocked.Exchange(ref _udpPacketsSentThisSecond, 0);
        var udpBytesReceived = Interlocked.Exchange(ref _udpBytesReceivedThisSecond, 0);
        var udpBytesSent = Interlocked.Exchange(ref _udpBytesSentThisSecond, 0);
        var udpRateLimited = Interlocked.Exchange(ref _udpPacketsRateLimitedThisSecond, 0);
        var udpInvalid = Interlocked.Exchange(ref _udpPacketsInvalidThisSecond, 0);
        var udpDuplicateOrLate = Interlocked.Exchange(ref _udpPacketsDuplicateOrLateThisSecond, 0);
        var roomCommandsDropped = Interlocked.Exchange(ref _roomCommandsDroppedThisSecond, 0);
        var tcpSendFailures = Interlocked.Exchange(ref _tcpSendFailuresThisSecond, 0);
        var tcpSent = Interlocked.Exchange(ref _tcpPacketsSentThisSecond, 0);
        var jsonSerializations = Interlocked.Exchange(ref _jsonSerializationsThisSecond, 0);
        var udpReceiveAverageBytes = udpReceived > 0 ? (double)udpBytesReceived / udpReceived : 0d;
        var udpSendAverageBytes = udpSent > 0 ? (double)udpBytesSent / udpSent : 0d;
        var currentConnections = Volatile.Read(ref _currentTcpConnections);
        var gameSessions = _gameSessions.Values.ToList();
        var currentRooms = gameSessions.Count;
        var currentPlayers = gameSessions.Sum(session => session.Sessions.Count);
        var roomCommandQueueLength = gameSessions.Sum(session => Volatile.Read(ref session.QueuedCommandCount));
        var waitingRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Waiting);
        var countdownRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Countdown);
        var playingRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Playing);
        var endedRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Ended);
        var roomLoad = _gameRoomService.GetLoadSnapshot();
        var runtimeMetrics = _runtimeMetrics.Sample();

        _metrics?.Add("udp_received_total", udpReceived);
        _metrics?.Add("udp_sent_total", udpSent);
        _metrics?.Add("udp_rate_limited_total", udpRateLimited);
        _metrics?.Add("udp_invalid_total", udpInvalid);
        _metrics?.Add("udp_duplicate_or_late_total", udpDuplicateOrLate);
        _metrics?.Add("room_commands_dropped_total", roomCommandsDropped);
        _metrics?.Add("tcp_send_failures_total", tcpSendFailures);
        _metrics?.Add("tcp_sent_total", tcpSent);
        _metrics?.Set("tcp_connections", currentConnections);
        _metrics?.Set("rooms", currentRooms);
        _metrics?.Set("players", currentPlayers);
        _metrics?.Set("room_command_queue", roomCommandQueueLength);
        _metrics?.Set("playing_rooms", playingRooms);
        _metrics?.Set("lobby_rooms", roomLoad.TotalRooms);
        _metrics?.Set("udp_pending_sends", Volatile.Read(ref _pendingUdpSends));

        Console.WriteLine(
            "[LoadMetrics] " +
            $"udp_recv/s={udpReceived} " +
            $"udp_send/s={udpSent} " +
            $"udp_recv_bytes/s={udpBytesReceived} " +
            $"udp_send_bytes/s={udpBytesSent} " +
            $"udp_recv_avg_bytes={udpReceiveAverageBytes:F2} " +
            $"udp_send_avg_bytes={udpSendAverageBytes:F2} " +
            $"udp_rate_limited/s={udpRateLimited} " +
            $"udp_invalid/s={udpInvalid} " +
            $"udp_duplicate_or_late/s={udpDuplicateOrLate} " +
            $"tcp_send/s={tcpSent} " +
            $"tcp_send_failures/s={tcpSendFailures} " +
            $"json_serialize/s={jsonSerializations} " +
            $"room_command_queue={roomCommandQueueLength} " +
            $"room_command_dropped/s={roomCommandsDropped} " +
            $"connections={currentConnections} " +
            $"players={currentPlayers} " +
            $"rooms={currentRooms} " +
            $"waiting_rooms={waitingRooms} " +
            $"countdown_rooms={countdownRooms} " +
            $"playing_rooms={playingRooms} " +
            $"ended_rooms={endedRooms} " +
            $"game_tcp_players={currentPlayers} " +
            $"game_tcp_rooms={currentRooms} " +
            $"lobby_players={roomLoad.TotalPlayers} " +
            $"lobby_rooms={roomLoad.TotalRooms} " +
            $"random_players={roomLoad.RandomPlayers} " +
            $"random_rooms={roomLoad.RandomRooms} " +
            $"random_matched_rooms={roomLoad.RandomMatchedRooms} " +
            $"random_in_game_rooms={roomLoad.RandomInGameRooms} " +
            runtimeMetrics);
    }

    private string SerializeForMetrics<T>(T value)
    {
        Interlocked.Increment(ref _jsonSerializationsThisSecond);
        return JsonSerializer.Serialize(value);
    }
}
