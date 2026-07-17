using System.Collections.Concurrent;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Hosting;
using polrob.Shared;

namespace polrob.Server.Network;

public partial class GameNetworkServer : BackgroundService
{
    private readonly TcpListener _tcpListener;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<string, GameSession> _gameSessions = new();
    private readonly ConcurrentDictionary<string, string> _playerRooms = new();
    private readonly ConcurrentDictionary<string, UdpRateLimitState> _udpRateLimits = new();
    private readonly GameRoomService _gameRoomService;
    private readonly ILogger<GameNetworkServer> _logger;
    private readonly GameMap _map = new();
    private readonly RuntimeMetricSampler _runtimeMetrics = new();
    private readonly int _roomCommandQueueCapacity;
    private readonly double _udpPacketsPerSecond;
    private readonly double _udpBurstSize;

    private Timer? _metricsTimer;
    private long _udpPacketsReceivedThisSecond;
    private long _udpPacketsSentThisSecond;
    private long _udpBytesReceivedThisSecond;
    private long _udpBytesSentThisSecond;
    private long _udpPacketsRateLimitedThisSecond;
    private long _udpPacketsInvalidThisSecond;
    private long _udpPacketsDuplicateOrLateThisSecond;
    private long _roomCommandsDroppedThisSecond;
    private long _tcpSendFailuresThisSecond;
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
    private const float ServerPlayerSpeed = 7f;
    private const float ServerPlayerRadius = 50f;
    private const float MovementUnitsPerSecondMultiplier = 60f;
    private static readonly TimeSpan MovementInputTimeout = TimeSpan.FromMilliseconds(250);
    private const int TcpListenBacklog = 2048;

    // TCP/UDP 소켓과 방 서비스를 준비합니다.
    public GameNetworkServer(
        GameRoomService gameRoomService,
        IConfiguration configuration,
        ILogger<GameNetworkServer> logger)
    {
        _gameRoomService = gameRoomService;
        _logger = logger;
        _roomCommandQueueCapacity = Math.Max(
            256,
            configuration.GetValue("GameNetwork:RoomCommandQueueCapacity", 4096));
        _udpPacketsPerSecond = Math.Max(
            1d,
            configuration.GetValue("GameNetwork:UdpPacketsPerSecond", 30d));
        _udpBurstSize = Math.Max(
            1d,
            configuration.GetValue("GameNetwork:UdpBurstSize", 20d));
        // 7777 for reliable TCP (Join, Leave, InitialState)
        _tcpListener = new TcpListener(IPAddress.Any, 7777);
        // 7778 for fast UDP (Movement)
        _udpClient = new UdpClient(7778);
    }

    // 백그라운드 서비스가 시작될 때 TCP/UDP 수신 루프와 메트릭 타이머를 켭니다.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _tcpListener.Start(TcpListenBacklog);
        _logger.LogInformation(
            "Game network server started. tcp=7777 udp=7778 room_queue_capacity={RoomCommandQueueCapacity} udp_rate={UdpPacketsPerSecond}/s burst={UdpBurstSize}",
            _roomCommandQueueCapacity,
            _udpPacketsPerSecond,
            _udpBurstSize);

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

    private void HandleRoomJoin(string roomId, GameSession gameSession, JoinRoomCommand command)
    {
        var player = command.Player;
        var playerId = player.Id;

        // 이동에 영향을 주는 값은 Join payload를 신뢰하지 않고 서버가 고정합니다.
        player.Speed = ServerPlayerSpeed;
        player.Radius = ServerPlayerRadius;
        player.Angle = 0f;
        player.IsMoving = false;

        PositionPlayerForRoom(player, gameSession);

        var playerSession = new PlayerSession
        {
            Client = command.Client,
            Writer = command.Writer,
            PlayerState = player,
            MovementSessionToken = Guid.NewGuid().ToString("N")
        };
        gameSession.Sessions[playerId] = playerSession;
        gameSession.HasHadPlayers = true;
        gameSession.EmptySinceUtc = null;
        _playerRooms[playerId] = roomId;
        _udpRateLimits.TryRemove(playerId, out _);

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

        TrySendTcp(command.Writer, TcpMessageType.MovementSession, playerSession.MovementSessionToken);
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
        _udpRateLimits.TryRemove(command.PlayerId, out _);
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
        var input = command.Input;
        if (!gameSession.Sessions.TryGetValue(input.Id, out var session))
        {
            return;
        }

        if (!string.Equals(input.Token, session.MovementSessionToken, StringComparison.Ordinal))
        {
            return;
        }

        if (session.UdpEndPoint == null)
        {
            session.UdpEndPoint = command.RemoteEndPoint;
            Console.WriteLine($"UDP Endpoint registered for {input.Id} / room {roomId}: {command.RemoteEndPoint}");
        }
        else if (!session.UdpEndPoint.Equals(command.RemoteEndPoint))
        {
            return;
        }

        if (!float.IsFinite(input.X) || !float.IsFinite(input.Y))
        {
            Interlocked.Increment(ref _udpPacketsInvalidThisSecond);
            return;
        }

        if (input.Sequence <= session.LastMovementInputSequence)
        {
            Interlocked.Increment(ref _udpPacketsDuplicateOrLateThisSecond);
            return;
        }

        var length = MathF.Sqrt((input.X * input.X) + (input.Y * input.Y));
        session.InputX = length > 1f ? input.X / length : input.X;
        session.InputY = length > 1f ? input.Y / length : input.Y;
        session.LastMovementInputSequence = input.Sequence;
        session.LastMovementInputAtUtc = DateTime.UtcNow;
    }

    // 마지막으로 받은 조이스틱 입력을 사용해 좌표, 속도, 각도, 충돌을 서버가 계산합니다.
    private void SimulateAuthoritativeMovement(GameSession gameSession, TimeSpan elapsed, DateTime now)
    {
        var deltaSeconds = Math.Clamp((float)elapsed.TotalSeconds, 0f, 0.1f);

        foreach (var session in gameSession.Sessions.Values)
        {
            var player = session.PlayerState;
            var wasMoving = player.IsMoving;
            var inputExpired = now - session.LastMovementInputAtUtc > MovementInputTimeout;

            if (gameSession.GamePhase != GamePhase.Playing ||
                IsPlayerMovementLocked(gameSession, player) || inputExpired)
            {
                session.InputX = 0f;
                session.InputY = 0f;
            }

            var hasInput = MathF.Abs(session.InputX) > 0.001f || MathF.Abs(session.InputY) > 0.001f;
            if (!hasInput)
            {
                player.IsMoving = false;
                if (wasMoving)
                {
                    gameSession.PendingUdpMovementPlayerIds.Add(player.Id);
                }
                continue;
            }

            var distance = player.Speed * MovementUnitsPerSecondMultiplier * deltaSeconds;
            var nextX = Math.Clamp(player.X + session.InputX * distance, player.Radius, _map.Width - player.Radius);
            var nextY = Math.Clamp(player.Y + session.InputY * distance, player.Radius, _map.Height - player.Radius);
            var moved = false;

            if (!IsMovementPositionBlocked(nextX, player.Y, player.Radius, session.NearbyCollisionObstacles))
            {
                moved |= MathF.Abs(nextX - player.X) > 0.001f;
                player.X = nextX;
            }

            if (!IsMovementPositionBlocked(player.X, nextY, player.Radius, session.NearbyCollisionObstacles))
            {
                moved |= MathF.Abs(nextY - player.Y) > 0.001f;
                player.Y = nextY;
            }

            player.Angle = MathF.Atan2(session.InputY, session.InputX) * 180f / MathF.PI - 90f;
            player.IsMoving = moved;
            RefreshJailEntry(gameSession, player);
            gameSession.PendingUdpMovementPlayerIds.Add(player.Id);
        }
    }

    private bool IsMovementPositionBlocked(float x, float y, float radius, List<Obstacle> nearbyObstacles)
    {
        return _map.IsMovementPositionBlocked(x, y, radius, nearbyObstacles);
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
            // Keep the role spawn on the front road, below the canonical
            // police-car collider copied from the concept map.
            player.Y = _map.PoliceStation.RightBottom.Y + 350f;
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
        var jailBounds = GameMap.GetBuildingCollisionBounds(_map.Jail);
        var minX = jailBounds.Left + robber.Radius;
        var maxX = jailBounds.Right - robber.Radius;
        var minY = jailBounds.Top + robber.Radius;
        var maxY = jailBounds.Bottom - robber.Radius;

        return (
            Math.Clamp(_map.Jail.CollisionCenter.X + offsetX, minX, maxX),
            Math.Clamp(_map.Jail.CollisionCenter.Y + offsetY, minY, maxY));
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
        return GameMap.IsPointInBuilding(player.X, player.Y, _map.Jail);
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
            if (building.BlocksVision &&
                DoesSegmentIntersectBuilding(
                    police.X,
                    police.Y,
                    robber.X,
                    robber.Y,
                    building))
            {
                return true;
            }
        }

        foreach (var obstacle in _map.Obstacles)
        {
            if (!obstacle.BlocksVision)
            {
                continue;
            }

            // 경찰이 들어가 있는 부쉬 자체는 경찰과 도둑 사이의 장애물로 보지 않는다.
            if (GameMap.IsBushObstacle(obstacle) &&
                GameMap.ContainsPoint(obstacle, police.X, police.Y))
            {
                continue;
            }

            if (obstacle.Type == "Polygon" &&
                DoesSegmentIntersectPolygon(
                    police.X,
                    police.Y,
                    robber.X,
                    robber.Y,
                    obstacle.PolygonPoints))
            {
                return true;
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

    private static bool DoesSegmentIntersectBuilding(
        float startX,
        float startY,
        float endX,
        float endY,
        MapBuilding building)
    {
        if (building.CollisionPolygon.Length >= 3)
        {
            return DoesSegmentIntersectPolygon(
                startX,
                startY,
                endX,
                endY,
                building.CollisionPolygon);
        }

        var localStart = GameMap.ToBuildingCollisionLocalPoint(startX, startY, building);
        var localEnd = GameMap.ToBuildingCollisionLocalPoint(endX, endY, building);
        var halfWidth = building.EffectiveCollisionWidth / 2f;
        var halfHeight = building.EffectiveCollisionHeight / 2f;

        return DoesSegmentIntersectRectangle(
            localStart.X,
            localStart.Y,
            localEnd.X,
            localEnd.Y,
            -halfWidth,
            -halfHeight,
            halfWidth,
            halfHeight);
    }

    private static bool DoesSegmentIntersectPolygon(
        float startX,
        float startY,
        float endX,
        float endY,
        IReadOnlyList<PointF> polygon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }

        if (IsPointInsidePolygon(startX, startY, polygon) ||
            IsPointInsidePolygon(endX, endY, polygon))
        {
            return true;
        }

        for (var index = 0; index < polygon.Count; index++)
        {
            var edgeStart = polygon[index];
            var edgeEnd = polygon[(index + 1) % polygon.Count];
            if (DoSegmentsIntersect(
                    startX,
                    startY,
                    endX,
                    endY,
                    edgeStart.X,
                    edgeStart.Y,
                    edgeEnd.X,
                    edgeEnd.Y))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInsidePolygon(
        float pointX,
        float pointY,
        IReadOnlyList<PointF> polygon)
    {
        var isInside = false;

        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var previous = polygon[(index + polygon.Count - 1) % polygon.Count];

            if (IsPointOnSegment(
                    pointX,
                    pointY,
                    previous.X,
                    previous.Y,
                    current.X,
                    current.Y))
            {
                return true;
            }

            if ((current.Y > pointY) != (previous.Y > pointY) &&
                pointX < (previous.X - current.X) * (pointY - current.Y) /
                         (previous.Y - current.Y) + current.X)
            {
                isInside = !isInside;
            }
        }

        return isInside;
    }

    private static bool DoSegmentsIntersect(
        float firstStartX,
        float firstStartY,
        float firstEndX,
        float firstEndY,
        float secondStartX,
        float secondStartY,
        float secondEndX,
        float secondEndY)
    {
        var firstStartSide = CrossProduct(
            firstStartX,
            firstStartY,
            firstEndX,
            firstEndY,
            secondStartX,
            secondStartY);
        var firstEndSide = CrossProduct(
            firstStartX,
            firstStartY,
            firstEndX,
            firstEndY,
            secondEndX,
            secondEndY);
        var secondStartSide = CrossProduct(
            secondStartX,
            secondStartY,
            secondEndX,
            secondEndY,
            firstStartX,
            firstStartY);
        var secondEndSide = CrossProduct(
            secondStartX,
            secondStartY,
            secondEndX,
            secondEndY,
            firstEndX,
            firstEndY);

        if (firstStartSide * firstEndSide < 0f &&
            secondStartSide * secondEndSide < 0f)
        {
            return true;
        }

        return Math.Abs(firstStartSide) <= 0.001f &&
                   IsPointOnSegment(secondStartX, secondStartY, firstStartX, firstStartY, firstEndX, firstEndY) ||
               Math.Abs(firstEndSide) <= 0.001f &&
                   IsPointOnSegment(secondEndX, secondEndY, firstStartX, firstStartY, firstEndX, firstEndY) ||
               Math.Abs(secondStartSide) <= 0.001f &&
                   IsPointOnSegment(firstStartX, firstStartY, secondStartX, secondStartY, secondEndX, secondEndY) ||
               Math.Abs(secondEndSide) <= 0.001f &&
                   IsPointOnSegment(firstEndX, firstEndY, secondStartX, secondStartY, secondEndX, secondEndY);
    }

    private static float CrossProduct(
        float startX,
        float startY,
        float endX,
        float endY,
        float pointX,
        float pointY)
    {
        return (endX - startX) * (pointY - startY) -
               (endY - startY) * (pointX - startX);
    }

    private static bool IsPointOnSegment(
        float pointX,
        float pointY,
        float startX,
        float startY,
        float endX,
        float endY)
    {
        if (Math.Abs(CrossProduct(startX, startY, endX, endY, pointX, pointY)) > 0.001f)
        {
            return false;
        }

        return pointX >= Math.Min(startX, endX) - 0.001f &&
               pointX <= Math.Max(startX, endX) + 0.001f &&
               pointY >= Math.Min(startY, endY) - 0.001f &&
               pointY <= Math.Max(startY, endY) + 0.001f;
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
        var allowedDistance = player.Radius + JailBreakContactTolerance;

        return GameMap.GetDistanceSquaredToBuilding(player.X, player.Y, _map.Jail) <=
               allowedDistance * allowedDistance;
    }

    // 감옥 아래쪽에서 장애물과 겹치지 않는 석방 위치를 찾습니다.
    private (float X, float Y) GetJailReleasePosition(float radius, int releaseIndex)
    {
        var jail = _map.Jail;
        var jailBounds = GameMap.GetBuildingCollisionBounds(jail);
        var startY = jailBounds.Bottom + radius + JailBreakReleaseOffset;
        var candidates = new List<(float X, float Y)>();
        float[][] rowOffsets =
        {
            new[] { 0f, -jail.EffectiveCollisionWidth / 4f, jail.EffectiveCollisionWidth / 4f, -jail.EffectiveCollisionWidth / 2f + radius, jail.EffectiveCollisionWidth / 2f - radius },
            new[] { -jail.EffectiveCollisionWidth / 6f, jail.EffectiveCollisionWidth / 6f, -jail.EffectiveCollisionWidth / 3f, jail.EffectiveCollisionWidth / 3f, 0f }
        };

        for (var row = 0; row < 5; row++)
        {
            var y = Math.Clamp(startY + row * radius * 1.5f, radius, _map.Height - radius);
            var offsets = rowOffsets[row % rowOffsets.Length];

            foreach (var offset in offsets)
            {
                var x = Math.Clamp(jail.CollisionCenter.X + offset, radius, _map.Width - radius);
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

        return (Math.Clamp(jail.CollisionCenter.X, radius, _map.Width - radius), Math.Clamp(startY, radius, _map.Height - radius));
    }

    // 석방 후보 위치가 감옥이나 장애물과 충돌하는지 확인합니다.
    private bool IsReleasePositionBlocked(float x, float y, float radius)
    {
        return _map.IsMovementPositionBlocked(x, y, radius, new List<Obstacle>());
    }

}
