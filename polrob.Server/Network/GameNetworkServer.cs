using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using polrob.Shared;

namespace polrob.Server.Network;

public class GameNetworkServer : BackgroundService
{
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<string, GameSession> _gameSessions = new();
    private readonly ConcurrentDictionary<string, string> _playerRooms = new(); // 
    private readonly GameRoomService _gameRoomService;
    private readonly GameMap _map = new();
    private readonly RuntimeMetricSampler _runtimeMetrics = new();

    private Timer? _metricsTimer;
    private long _udpPacketsReceivedThisSecond;
    private long _udpPacketsSentThisSecond;
    private long _udpBytesReceivedThisSecond;
    private long _udpBytesSentThisSecond;
    private long _tcpPacketsSentThisSecond;
    private long _jsonSerializationsThisSecond;
    private int _currentTcpConnections;
    private static readonly TimeSpan RoomTickInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan UdpMovementBroadcastInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GameRuleTickInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GameStateSyncInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EmptyRoomStopDelay = TimeSpan.FromSeconds(2);
    private const string DefaultRoomId = "default";
    private const float VisionRangePlayerSizeMultiplier = 2.5f;
    private const float VisionConeAngleDegrees = 90f;
    private const int GameDurationSeconds = 300;
    private const double ArrestDurationSeconds = 2d;
    private const double JailBreakDurationSeconds = 3d;
    private const float JailBreakReleaseOffset = 20f;
    private const float JailBreakContactTolerance = 90f;
    private const int TcpListenBacklog = 2048;

    // TCP/UDP 소켓과 방 서비스를 준비합니다.
    public GameNetworkServer(GameRoomService gameRoomService)
    {
        _gameRoomService = gameRoomService;
        // 7777 for reliable TCP (Join, Leave, InitialState)
        _tcpListener = new TcpListener(IPAddress.Any, 7777);
        // 7778 for fast UDP (Movement)
        _udpClient = new UdpClient(7778);
    }

    // 백그라운드 서비스가 시작될 때 TCP/UDP 수신 루프와 메트릭 타이머를 켭니다.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _tcpListener.Start(TcpListenBacklog);
        Console.WriteLine("TCP Server started on port 7777");
        Console.WriteLine("UDP Server started on port 7778");

        _metricsTimer = new Timer(LogLoadMetricsCallback, null, 1000, 1000);

        _ = Task.Run(() => AcceptTcpClientsAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => ReceiveUdpAsync(stoppingToken), stoppingToken);

        await Task.CompletedTask;
    }

    // 서버가 종료될 때 주기적으로 돌던 타이머를 정리합니다.
    public override void Dispose()
    {
        _metricsTimer?.Dispose();
        _runtimeMetrics.Dispose();
        base.Dispose();
    }

    // 부하 테스트에서 필요한 초당 패킷/직렬화 수와 현재 서버 상태를 터미널에 출력합니다.
    private void LogLoadMetricsCallback(object? state)
    {
        var udpReceived = Interlocked.Exchange(ref _udpPacketsReceivedThisSecond, 0);
        var udpSent = Interlocked.Exchange(ref _udpPacketsSentThisSecond, 0);
        var udpBytesReceived = Interlocked.Exchange(ref _udpBytesReceivedThisSecond, 0);
        var udpBytesSent = Interlocked.Exchange(ref _udpBytesSentThisSecond, 0);
        var tcpSent = Interlocked.Exchange(ref _tcpPacketsSentThisSecond, 0);
        var jsonSerializations = Interlocked.Exchange(ref _jsonSerializationsThisSecond, 0);
        var udpReceiveAverageBytes = udpReceived > 0 ? (double)udpBytesReceived / udpReceived : 0d;
        var udpSendAverageBytes = udpSent > 0 ? (double)udpBytesSent / udpSent : 0d;
        var currentConnections = Volatile.Read(ref _currentTcpConnections);
        var gameSessions = _gameSessions.Values.ToList();
        var currentRooms = gameSessions.Count;
        var currentPlayers = gameSessions.Sum(session => session.Sessions.Count);
        var waitingRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Waiting);
        var countdownRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Countdown);
        var playingRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Playing);
        var endedRooms = gameSessions.Count(session => session.GamePhase == GamePhase.Ended);
        var roomLoad = _gameRoomService.GetLoadSnapshot();
        var runtimeMetrics = _runtimeMetrics.Sample();

        Console.WriteLine(
            "[LoadMetrics] " +
            $"udp_recv/s={udpReceived} " +
            $"udp_send/s={udpSent} " +
            $"udp_recv_bytes/s={udpBytesReceived} " +
            $"udp_send_bytes/s={udpBytesSent} " +
            $"udp_recv_avg_bytes={udpReceiveAverageBytes:F2} " +
            $"udp_send_avg_bytes={udpSendAverageBytes:F2} " +
            $"tcp_send/s={tcpSent} " +
            $"json_serialize/s={jsonSerializations} " +
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

    private GameSession GetOrCreateGameSession(string roomId, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_gameSessions.TryGetValue(roomId, out var existingSession))
            {
                lock (existingSession.CommandGate)
                {
                    if (!existingSession.IsStopping)
                    {
                        return existingSession;
                    }
                }

                Thread.Yield();
                continue;
            }

            var createdSession = new GameSession();
            if (!_gameSessions.TryAdd(roomId, createdSession))
            {
                continue;
            }

            _ = Task.Run(() => RunRoomTickLoopAsync(roomId, createdSession, stoppingToken), CancellationToken.None);
            return createdSession;
        }

        throw new OperationCanceledException(stoppingToken);
    }

    private static bool TryWriteRoomCommand(GameSession gameSession, RoomCommand command)
    {
        lock (gameSession.CommandGate)
        {
            return !gameSession.IsStopping && gameSession.Commands.Writer.TryWrite(command);
        }
    }

    // 방 하나의 입력 큐를 순서대로 비우고, 같은 루프에서 규칙/상태 tick을 처리합니다.
    private async Task RunRoomTickLoopAsync(
        string roomId,
        GameSession gameSession,
        CancellationToken stoppingToken)
    {
        var lastTickAt = DateTime.UtcNow;
        var udpBroadcastElapsed = TimeSpan.Zero;
        var ruleElapsed = TimeSpan.Zero;
        var stateElapsed = TimeSpan.Zero;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var elapsed = now - lastTickAt;
                lastTickAt = now;
                udpBroadcastElapsed += elapsed;
                ruleElapsed += elapsed;
                stateElapsed += elapsed;

                DrainRoomCommands(roomId, gameSession);

                while (udpBroadcastElapsed >= UdpMovementBroadcastInterval)
                {
                    FlushPendingUdpMovementBroadcasts(gameSession);
                    udpBroadcastElapsed -= UdpMovementBroadcastInterval;
                }

                while (ruleElapsed >= GameRuleTickInterval)
                {
                    ProcessRoomRuleTick(roomId, gameSession);
                    ruleElapsed -= GameRuleTickInterval;
                }

                while (stateElapsed >= GameStateSyncInterval)
                {
                    ProcessRoomStateSync(roomId, gameSession);
                    stateElapsed -= GameStateSyncInterval;
                }

                if (TryStopRoomLoop(roomId, gameSession, now))
                {
                    return;
                }

                await Task.Delay(RoomTickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Server is stopping.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Room tick loop failed for {roomId}: {ex.Message}");
        }
        finally
        {
            if (_gameSessions.TryGetValue(roomId, out var currentSession)
                && ReferenceEquals(currentSession, gameSession))
            {
                _gameSessions.TryRemove(roomId, out _);
            }

            gameSession.Commands.Writer.TryComplete();
        }
    }

    private bool TryStopRoomLoop(string roomId, GameSession gameSession, DateTime now)
    {
        if (!gameSession.HasHadPlayers || gameSession.Sessions.Count > 0)
        {
            gameSession.EmptySinceUtc = null;
            return false;
        }

        gameSession.EmptySinceUtc ??= now;
        if (now - gameSession.EmptySinceUtc < EmptyRoomStopDelay)
        {
            return false;
        }

        lock (gameSession.CommandGate)
        {
            if (gameSession.Sessions.Count > 0 || gameSession.Commands.Reader.TryPeek(out _))
            {
                gameSession.EmptySinceUtc = null;
                return false;
            }

            gameSession.IsStopping = true;
            if (!_gameSessions.TryRemove(roomId, out var removedSession)
                || !ReferenceEquals(removedSession, gameSession))
            {
                return false;
            }

            gameSession.Commands.Writer.TryComplete();
            return true;
        }
    }

    private void DrainRoomCommands(string roomId, GameSession gameSession)
    {
        Dictionary<string, MoveRoomCommand>? latestMoveByPlayerId = null;

        while (gameSession.Commands.Reader.TryRead(out var command))
        {
            switch (command)
            {
                case JoinRoomCommand join:
                    FlushCoalescedMoves(roomId, gameSession, latestMoveByPlayerId);
                    latestMoveByPlayerId?.Clear();
                    HandleRoomJoin(roomId, gameSession, join);
                    break;
                case LeaveRoomCommand leave:
                    FlushCoalescedMoves(roomId, gameSession, latestMoveByPlayerId);
                    latestMoveByPlayerId?.Clear();
                    HandleRoomLeave(roomId, gameSession, leave);
                    break;
                case MoveRoomCommand move:
                    latestMoveByPlayerId ??= new Dictionary<string, MoveRoomCommand>();
                    latestMoveByPlayerId[move.Movement.Id] = move;
                    break;
            }
        }

        FlushCoalescedMoves(roomId, gameSession, latestMoveByPlayerId);
    }

    private void FlushCoalescedMoves(
        string roomId,
        GameSession gameSession,
        Dictionary<string, MoveRoomCommand>? latestMoveByPlayerId)
    {
        if (latestMoveByPlayerId is not { Count: > 0 })
        {
            return;
        }

        foreach (var move in latestMoveByPlayerId.Values)
        {
            HandleRoomMove(roomId, gameSession, move);
        }
    }

    // 짧은 주기로 체포 판정, 체포 완료, 탈옥 진행 같은 실시간 게임 규칙을 갱신합니다.
    private void ProcessRoomRuleTick(string roomId, GameSession gameSession)
    {
        if (gameSession.GamePhase != GamePhase.Playing || gameSession.Sessions.Count == 0)
        {
            ClearJailBreakProgress(gameSession, roomId);
            return;
        }

        CompletePendingArrests(gameSession);
        DetectRobbersForArrest(gameSession);
        UpdateJailBreakProgress(roomId, gameSession);
    }

    // 1초마다 게임 페이즈와 남은 시간을 갱신하고 방 전체에 상태를 동기화합니다.
    private void ProcessRoomStateSync(string roomId, GameSession gameSession)
    {
        _gameRoomService.RemoveExpiredEmptyRooms();

        if (gameSession.Sessions.Count == 0)
        {
            return;
        }

        if (gameSession.GamePhase == GamePhase.Waiting)
        {
            if (IsRoomReadyForCountdown(roomId, gameSession))
            {
                gameSession.GamePhase = GamePhase.Countdown;
                gameSession.CountdownTime = 3;
                gameSession.GameTime = GameDurationSeconds;
                gameSession.WinnerRole = null;
                gameSession.ElapsedGameTime = 0;
            }
        }
        else if (gameSession.GamePhase == GamePhase.Countdown)
        {
            gameSession.CountdownTime--;
            if (gameSession.CountdownTime < 0)
            {
                gameSession.GamePhase = GamePhase.Playing;
                gameSession.CountdownTime = 0;
                gameSession.GameStartedAtUtc = DateTime.UtcNow;
            }
        }
        else if (gameSession.GamePhase == GamePhase.Playing)
        {
            gameSession.GameTime--;

            var robbers = gameSession.Sessions.Values
                .Where(s => s.PlayerState.Role == PlayerRole.Robber)
                .ToList();

            var allRobbersCaught = false;
            if (robbers.Count > 0)
            {
                foreach (var robber in robbers)
                {
                    RefreshJailEntry(gameSession, robber.PlayerState);
                }

                allRobbersCaught = robbers.All(p => IsInJail(p.PlayerState));
            }

            if (gameSession.GameTime <= 0 || allRobbersCaught)
            {
                gameSession.GamePhase = GamePhase.Ended;
                gameSession.GameTime = Math.Max(0, gameSession.GameTime);
                gameSession.WinnerRole = gameSession.GameTime <= 0
                    ? PlayerRole.Robber
                    : PlayerRole.Police;
                gameSession.ElapsedGameTime = gameSession.GameStartedAtUtc.HasValue
                    ? Math.Clamp(
                        (int)Math.Round(
                            (DateTime.UtcNow - gameSession.GameStartedAtUtc.Value).TotalSeconds),
                        0,
                        GameDurationSeconds)
                    : GameDurationSeconds - gameSession.GameTime;
                _gameRoomService.CompleteGame(roomId);
            }
        }

        var syncData = new GameStateSync
        {
            RoomId = roomId,
            Phase = gameSession.GamePhase,
            CountdownTime = gameSession.CountdownTime,
            GameTime = gameSession.GameTime,
            WinnerRole = gameSession.WinnerRole,
            ElapsedGameTime = gameSession.ElapsedGameTime
        };

        BroadcastTcp(gameSession, TcpMessageType.GameState, SerializeForMetrics(syncData), null);
    }

    private bool IsRoomReadyForCountdown(string roomId, GameSession gameSession)
    {
        if (string.Equals(roomId, DefaultRoomId, StringComparison.Ordinal))
        {
            return gameSession.Sessions.Count > 0;
        }

        var roomStatus = _gameRoomService.GetRoomStatus(roomId);
        if (!roomStatus.Success || !roomStatus.Matched)
        {
            return false;
        }

        var expectedPlayerCount = Math.Max(1, roomStatus.CurrentCount);
        return gameSession.Sessions.Count >= expectedPlayerCount;
    }

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
            catch
            {
                // Ignore when cancelling.
            }
        }
    }

    // TCP 클라이언트 하나의 입장 패킷과 연결 종료 정리를 담당합니다.
    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        using var stream = client.GetStream();
        using var reader = new BinaryReader(stream);
        using var writer = new BinaryWriter(stream);
        Interlocked.Increment(ref _currentTcpConnections);

        string? playerId = null;
        string? roomId = null;

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
                    var player = JsonSerializer.Deserialize<Player>(json);
                    if (player == null)
                    {
                        continue;
                    }

                    playerId = player.Id;
                    roomId = NormalizeRoomId(player.RoomId);
                    player.RoomId = roomId;

                    var joinCommand = new JoinRoomCommand(player, client, writer);
                    var gameSession = GetOrCreateGameSession(roomId, stoppingToken);
                    if (!TryWriteRoomCommand(gameSession, joinCommand))
                    {
                        gameSession = GetOrCreateGameSession(roomId, stoppingToken);
                        TryWriteRoomCommand(gameSession, joinCommand);
                    }
                }
            }
        }
        catch
        {
            // Disconnected.
        }
        finally
        {
            if (playerId != null && roomId != null && _gameSessions.TryGetValue(roomId, out var gameSession))
            {
                if (!TryWriteRoomCommand(gameSession, new LeaveRoomCommand(playerId)))
                {
                    _playerRooms.TryRemove(playerId, out _);
                }
            }
            else if (playerId != null)
            {
                _playerRooms.TryRemove(playerId, out _);
            }

            Interlocked.Decrement(ref _currentTcpConnections);
            client.Close();
        }
    }

    private void HandleRoomJoin(string roomId, GameSession gameSession, JoinRoomCommand command)
    {
        var player = command.Player;
        var playerId = player.Id;

        PositionPlayerForRoom(player, gameSession);
        var playerSession = new PlayerSession
        {
            Client = command.Client,
            Writer = command.Writer,
            PlayerState = player
        };
        gameSession.Sessions[playerId] = playerSession;
        gameSession.HasHadPlayers = true;
        gameSession.EmptySinceUtc = null;
        _playerRooms[playerId] = roomId;

        var visiblePlayers = gameSession.Sessions.Values
            .Select(s => s.PlayerState)
            .Where(p => p.Role == player.Role ||
                        IsPlayerVisibleToTeam(gameSession.Sessions.Values, player.Role, p))
            .ToList();

        foreach (var visibleOpponent in visiblePlayers.Where(p => p.Role != player.Role))
        {
            playerSession.VisibleOpponentPlayerIds.Add(visibleOpponent.Id);
        }

        Console.WriteLine($"Player Connected [TCP]: {playerId} / room {roomId}");

        TrySendTcp(command.Writer, TcpMessageType.InitialState, SerializeForMetrics(visiblePlayers));
        Console.WriteLine($"{roomId} 방 {player.Role} 역할 {visiblePlayers.Count}명으로 플레이어 초기화!!");

        var syncData = new GameStateSync
        {
            RoomId = roomId,
            Phase = gameSession.GamePhase,
            CountdownTime = gameSession.CountdownTime,
            GameTime = gameSession.GameTime,
            WinnerRole = gameSession.WinnerRole,
            ElapsedGameTime = gameSession.ElapsedGameTime
        };
        TrySendTcp(command.Writer, TcpMessageType.GameState, SerializeForMetrics(syncData));

        BroadcastTcpToRole(
            gameSession,
            player.Role,
            TcpMessageType.Joined,
            SerializeForMetrics(player),
            playerId);
        RefreshOpponentVisibility(gameSession);
        Console.WriteLine($"{roomId} 방 {player.Role} 역할에 플레이어 입장 브로드캐스트!!");
    }

    private void HandleRoomLeave(string roomId, GameSession gameSession, LeaveRoomCommand command)
    {
        if (!gameSession.Sessions.TryRemove(command.PlayerId, out var removedSession))
        {
            _playerRooms.TryRemove(command.PlayerId, out _);
            return;
        }

        gameSession.JailEntryTimes.TryRemove(command.PlayerId, out _);
        gameSession.JailBreakStartedAtByRescuer.Remove(command.PlayerId);
        gameSession.JailBreakProgressByRescuer.Remove(command.PlayerId);
        gameSession.PendingUdpMovementPlayerIds.Remove(command.PlayerId);
        foreach (var arrest in gameSession.ActiveArrestsByRobberId.Values
                     .Where(a => a.RobberId == command.PlayerId || a.PoliceId == command.PlayerId)
                     .ToList())
        {
            gameSession.ActiveArrestsByRobberId.Remove(arrest.RobberId);
        }

        _playerRooms.TryRemove(command.PlayerId, out _);
        Console.WriteLine($"Player Disconnected: {command.PlayerId} / room {roomId}");

        if (TryAbortRandomGameStart(roomId, gameSession, command.PlayerId))
        {
            return;
        }

        BroadcastTcpToRole(
            gameSession,
            removedSession.PlayerState.Role,
            TcpMessageType.Left,
            command.PlayerId,
            null);

        foreach (var remainingSession in gameSession.Sessions.Values)
        {
            if (remainingSession.VisibleOpponentPlayerIds.Remove(command.PlayerId))
            {
                TrySendTcp(remainingSession.Writer, TcpMessageType.Left, command.PlayerId);
            }
        }
    }

    private bool TryAbortRandomGameStart(string roomId, GameSession gameSession, string leavingPlayerId)
    {
        if (gameSession.GamePhase is not (GamePhase.Waiting or GamePhase.Countdown) ||
            string.Equals(roomId, DefaultRoomId, StringComparison.Ordinal))
        {
            return false;
        }

        var roomStatus = _gameRoomService.GetRoomStatus(roomId);
        if (!roomStatus.Success || roomStatus.IsPrivate || !roomStatus.Matched)
        {
            return false;
        }

        var resetStatus = _gameRoomService.AbortRandomGameStart(roomId, leavingPlayerId);
        gameSession.GamePhase = GamePhase.Rematching;
        gameSession.CountdownTime = 0;
        gameSession.GameStartedAtUtc = null;
        ClearJailBreakProgress(gameSession, roomId);

        var syncData = new GameStateSync
        {
            RoomId = roomId,
            Phase = GamePhase.Rematching,
            CountdownTime = 0,
            GameTime = gameSession.GameTime,
            WinnerRole = null,
            ElapsedGameTime = 0
        };

        BroadcastTcp(gameSession, TcpMessageType.GameState, SerializeForMetrics(syncData), null);
        Console.WriteLine(
            $"Random game start aborted: {roomId}, leaving player {leavingPlayerId}, remaining lobby players {resetStatus.CurrentCount}");
        return true;
    }

    private void HandleRoomMove(string roomId, GameSession gameSession, MoveRoomCommand command)
    {
        var movement = command.Movement;
        if (!gameSession.Sessions.TryGetValue(movement.Id, out var session))
        {
            return;
        }

        if (session.UdpEndPoint == null || !session.UdpEndPoint.Equals(command.RemoteEndPoint))
        {
            session.UdpEndPoint = command.RemoteEndPoint;
            Console.WriteLine($"UDP Endpoint registered for {movement.Id} / room {roomId}: {command.RemoteEndPoint}");
        }

        if (IsPlayerMovementLocked(gameSession, session.PlayerState))
        {
            session.PlayerState.IsMoving = false;
            gameSession.PendingUdpMovementPlayerIds.Remove(movement.Id);
            BroadcastPlayerState(gameSession, session.PlayerState);
            return;
        }

        session.PlayerState.X = movement.X;
        session.PlayerState.Y = movement.Y;
        session.PlayerState.Angle = movement.Angle;
        session.PlayerState.IsMoving = movement.IsMoving;
        RefreshJailEntry(gameSession, session.PlayerState);

        gameSession.PendingUdpMovementPlayerIds.Add(movement.Id);
    }

    private void FlushPendingUdpMovementBroadcasts(GameSession gameSession)
    {
        if (gameSession.PendingUdpMovementPlayerIds.Count == 0)
        {
            return;
        }

        var playerIds = gameSession.PendingUdpMovementPlayerIds.ToList();
        gameSession.PendingUdpMovementPlayerIds.Clear();
        RefreshOpponentVisibility(gameSession);

        foreach (var playerId in playerIds)
        {
            if (!gameSession.Sessions.TryGetValue(playerId, out var session))
            {
                continue;
            }

            var authoritativePlayerJson = SerializeForMetrics(PlayerMovementSync.FromPlayer(session.PlayerState));
            var authoritativeBuffer = System.Text.Encoding.UTF8.GetBytes(authoritativePlayerJson);
            BroadcastUdpToVisiblePlayers(gameSession, session.PlayerState, authoritativeBuffer);
        }
    }

    // 방에 입장한 플레이어를 역할별 시작 위치에 배치합니다.
    private void PositionPlayerForRoom(Player player, GameSession gameSession)
    {
        var policeCount = gameSession.Sessions.Values.Count(s => s.PlayerState.Role == PlayerRole.Police);
        var robberCount = gameSession.Sessions.Values.Count(s => s.PlayerState.Role == PlayerRole.Robber);
        const float gap = 150f;

        if (player.Role == PlayerRole.Police)
        {
            var startX = _map.PoliceStation.Center.X - (gap / 2f);
            player.X = startX + policeCount * gap;
            player.Y = _map.PoliceStation.RightBottom.Y + 200f;
        }
        else
        {
            var startX = _map.Width / 2f - gap * 1.5f;
            player.X = startX + robberCount * gap;
            player.Y = _map.Height / 2f;
        }
    }

    // 완료 시간이 지난 체포를 감옥 이동으로 확정합니다.
    private void CompletePendingArrests(GameSession gameSession)
    {
        var now = DateTime.UtcNow;
        var completedArrests = gameSession.ActiveArrestsByRobberId.Values
            .Where(a => a.CompletesAtUtc <= now)
            .ToList();

        foreach (var arrest in completedArrests)
        {
            if (!gameSession.Sessions.TryGetValue(arrest.RobberId, out var robberSession))
            {
                gameSession.ActiveArrestsByRobberId.Remove(arrest.RobberId);
                continue;
            }

            var robber = robberSession.PlayerState;
            var jailPosition = GetJailHoldingPosition(robber, gameSession);
            robber.X = jailPosition.X;
            robber.Y = jailPosition.Y;
            robber.Angle = 0f;
            robber.IsMoving = false;

            gameSession.JailEntryTimes[robber.Id] = now;
            gameSession.ActiveArrestsByRobberId.Remove(robber.Id);

            BroadcastPlayerState(gameSession, robber);

            if (gameSession.Sessions.TryGetValue(arrest.PoliceId, out var policeSession))
            {
                policeSession.PlayerState.IsMoving = false;
                BroadcastPlayerState(gameSession, policeSession.PlayerState);
            }
        }
    }

    // 경찰 시야 안에 들어온 도둑을 찾아 체포를 시작합니다.
    private void DetectRobbersForArrest(GameSession gameSession)
    {
        var policePlayers = gameSession.Sessions.Values
            .Select(s => s.PlayerState)
            .Where(p => p.Role == PlayerRole.Police && !IsPlayerInActiveArrest(gameSession, p.Id))
            .OrderBy(p => p.Id)
            .ToList();

        var robbers = gameSession.Sessions.Values
            .Select(s => s.PlayerState)
            .Where(p => p.Role == PlayerRole.Robber)
            .OrderBy(p => p.Id)
            .ToList();

        foreach (var police in policePlayers)
        {
            foreach (var robber in robbers)
            {
                if (IsInJail(robber) || gameSession.ActiveArrestsByRobberId.ContainsKey(robber.Id))
                {
                    continue;
                }

                var robberBush = _map.FindBushContainingPoint(robber.X, robber.Y);
                if (robberBush != null && !GameMap.ContainsPoint(robberBush, police.X, police.Y))
                {
                    continue;
                }

                if (IsPointInVision(police, robber.X, robber.Y) &&
                    !IsVisionBlockedByObstacle(police, robber))
                {
                    StartArrest(gameSession, police, robber);
                    break;
                }
            }
        }
    }

    // 경찰과 도둑을 멈추고 일정 시간 뒤 완료될 체포 상태를 등록합니다.
    private void StartArrest(GameSession gameSession, Player police, Player robber)
    {
        var now = DateTime.UtcNow;
        gameSession.ActiveArrestsByRobberId[robber.Id] = new ArrestState
        {
            PoliceId = police.Id,
            RobberId = robber.Id,
            CompletesAtUtc = now.AddSeconds(ArrestDurationSeconds)
        };

        police.IsMoving = false;
        robber.IsMoving = false;

        BroadcastTcp(gameSession, TcpMessageType.Arrested, $"{police.Id},{robber.Id}", null);
        BroadcastPlayerState(gameSession, police);
        BroadcastPlayerState(gameSession, robber);
    }

    // 감옥 안에서 도둑들이 겹치지 않도록 수용 위치를 계산합니다.
    private (float X, float Y) GetJailHoldingPosition(Player robber, GameSession gameSession)
    {
        var robbers = gameSession.Sessions.Values
            .Select(s => s.PlayerState)
            .Where(p => p.Role == PlayerRole.Robber)
            .OrderBy(p => p.Id)
            .ToList();

        var index = robbers.FindIndex(p => p.Id == robber.Id);
        if (index < 0)
        {
            index = 0;
        }

        const int columns = 3;
        const float gap = 150f;
        var rows = Math.Max(1, (int)Math.Ceiling(robbers.Count / (double)columns));
        var column = index % columns;
        var row = index / columns;
        var offsetX = (column - ((columns - 1) / 2f)) * gap;
        var offsetY = (row - ((rows - 1) / 2f)) * gap;
        var minX = _map.Jail.LeftTop.X + robber.Radius;
        var maxX = _map.Jail.RightBottom.X - robber.Radius;
        var minY = _map.Jail.LeftTop.Y + robber.Radius;
        var maxY = _map.Jail.RightBottom.Y - robber.Radius;

        return (
            Math.Clamp(_map.Jail.Center.X + offsetX, minX, maxX),
            Math.Clamp(_map.Jail.Center.Y + offsetY, minY, maxY));
    }

    // 감옥 근처에서 구조 중인 도둑들의 탈옥 진행률을 갱신하고 완료 시 석방합니다.
    private void UpdateJailBreakProgress(string roomId, GameSession gameSession)
    {
        var now = DateTime.UtcNow;
        var jailedRobberCount = gameSession.Sessions.Values
            .Count(s => s.PlayerState.Role == PlayerRole.Robber && IsInJail(s.PlayerState));

        if (jailedRobberCount == 0)
        {
            ClearJailBreakProgress(gameSession, roomId);
            return;
        }

        var activeRescuers = gameSession.Sessions.Values
            .Select(s => s.PlayerState)
            .Where(p => p.Role == PlayerRole.Robber &&
                        !IsInJail(p) &&
                        !IsPlayerInActiveArrest(gameSession, p.Id) &&
                        IsTouchingOrNearJail(p))
            .OrderBy(p => p.Id)
            .Take(jailedRobberCount)
            .ToList();

        if (activeRescuers.Count == 0)
        {
            ClearJailBreakProgress(gameSession, roomId);
            return;
        }

        var activeRescuerIds = activeRescuers.Select(p => p.Id).ToHashSet();
        foreach (var rescuerId in gameSession.JailBreakStartedAtByRescuer.Keys
                     .Where(id => !activeRescuerIds.Contains(id))
                     .ToList())
        {
            gameSession.JailBreakStartedAtByRescuer.Remove(rescuerId);
            gameSession.JailBreakProgressByRescuer.Remove(rescuerId);
        }

        foreach (var rescuer in activeRescuers)
        {
            if (!gameSession.JailBreakStartedAtByRescuer.TryGetValue(rescuer.Id, out var startedAt))
            {
                startedAt = now;
                gameSession.JailBreakStartedAtByRescuer[rescuer.Id] = startedAt;
            }

            var elapsedSeconds = (now - startedAt).TotalSeconds;
            gameSession.JailBreakProgressByRescuer[rescuer.Id] =
                Math.Clamp((float)(elapsedSeconds / JailBreakDurationSeconds), 0f, 1f);
        }

        var readyRescuers = activeRescuers
            .Where(p => gameSession.JailBreakProgressByRescuer.TryGetValue(p.Id, out var progress) && progress >= 1f)
            .ToList();

        if (readyRescuers.Count > 0)
        {
            ReleaseJailedRobbers(roomId, gameSession, readyRescuers, now);
        }

        BroadcastJailBreakProgress(gameSession, roomId);
    }

    // 구조 조건이 깨졌을 때 탈옥 진행 상태를 초기화하고 클라이언트에 알립니다.
    private void ClearJailBreakProgress(GameSession gameSession, string roomId)
    {
        if (gameSession.JailBreakStartedAtByRescuer.Count == 0 &&
            gameSession.JailBreakProgressByRescuer.Count == 0)
        {
            return;
        }

        gameSession.JailBreakStartedAtByRescuer.Clear();
        gameSession.JailBreakProgressByRescuer.Clear();
        BroadcastJailBreakProgress(gameSession, roomId);
    }

    // 탈옥 진행을 완료한 구조자 수만큼 오래 갇힌 도둑을 감옥 밖으로 보냅니다.
    private void ReleaseJailedRobbers(
        string roomId,
        GameSession gameSession,
        List<Player> readyRescuers,
        DateTime now)
    {
        var targetSessions = gameSession.Sessions.Values
            .Where(s => s.PlayerState.Role == PlayerRole.Robber && IsInJail(s.PlayerState))
            .Select(s => new
            {
                Session = s,
                EnteredAt = gameSession.JailEntryTimes.GetOrAdd(s.PlayerState.Id, now)
            })
            .OrderBy(s => s.EnteredAt)
            .ThenBy(s => s.Session.PlayerState.Id)
            .Take(readyRescuers.Count)
            .ToList();

        for (var i = 0; i < targetSessions.Count; i++)
        {
            var target = targetSessions[i].Session.PlayerState;
            var releasePosition = GetJailReleasePosition(target.Radius, i);

            target.X = releasePosition.X;
            target.Y = releasePosition.Y;
            target.Angle = 0f;
            target.IsMoving = false;
            gameSession.JailEntryTimes.TryRemove(target.Id, out _);

            var syncData = new JailBreakSync
            {
                RoomId = roomId,
                RescuerId = readyRescuers[Math.Min(i, readyRescuers.Count - 1)].Id,
                RobberId = target.Id,
                X = target.X,
                Y = target.Y
            };

            BroadcastTcpToRole(
                gameSession,
                PlayerRole.Robber,
                TcpMessageType.JailBreak,
                SerializeForMetrics(syncData),
                null);
            BroadcastPlayerState(gameSession, target);
        }

        foreach (var rescuer in readyRescuers)
        {
            gameSession.JailBreakStartedAtByRescuer.Remove(rescuer.Id);
            gameSession.JailBreakProgressByRescuer.Remove(rescuer.Id);
        }

        var remainingJailedRobbers = gameSession.Sessions.Values
            .Count(s => s.PlayerState.Role == PlayerRole.Robber && IsInJail(s.PlayerState));

        if (remainingJailedRobbers == 0)
        {
            gameSession.JailBreakStartedAtByRescuer.Clear();
            gameSession.JailBreakProgressByRescuer.Clear();
        }
    }

    // 현재 구조자별 탈옥 진행률을 같은 도둑 역할의 TCP 클라이언트에 보냅니다.
    private void BroadcastJailBreakProgress(GameSession gameSession, string roomId)
    {
        var syncData = new JailBreakProgressSync
        {
            RoomId = roomId,
            ProgressByRescuer = new Dictionary<string, float>(gameSession.JailBreakProgressByRescuer)
        };

        BroadcastTcpToRole(
            gameSession,
            PlayerRole.Robber,
            TcpMessageType.JailBreakProgress,
            SerializeForMetrics(syncData),
            null);
    }

    // 도둑의 현재 위치를 기준으로 감옥 입장 시간 기록을 추가하거나 제거합니다.
    private void RefreshJailEntry(GameSession gameSession, Player player)
    {
        if (player.Role != PlayerRole.Robber)
        {
            return;
        }

        if (IsInJail(player))
        {
            gameSession.JailEntryTimes.TryAdd(player.Id, DateTime.UtcNow);
        }
        else
        {
            gameSession.JailEntryTimes.TryRemove(player.Id, out _);
        }
    }

    // 플레이어가 현재 체포 중인 경찰이나 도둑인지 확인합니다.
    private static bool IsPlayerInActiveArrest(GameSession gameSession, string playerId)
    {
        return gameSession.ActiveArrestsByRobberId.ContainsKey(playerId) ||
               gameSession.ActiveArrestsByRobberId.Values.Any(a => a.PoliceId == playerId);
    }

    // 체포 중이거나 감옥에 갇혀 있어 이동을 막아야 하는 플레이어인지 확인합니다.
    private bool IsPlayerMovementLocked(GameSession gameSession, Player player)
    {
        return IsPlayerInActiveArrest(gameSession, player.Id) ||
               (player.Role == PlayerRole.Robber && IsInJail(player));
    }

    // 플레이어의 현재 좌표가 감옥 사각형 안에 있는지 확인합니다.
    private bool IsInJail(Player player)
    {
        return player.X >= _map.Jail.LeftTop.X &&
               player.X <= _map.Jail.RightBottom.X &&
               player.Y >= _map.Jail.LeftTop.Y &&
               player.Y <= _map.Jail.RightBottom.Y;
    }

    // 특정 좌표가 플레이어의 시야 거리와 시야각 안에 들어오는지 계산합니다.
    private static bool IsPointInVision(Player player, float x, float y)
    {
        var dx = x - player.X;
        var dy = y - player.Y;
        var distanceSquared = dx * dx + dy * dy;
        var visionRange = GetVisionRange(player);

        if (distanceSquared > visionRange * visionRange)
        {
            return false;
        }

        var targetAngle = NormalizeDegrees((float)(Math.Atan2(dy, dx) * 180f / Math.PI));
        var facingAngle = GetFacingAngle(player);
        var angleDifference = Math.Abs(ShortestAngleDifference(facingAngle, targetAngle));

        return angleDifference <= VisionConeAngleDegrees / 2f;
    }

    // 경찰과 도둑 사이의 선분을 실제로 가로막는 장애물이 있는지 확인합니다.
    private bool IsVisionBlockedByObstacle(Player police, Player robber)
    {
        foreach (var building in _map.Buildings)
        {
            if (DoesSegmentIntersectRectangle(
                    police.X,
                    police.Y,
                    robber.X,
                    robber.Y,
                    building.LeftTop.X,
                    building.LeftTop.Y,
                    building.RightBottom.X,
                    building.RightBottom.Y))
            {
                return true;
            }
        }

        foreach (var obstacle in _map.Obstacles)
        {
            // 경찰이 들어가 있는 부쉬 자체는 경찰과 도둑 사이의 장애물로 보지 않는다.
            if (GameMap.IsBushObstacle(obstacle) &&
                GameMap.ContainsPoint(obstacle, police.X, police.Y))
            {
                continue;
            }

            if (obstacle.Type == "Rect" &&
                DoesSegmentIntersectRectangle(
                    police.X,
                    police.Y,
                    robber.X,
                    robber.Y,
                    obstacle.LeftTop.X,
                    obstacle.LeftTop.Y,
                    obstacle.RightBottom.X,
                    obstacle.RightBottom.Y))
            {
                return true;
            }

            if (obstacle.Type == "Circle" &&
                DoesSegmentIntersectCircle(
                    police.X,
                    police.Y,
                    robber.X,
                    robber.Y,
                    obstacle.CenterX.X,
                    obstacle.CenterX.Y,
                    obstacle.Radius))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoesSegmentIntersectRectangle(
        float startX,
        float startY,
        float endX,
        float endY,
        float left,
        float top,
        float right,
        float bottom)
    {
        var directionX = endX - startX;
        var directionY = endY - startY;
        var minimum = 0f;
        var maximum = 1f;

        return ClipSegmentToAxis(-directionX, startX - left, ref minimum, ref maximum) &&
               ClipSegmentToAxis(directionX, right - startX, ref minimum, ref maximum) &&
               ClipSegmentToAxis(-directionY, startY - top, ref minimum, ref maximum) &&
               ClipSegmentToAxis(directionY, bottom - startY, ref minimum, ref maximum);
    }

    private static bool ClipSegmentToAxis(
        float direction,
        float distance,
        ref float minimum,
        ref float maximum)
    {
        if (Math.Abs(direction) < float.Epsilon)
        {
            return distance >= 0f;
        }

        var ratio = distance / direction;
        if (direction < 0f)
        {
            if (ratio > maximum)
            {
                return false;
            }

            minimum = Math.Max(minimum, ratio);
        }
        else
        {
            if (ratio < minimum)
            {
                return false;
            }

            maximum = Math.Min(maximum, ratio);
        }

        return true;
    }

    private static bool DoesSegmentIntersectCircle(
        float startX,
        float startY,
        float endX,
        float endY,
        float centerX,
        float centerY,
        float radius)
    {
        var directionX = endX - startX;
        var directionY = endY - startY;
        var segmentLengthSquared = directionX * directionX + directionY * directionY;

        if (segmentLengthSquared <= float.Epsilon)
        {
            return false;
        }

        var projection = ((centerX - startX) * directionX + (centerY - startY) * directionY) /
                         segmentLengthSquared;
        projection = Math.Clamp(projection, 0f, 1f);

        var closestX = startX + projection * directionX;
        var closestY = startY + projection * directionY;
        var distanceX = centerX - closestX;
        var distanceY = centerY - closestY;

        return distanceX * distanceX + distanceY * distanceY <= radius * radius;
    }

    // 플레이어 크기를 기준으로 시야 거리를 계산합니다.
    private static float GetVisionRange(Player player)
    {
        return player.Radius * 2f * VisionRangePlayerSizeMultiplier;
    }

    // 플레이어 회전값을 실제 바라보는 방향 각도로 변환합니다.
    private static float GetFacingAngle(Player player)
    {
        return NormalizeDegrees(player.Angle + 90f);
    }

    // 각도를 0도 이상 360도 미만 범위로 맞춥니다.
    private static float NormalizeDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees < 0)
        {
            degrees += 360f;
        }

        return degrees;
    }

    // 두 각도 사이의 가장 짧은 방향 차이를 -180도부터 180도 범위로 계산합니다.
    private static float ShortestAngleDifference(float fromDegrees, float toDegrees)
    {
        var difference = NormalizeDegrees(toDegrees - fromDegrees);
        return difference > 180f ? difference - 360f : difference;
    }

    // 플레이어가 탈옥 구조를 진행할 만큼 감옥에 가까이 붙어 있는지 확인합니다.
    private bool IsTouchingOrNearJail(Player player)
    {
        var closestX = Math.Max(_map.Jail.LeftTop.X, Math.Min(player.X, _map.Jail.RightBottom.X));
        var closestY = Math.Max(_map.Jail.LeftTop.Y, Math.Min(player.Y, _map.Jail.RightBottom.Y));
        var distanceX = player.X - closestX;
        var distanceY = player.Y - closestY;
        var allowedDistance = player.Radius + JailBreakContactTolerance;

        return distanceX * distanceX + distanceY * distanceY <= allowedDistance * allowedDistance;
    }

    // 감옥 아래쪽에서 장애물과 겹치지 않는 석방 위치를 찾습니다.
    private (float X, float Y) GetJailReleasePosition(float radius, int releaseIndex)
    {
        var jail = _map.Jail;
        var startY = jail.RightBottom.Y + radius + JailBreakReleaseOffset;
        var candidates = new List<(float X, float Y)>();
        float[][] rowOffsets =
        {
            new[] { 0f, -jail.Width / 4f, jail.Width / 4f, -jail.Width / 2f + radius, jail.Width / 2f - radius },
            new[] { -jail.Width / 6f, jail.Width / 6f, -jail.Width / 3f, jail.Width / 3f, 0f }
        };

        for (var row = 0; row < 5; row++)
        {
            var y = Math.Clamp(startY + row * radius * 1.5f, radius, _map.Height - radius);
            var offsets = rowOffsets[row % rowOffsets.Length];

            foreach (var offset in offsets)
            {
                var x = Math.Clamp(jail.Center.X + offset, radius, _map.Width - radius);
                if (!IsReleasePositionBlocked(x, y, radius))
                {
                    candidates.Add((x, y));
                }
            }
        }

        if (candidates.Count > 0)
        {
            return candidates[Math.Min(releaseIndex, candidates.Count - 1)];
        }

        return (Math.Clamp(jail.Center.X, radius, _map.Width - radius), Math.Clamp(startY, radius, _map.Height - radius));
    }

    // 석방 후보 위치가 감옥이나 장애물과 충돌하는지 확인합니다.
    private bool IsReleasePositionBlocked(float x, float y, float radius)
    {
        if (IsCircleCollidingWithBuilding(x, y, radius, _map.Jail))
        {
            return true;
        }

        foreach (var obs in _map.Obstacles)
        {
            if (obs.Type == "Rect")
            {
                var closestX = Math.Max(obs.LeftTop.X, Math.Min(x, obs.RightBottom.X));
                var closestY = Math.Max(obs.LeftTop.Y, Math.Min(y, obs.RightBottom.Y));
                var distanceX = x - closestX;
                var distanceY = y - closestY;

                if (distanceX * distanceX + distanceY * distanceY < radius * radius)
                {
                    return true;
                }
            }
            else if (obs.Type == "Circle")
            {
                var dx = x - obs.CenterX.X;
                var dy = y - obs.CenterX.Y;
                var radiusSum = radius + obs.Radius;

                if (dx * dx + dy * dy < radiusSum * radiusSum)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // 원형 플레이어가 사각형 건물 영역과 충돌하는지 계산합니다.
    private static bool IsCircleCollidingWithBuilding(float x, float y, float radius, MapBuilding building)
    {
        var closestX = Math.Max(building.LeftTop.X, Math.Min(x, building.RightBottom.X));
        var closestY = Math.Max(building.LeftTop.Y, Math.Min(y, building.RightBottom.Y));
        var distanceX = x - closestX;
        var distanceY = y - closestY;

        return distanceX * distanceX + distanceY * distanceY < radius * radius;
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
        catch
        {
            // Dead TCP connections are cleaned up by their read loop.
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

    // UDP 이동 패킷을 받아 서버의 플레이어 상태에 반영하고 같은 역할에만 전파합니다.
    private async Task ReceiveUdpAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(stoppingToken);
                Interlocked.Increment(ref _udpPacketsReceivedThisSecond);
                Interlocked.Add(ref _udpBytesReceivedThisSecond, result.Buffer.Length);
                var movement = JsonSerializer.Deserialize<PlayerMovementSync>(result.Buffer);
                if (movement == null || string.IsNullOrWhiteSpace(movement.Id))
                {
                    var player = JsonSerializer.Deserialize<Player>(result.Buffer);
                    movement = player == null ? null : PlayerMovementSync.FromPlayer(player);
                }

                if (movement == null || !_playerRooms.TryGetValue(movement.Id, out var roomId))
                {
                    continue;
                }

                if (!_gameSessions.TryGetValue(roomId, out var gameSession))
                {
                    continue;
                }

                TryWriteRoomCommand(gameSession, new MoveRoomCommand(movement, result.RemoteEndPoint));
            }
            catch
            {
                // Ignore malformed or late UDP packets.
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
            if (kvp.Key == movingPlayer.Id)
            {
                continue;
            }

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

public class GameSession
{
    public object CommandGate { get; } = new();
    public Channel<RoomCommand> Commands { get; } = Channel.CreateUnbounded<RoomCommand>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    public ConcurrentDictionary<string, PlayerSession> Sessions { get; } = new();
    public ConcurrentDictionary<string, DateTime> JailEntryTimes { get; } = new(); // 감옥 입장 순서
    public Dictionary<string, ArrestState> ActiveArrestsByRobberId { get; } = new(); // 현재 체포 중인 도둑 관리
    public Dictionary<string, DateTime> JailBreakStartedAtByRescuer { get; } = new(); // 탈옥 시작 시간
    public Dictionary<string, float> JailBreakProgressByRescuer { get; } = new(); // 탈옥 진행률 관리한느 필드
    public HashSet<string> PendingUdpMovementPlayerIds { get; } = new();
    public GamePhase GamePhase { get; set; }
    public int CountdownTime { get; set; } = 3;
    public int GameTime { get; set; } = 300;
    public PlayerRole? WinnerRole { get; set; }
    public int ElapsedGameTime { get; set; }
    public DateTime? GameStartedAtUtc { get; set; }
    public bool HasHadPlayers { get; set; }
    public DateTime? EmptySinceUtc { get; set; }
    public bool IsStopping { get; set; }
}

public abstract record RoomCommand;

public sealed record JoinRoomCommand(
    Player Player,
    TcpClient Client,
    BinaryWriter Writer) : RoomCommand;

public sealed record LeaveRoomCommand(string PlayerId) : RoomCommand;

public sealed record MoveRoomCommand(PlayerMovementSync Movement, IPEndPoint RemoteEndPoint) : RoomCommand;

public class ArrestState
{
    public string PoliceId { get; set; } = string.Empty;
    public string RobberId { get; set; } = string.Empty;
    public DateTime CompletesAtUtc { get; set; } // 체포가 완료되는 시간
}

public class PlayerSession
{
    public TcpClient Client { get; set; } = null!;
    public BinaryWriter Writer { get; set; } = null!;
    public Player PlayerState { get; set; } = null!;
    public IPEndPoint? UdpEndPoint { get; set; }
    public HashSet<string> VisibleOpponentPlayerIds { get; } = new();
}

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
