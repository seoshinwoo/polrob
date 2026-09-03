using System.Net.Sockets;
using System.Text.Json;
using polrob.Server.Controllers;
using polrob.Shared;

namespace polrob.Server.Network;

public partial class GameNetworkServer
{
    // TCP 접속을 계속 기다리다가 새 클라이언트마다 처리 작업을 시작합니다.
    private async Task AcceptTcpClientsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await _tcpListener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleTcpClientAsync(client, stoppingToken), stoppingToken);
            }
            catch (Exception ex)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                _logger.LogError(ex, "TCP accept loop failed unexpectedly.");
            }
        }
    }

    // TCP 클라이언트 하나의 입장 패킷과 연결 종료 정리를 담당합니다.
    private Task HandleTcpClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        using var stream = client.GetStream();
        using var reader = new BinaryReader(stream);
        using var writer = new BinaryWriter(stream);
        Interlocked.Increment(ref _currentTcpConnections);

        string? playerId = null;
        string? roomId = null;
        string? connectionId = null;

        try
        {
            while (client.Connected && !stoppingToken.IsCancellationRequested)
            {
                // Simple binary protocol frame:
                // [Int32 Payload Length]
                // [Byte Packet Type]
                // [String JSON Payload]
                _ = reader.ReadInt32();
                var type = (TcpMessageType)reader.ReadByte();
                var json = reader.ReadString();

                if (type == TcpMessageType.Join)
                {
                    if (playerId is not null)
                    {
                        throw new InvalidDataException("하나의 TCP 연결에서는 한 번만 게임에 입장할 수 있습니다.");
                    }

                    var joinRequest = JsonSerializer.Deserialize<GameJoinRequest>(json);
                    if (joinRequest == null ||
                        !AuthController.ValidateSession(joinRequest.SessionToken, out var authenticatedUserId) ||
                        string.IsNullOrWhiteSpace(authenticatedUserId))
                    {
                        throw new InvalidDataException("TCP 게임 입장 인증에 실패했습니다.");
                    }

                    roomId = NormalizeRoomId(joinRequest.RoomId);
                    var player = _gameRoomService.GetAuthenticatedGamePlayer(roomId, authenticatedUserId);
                    if (player == null)
                    {
                        throw new InvalidDataException("인증된 사용자가 해당 방에 참여 중이지 않습니다.");
                    }

                    playerId = authenticatedUserId;
                    connectionId = Guid.NewGuid().ToString("N");

                    var joinCommand = new JoinRoomCommand(player, client, writer, connectionId);
                    var gameSession = GetOrCreateGameSession(roomId, stoppingToken);
                    if (!TryWriteRoomCommand(gameSession, joinCommand))
                    {
                        throw new InvalidOperationException($"Room command queue is full for room {roomId}.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "TCP client disconnected.");
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            _logger.LogWarning(ex, "Rejected TCP game client join request.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected TCP client handler failure.");
        }
        finally
        {
            if (playerId != null && roomId != null && connectionId != null &&
                _gameSessions.TryGetValue(roomId, out var gameSession))
            {
                if (!TryWriteRoomCommand(gameSession, new LeaveRoomCommand(playerId, connectionId)))
                {
                    RemovePlayerRoomRegistration(playerId, connectionId);
                }
            }
            else if (playerId != null && connectionId != null)
            {
                RemovePlayerRoomRegistration(playerId, connectionId);
            }

            Interlocked.Decrement(ref _currentTcpConnections);
            client.Close();
        }

        return Task.CompletedTask;
    }

    // 타입과 JSON payload를 정해진 TCP 프레임 형식으로 전송합니다.
    private void SendTcp(BinaryWriter writer, TcpMessageType type, string payload)
    {
        lock (writer)
        {
            writer.Write(payload.Length + 1);
            writer.Write((byte)type);
            writer.Write(payload);
            Interlocked.Increment(ref _tcpPacketsSentThisSecond);
        }
    }

    private bool TrySendTcp(BinaryWriter writer, TcpMessageType type, string payload)
    {
        try
        {
            SendTcp(writer, type, payload);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Dead TCP connections are cleaned up by their read loop.
            Interlocked.Increment(ref _tcpSendFailuresThisSecond);
            _logger.LogDebug(ex, "TCP send failed because the client connection is gone.");
            return false;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _tcpSendFailuresThisSecond);
            _logger.LogWarning(ex, "TCP send failed unexpectedly.");
            return false;
        }
    }

    // 한 방의 TCP 클라이언트들에게 메시지를 보내고 필요하면 특정 플레이어는 제외합니다.
    private void BroadcastTcp(GameSession gameSession, TcpMessageType type, string payload, string? excludeId)
    {
        foreach (var kvp in gameSession.Sessions)
        {
            if (kvp.Key == excludeId)
            {
                continue;
            }

            TrySendTcp(kvp.Value.Writer, type, payload);
        }
    }

    // 위치 상태는 같은 역할 또는 현재 이 플레이어를 보고 있는 상대에게 보냅니다.
    private void BroadcastPlayerState(GameSession gameSession, Player player)
    {
        RefreshOpponentVisibility(gameSession);
        RefreshOpponentProximityAlerts(gameSession);
        var payload = SerializeForMetrics(player);

        foreach (var session in gameSession.Sessions.Values)
        {
            if (session.PlayerState.Role == player.Role ||
                session.VisibleOpponentPlayerIds.Contains(player.Id))
            {
                TrySendTcp(session.Writer, TcpMessageType.PlayerState, payload);
            }
        }
    }

    // 시야와 무관하게 가장 가까운 상대 팀과의 거리 단계만 보냅니다.
    // 좌표나 상대 ID는 전송하지 않아 시야 밖 위치는 노출되지 않습니다.
    private void RefreshOpponentProximityAlerts(GameSession gameSession)
    {
        var sessions = gameSession.Sessions.Values.ToList();

        foreach (var recipientSession in sessions)
        {
            var recipient = recipientSession.PlayerState;
            var nearestSurfaceDistance = float.PositiveInfinity;

            foreach (var opponentSession in sessions)
            {
                var opponent = opponentSession.PlayerState;
                if (opponent.Role == recipient.Role)
                {
                    continue;
                }

                var deltaX = opponent.X - recipient.X;
                var deltaY = opponent.Y - recipient.Y;
                var centerDistance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                var surfaceDistance = MathF.Max(
                    0f,
                    centerDistance - recipient.Radius - opponent.Radius);
                nearestSurfaceDistance = MathF.Min(nearestSurfaceDistance, surfaceDistance);
            }

            var pulseMilliseconds = OpponentProximitySync.FromSurfaceDistance(nearestSurfaceDistance);
            if (recipientSession.LastOpponentProximityPulseMilliseconds == pulseMilliseconds)
            {
                continue;
            }

            recipientSession.LastOpponentProximityPulseMilliseconds = pulseMilliseconds;
            TrySendTcp(
                recipientSession.Writer,
                TcpMessageType.OpponentProximity,
                SerializeForMetrics(new OpponentProximitySync
                {
                    PulseMilliseconds = pulseMilliseconds
                }));
        }
    }

    // 팀원 중 한 명이라도 상대를 보면 팀 전체의 클라이언트 목록에 추가하고, 아무도 못 보면 제거합니다.
    private void RefreshOpponentVisibility(GameSession gameSession)
    {
        var sessions = gameSession.Sessions.Values.ToList();

        foreach (var role in Enum.GetValues<PlayerRole>())
        {
            var teamSessions = sessions
                .Where(s => s.PlayerState.Role == role)
                .ToList();
            var opponentSessions = sessions
                .Where(s => s.PlayerState.Role != role)
                .ToList();

            foreach (var targetSession in opponentSessions)
            {
                var target = targetSession.PlayerState;
                var isVisibleToTeam = IsPlayerVisibleToTeam(teamSessions, role, target);

                foreach (var recipientSession in teamSessions)
                {
                    if (isVisibleToTeam)
                    {
                        if (recipientSession.VisibleOpponentPlayerIds.Add(target.Id))
                        {
                            TrySendTcp(
                                recipientSession.Writer,
                                TcpMessageType.Joined,
                                SerializeForMetrics(target));
                        }
                    }
                    else if (recipientSession.VisibleOpponentPlayerIds.Remove(target.Id))
                    {
                        TrySendTcp(recipientSession.Writer, TcpMessageType.Left, target.Id);
                    }
                }
            }
        }
    }

    private bool IsPlayerVisibleToTeam(
        IEnumerable<PlayerSession> sessions,
        PlayerRole teamRole,
        Player target)
    {
        if (teamRole == PlayerRole.Police &&
            target.Role == PlayerRole.Robber &&
            IsInJail(target))
        {
            return true;
        }

        return sessions.Any(session =>
            session.PlayerState.Role == teamRole &&
            IsPointInVision(session.PlayerState, target.X, target.Y));
    }

    // 한 방에서 지정한 역할의 TCP 클라이언트들에게만 메시지를 보냅니다.
    private void BroadcastTcpToRole(
        GameSession gameSession,
        PlayerRole role,
        TcpMessageType type,
        string payload,
        string? excludeId)
    {
        foreach (var kvp in gameSession.Sessions)
        {
            if (kvp.Key == excludeId || kvp.Value.PlayerState.Role != role)
            {
                continue;
            }

            TrySendTcp(kvp.Value.Writer, type, payload);
        }
    }

    // UDP 조이스틱 입력을 받아 해당 방의 단일 처리 큐에 넣습니다.
    private async Task ReceiveUdpAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(stoppingToken);
                Interlocked.Increment(ref _udpPacketsReceivedThisSecond);
                Interlocked.Add(ref _udpBytesReceivedThisSecond, result.Buffer.Length);
                var movement = JsonSerializer.Deserialize<PlayerMovementInput>(result.Buffer);

                if (movement == null || !_playerRooms.TryGetValue(movement.Id, out var registration))
                {
                    Interlocked.Increment(ref _udpPacketsInvalidThisSecond);
                    continue;
                }

                if (!_gameSessions.TryGetValue(registration.RoomId, out var gameSession))
                {
                    continue;
                }

                var rateLimiter = _udpRateLimits.GetOrAdd(movement.Id, _ => new UdpRateLimitState(_udpBurstSize));
                if (!rateLimiter.TryConsume(DateTime.UtcNow, _udpPacketsPerSecond, _udpBurstSize))
                {
                    Interlocked.Increment(ref _udpPacketsRateLimitedThisSecond);
                    continue;
                }

                TryWriteRoomCommand(gameSession, new MoveRoomCommand(movement, result.RemoteEndPoint));
            }
            catch (JsonException ex)
            {
                Interlocked.Increment(ref _udpPacketsInvalidThisSecond);
                _logger.LogDebug(ex, "Ignored malformed UDP movement packet.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "UDP receive failed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected UDP receive loop failure.");
            }
        }
    }

    // 같은 역할과 현재 이동 플레이어를 보고 있는 상대의 UDP endpoint에 위치를 보냅니다.
    private void BroadcastUdpToVisiblePlayers(
        GameSession gameSession,
        Player movingPlayer,
        byte[] buffer)
    {
        foreach (var kvp in gameSession.Sessions)
        {
            var recipient = kvp.Value;
            if (recipient.PlayerState.Role != movingPlayer.Role &&
                !recipient.VisibleOpponentPlayerIds.Contains(movingPlayer.Id))
            {
                continue;
            }

            if (recipient.UdpEndPoint != null)
            {
                _ = _udpClient.SendAsync(buffer, buffer.Length, recipient.UdpEndPoint);
                Interlocked.Increment(ref _udpPacketsSentThisSecond);
                Interlocked.Add(ref _udpBytesSentThisSecond, buffer.Length);
            }
        }
    }

    // 비어 있는 roomId를 기본 방 ID로 보정합니다.
    private static string NormalizeRoomId(string? roomId)
    {
        return string.IsNullOrWhiteSpace(roomId) ? DefaultRoomId : roomId;
    }
}
