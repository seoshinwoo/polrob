using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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

    private Timer? _stateTimer;
    private Timer? _ruleTimer;
    private const string DefaultRoomId = "default";
    private const float VisionRangePlayerSizeMultiplier = 2.5f;
    private const float VisionConeAngleDegrees = 90f;
    private const double ArrestDurationSeconds = 2d;
    private const double JailBreakDurationSeconds = 3d;
    private const float JailBreakReleaseOffset = 20f;
    private const float JailBreakContactTolerance = 90f;

    // TCP/UDP 소켓과 방 서비스를 준비합니다.
    public GameNetworkServer(GameRoomService gameRoomService)
    {
        _gameRoomService = gameRoomService;
        // 7777 for reliable TCP (Join, Leave, InitialState)
        _tcpListener = new TcpListener(IPAddress.Any, 7777);
        // 7778 for fast UDP (Movement)
        _udpClient = new UdpClient(7778);
    }

    // 백그라운드 서비스가 시작될 때 TCP/UDP 수신 루프와 게임 타이머를 켭니다.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _tcpListener.Start();
        Console.WriteLine("TCP Server started on port 7777");
        Console.WriteLine("UDP Server started on port 7778");

        _stateTimer = new Timer(GameStateSyncCallback, null, 1000, 1000);
        _ruleTimer = new Timer(GameRuleTickCallback, null, 100, 100);

        _ = Task.Run(() => AcceptTcpClientsAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => ReceiveUdpAsync(stoppingToken), stoppingToken);

        await Task.CompletedTask;
    }

    // 서버가 종료될 때 주기적으로 돌던 타이머들을 정리합니다.
    public override void Dispose()
    {
        _stateTimer?.Dispose();
        _ruleTimer?.Dispose();
        base.Dispose();
    }

    // 짧은 주기로 체포 판정, 체포 완료, 탈옥 진행 같은 실시간 게임 규칙을 갱신합니다.
    private void GameRuleTickCallback(object? state)
    {
        foreach (var sessionEntry in _gameSessions.ToArray())
        {
            var roomId = sessionEntry.Key;
            var gameSession = sessionEntry.Value;

            lock (gameSession.SyncRoot)
            {
                if (gameSession.GamePhase != GamePhase.Playing || gameSession.Sessions.Count == 0)
                {
                    ClearJailBreakProgress(gameSession, roomId);
                    continue;
                }

                CompletePendingArrests(gameSession);
                DetectRobbersForArrest(gameSession);
                UpdateJailBreakProgress(roomId, gameSession);
            }
        }
    }

    // 1초마다 게임 페이즈와 남은 시간을 갱신하고 방 전체에 상태를 동기화합니다.
    private void GameStateSyncCallback(object? state)
    {
        _gameRoomService.RemoveExpiredEmptyRooms();

        foreach (var sessionEntry in _gameSessions.ToArray())
        {
            var roomId = sessionEntry.Key;
            var gameSession = sessionEntry.Value;

            lock (gameSession.SyncRoot)
            {
                if (gameSession.Sessions.Count == 0)
                {
                    _gameSessions.TryRemove(roomId, out _);
                    continue;
                }

                if (gameSession.GamePhase == GamePhase.Waiting)
                {
                    gameSession.GamePhase = GamePhase.Countdown;
                    gameSession.CountdownTime = 3;
                    gameSession.GameTime = 300;
                }
                else if (gameSession.GamePhase == GamePhase.Countdown)
                {
                    gameSession.CountdownTime--;
                    if (gameSession.CountdownTime < 0)
                    {
                        gameSession.GamePhase = GamePhase.Playing;
                        gameSession.CountdownTime = 0;
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
                        gameSession.GameTime = 0;
                        _gameRoomService.CompleteGame(roomId);
                    }
                }

                var syncData = new GameStateSync
                {
                    RoomId = roomId,
                    Phase = gameSession.GamePhase,
                    CountdownTime = gameSession.CountdownTime,
                    GameTime = gameSession.GameTime
                };

                BroadcastTcp(gameSession, TcpMessageType.GameState, JsonSerializer.Serialize(syncData), null);
            }
        }
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

                    var gameSession = _gameSessions.GetOrAdd(roomId, _ => new GameSession());
                    lock (gameSession.SyncRoot)
                    {
                        PositionPlayerForRoom(player, gameSession);
                        gameSession.Sessions[playerId] = new PlayerSession
                        {
                            Client = client,
                            Writer = writer,
                            PlayerState = player
                        };
                        _playerRooms[playerId] = roomId;
                    }

                    Console.WriteLine($"Player Connected [TCP]: {playerId} / room {roomId}");

                    var allPlayers = gameSession.Sessions.Values.Select(s => s.PlayerState).ToList();
                    SendTcp(writer, TcpMessageType.InitialState, JsonSerializer.Serialize(allPlayers));
                    Console.WriteLine($"{roomId} 방 {allPlayers.Count}명에게 플레이어 초기화!!");

                    var syncData = new GameStateSync
                    {
                        RoomId = roomId,
                        Phase = gameSession.GamePhase,
                        CountdownTime = gameSession.CountdownTime,
                        GameTime = gameSession.GameTime
                    };
                    SendTcp(writer, TcpMessageType.GameState, JsonSerializer.Serialize(syncData));

                    BroadcastTcp(gameSession, TcpMessageType.Joined, JsonSerializer.Serialize(player), playerId);
                    Console.WriteLine($"{roomId} 방에 플레이어 입장 브로드캐스트!!");
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
                lock (gameSession.SyncRoot)
                {
                    if (gameSession.Sessions.TryRemove(playerId, out _))
                    {
                        gameSession.JailEntryTimes.TryRemove(playerId, out _);
                        gameSession.JailBreakStartedAtByRescuer.Remove(playerId);
                        gameSession.JailBreakProgressByRescuer.Remove(playerId);
                        foreach (var arrest in gameSession.ActiveArrestsByRobberId.Values
                                     .Where(a => a.RobberId == playerId || a.PoliceId == playerId)
                                     .ToList())
                        {
                            gameSession.ActiveArrestsByRobberId.Remove(arrest.RobberId);
                        }

                        _playerRooms.TryRemove(playerId, out _);
                        Console.WriteLine($"Player Disconnected: {playerId} / room {roomId}");
                        BroadcastTcp(gameSession, TcpMessageType.Left, playerId, null);
                    }

                    if (gameSession.Sessions.Count == 0)
                    {
                        _gameSessions.TryRemove(roomId, out _);
                    }
                }
            }

            client.Close();
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

            BroadcastTcp(gameSession, TcpMessageType.PlayerState, JsonSerializer.Serialize(robber), null);

            if (gameSession.Sessions.TryGetValue(arrest.PoliceId, out var policeSession))
            {
                policeSession.PlayerState.IsMoving = false;
                BroadcastTcp(gameSession, TcpMessageType.PlayerState, JsonSerializer.Serialize(policeSession.PlayerState), null);
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

                if (IsPointInVision(police, robber.X, robber.Y))
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
        BroadcastTcp(gameSession, TcpMessageType.PlayerState, JsonSerializer.Serialize(police), null);
        BroadcastTcp(gameSession, TcpMessageType.PlayerState, JsonSerializer.Serialize(robber), null);
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
                        p.IsMoving &&
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

            BroadcastTcp(gameSession, TcpMessageType.JailBreak, JsonSerializer.Serialize(syncData), null);
            BroadcastTcp(gameSession, TcpMessageType.PlayerState, JsonSerializer.Serialize(target), null);
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

    // 현재 구조자별 탈옥 진행률을 방의 모든 TCP 클라이언트에 보냅니다.
    private static void BroadcastJailBreakProgress(GameSession gameSession, string roomId)
    {
        var syncData = new JailBreakProgressSync
        {
            RoomId = roomId,
            ProgressByRescuer = new Dictionary<string, float>(gameSession.JailBreakProgressByRescuer)
        };

        BroadcastTcp(gameSession, TcpMessageType.JailBreakProgress, JsonSerializer.Serialize(syncData), null);
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
    private static void SendTcp(BinaryWriter writer, TcpMessageType type, string payload)
    {
        lock (writer)
        {
            writer.Write(payload.Length + 1);
            writer.Write((byte)type);
            writer.Write(payload);
        }
    }

    // 한 방의 TCP 클라이언트들에게 메시지를 보내고 필요하면 특정 플레이어는 제외합니다.
    private static void BroadcastTcp(GameSession gameSession, TcpMessageType type, string payload, string? excludeId)
    {
        foreach (var kvp in gameSession.Sessions)
        {
            if (kvp.Key == excludeId)
            {
                continue;
            }

            try
            {
                SendTcp(kvp.Value.Writer, type, payload);
            }
            catch
            {
                // Dead TCP connections are cleaned up by their read loop.
            }
        }
    }

    // UDP 이동 패킷을 받아 서버의 플레이어 상태에 반영하고 같은 방에 전파합니다.
    private async Task ReceiveUdpAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(stoppingToken);
                var json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                var player = JsonSerializer.Deserialize<Player>(json);

                if (player == null || !_playerRooms.TryGetValue(player.Id, out var roomId))
                {
                    continue;
                }

                if (!_gameSessions.TryGetValue(roomId, out var gameSession)
                    || !gameSession.Sessions.TryGetValue(player.Id, out var session))
                {
                    continue;
                }

                string authoritativePlayerJson;
                var shouldBroadcastUdp = false;
                var shouldBroadcastTcpState = false;
                lock (gameSession.SyncRoot)
                {
                    if (IsPlayerMovementLocked(gameSession, session.PlayerState))
                    {
                        session.PlayerState.IsMoving = false;
                        authoritativePlayerJson = JsonSerializer.Serialize(session.PlayerState);
                        shouldBroadcastTcpState = true;
                    }
                    else
                    {
                        session.PlayerState.X = player.X;
                        session.PlayerState.Y = player.Y;
                        session.PlayerState.Angle = player.Angle;
                        session.PlayerState.IsMoving = player.IsMoving;
                        RefreshJailEntry(gameSession, session.PlayerState);
                        authoritativePlayerJson = JsonSerializer.Serialize(session.PlayerState);
                        shouldBroadcastUdp = true;
                    }
                }

                if (session.UdpEndPoint == null || !session.UdpEndPoint.Equals(result.RemoteEndPoint))
                {
                    session.UdpEndPoint = result.RemoteEndPoint;
                    Console.WriteLine($"UDP Endpoint registered for {player.Id} / room {roomId}: {result.RemoteEndPoint}");
                }

                if (shouldBroadcastTcpState)
                {
                    BroadcastTcp(gameSession, TcpMessageType.PlayerState, authoritativePlayerJson, null);
                }
                else if (shouldBroadcastUdp)
                {
                    var authoritativeBuffer = System.Text.Encoding.UTF8.GetBytes(authoritativePlayerJson);
                    BroadcastUdp(gameSession, authoritativeBuffer, player.Id);
                }
            }
            catch
            {
                // Ignore malformed or late UDP packets.
            }
        }
    }

    // 한 방의 등록된 UDP endpoint들에게 이동 데이터를 보내고 보낸 플레이어는 제외합니다.
    private void BroadcastUdp(GameSession gameSession, byte[] buffer, string excludeId)
    {
        foreach (var kvp in gameSession.Sessions)
        {
            if (kvp.Key == excludeId)
            {
                continue;
            }

            if (kvp.Value.UdpEndPoint != null)
            {
                _udpClient.SendAsync(buffer, buffer.Length, kvp.Value.UdpEndPoint);
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
    public object SyncRoot { get; } = new(); // 방 수정 상태 동시 수정을 막는 락
    public ConcurrentDictionary<string, PlayerSession> Sessions { get; } = new();
    public ConcurrentDictionary<string, DateTime> JailEntryTimes { get; } = new(); // 감옥 입장 순서
    public Dictionary<string, ArrestState> ActiveArrestsByRobberId { get; } = new(); // 현재 체포 중인 도둑 관리
    public Dictionary<string, DateTime> JailBreakStartedAtByRescuer { get; } = new(); // 탈옥 시작 시간
    public Dictionary<string, float> JailBreakProgressByRescuer { get; } = new(); // 탈옥 진행률 관리한느 필드
    public GamePhase GamePhase { get; set; }
    public int CountdownTime { get; set; } = 3;
    public int GameTime { get; set; } = 300;
}

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
}
