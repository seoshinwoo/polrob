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
    private readonly ConcurrentDictionary<string, string> _playerRooms = new();
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

    public GameNetworkServer(GameRoomService gameRoomService)
    {
        _gameRoomService = gameRoomService;
        // 7777 for reliable TCP (Join, Leave, InitialState)
        _tcpListener = new TcpListener(IPAddress.Any, 7777);
        // 7778 for fast UDP (Movement)
        _udpClient = new UdpClient(7778);
    }

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

    public override void Dispose()
    {
        _stateTimer?.Dispose();
        _ruleTimer?.Dispose();
        base.Dispose();
    }

    private void GameRuleTickCallback(object? state)
    {
        foreach (var sessionEntry in _gameSessions.ToArray())
        {
            var roomId = sessionEntry.Key;
            var gameSession = sessionEntry.Value;

            lock (gameSession.SyncRoot)
            {
                if (gameSession.GamePhase != 2 || gameSession.Sessions.Count == 0)
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

                if (gameSession.GamePhase == 0)
                {
                    gameSession.GamePhase = 1; // First player joined, start countdown
                    gameSession.CountdownTime = 3;
                    gameSession.GameTime = 300;
                }
                else if (gameSession.GamePhase == 1)
                {
                    gameSession.CountdownTime--;
                    if (gameSession.CountdownTime < 0)
                    {
                        gameSession.GamePhase = 2;
                        gameSession.CountdownTime = 0;
                    }
                }
                else if (gameSession.GamePhase == 2)
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
                        gameSession.GamePhase = 3;
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

                BroadcastTcp(gameSession, 6, JsonSerializer.Serialize(syncData), null);
            }
        }
    }

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
                // [Byte Packet Type: 1=Join, 2=Joined, 3=Left, 4=InitialState, 5=Arrested,
                // 6=GameState, 7=JailBreak, 8=PlayerState, 9=JailBreakProgress]
                // [String JSON Payload]
                _ = reader.ReadInt32();
                var type = reader.ReadByte();
                var json = reader.ReadString();

                if (type == 1)
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
                    SendTcp(writer, 4, JsonSerializer.Serialize(allPlayers));
                    Console.WriteLine($"{roomId} 방 {allPlayers.Count}명에게 플레이어 초기화!!");

                    var syncData = new GameStateSync
                    {
                        RoomId = roomId,
                        Phase = gameSession.GamePhase,
                        CountdownTime = gameSession.CountdownTime,
                        GameTime = gameSession.GameTime
                    };
                    SendTcp(writer, 6, JsonSerializer.Serialize(syncData));

                    BroadcastTcp(gameSession, 2, JsonSerializer.Serialize(player), playerId);
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
                        BroadcastTcp(gameSession, 3, playerId, null);
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

            BroadcastTcp(gameSession, 8, JsonSerializer.Serialize(robber), null);

            if (gameSession.Sessions.TryGetValue(arrest.PoliceId, out var policeSession))
            {
                policeSession.PlayerState.IsMoving = false;
                BroadcastTcp(gameSession, 8, JsonSerializer.Serialize(policeSession.PlayerState), null);
            }
        }
    }

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

        BroadcastTcp(gameSession, 5, $"{police.Id},{robber.Id}", null);
        BroadcastTcp(gameSession, 8, JsonSerializer.Serialize(police), null);
        BroadcastTcp(gameSession, 8, JsonSerializer.Serialize(robber), null);
    }

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

            BroadcastTcp(gameSession, 7, JsonSerializer.Serialize(syncData), null);
            BroadcastTcp(gameSession, 8, JsonSerializer.Serialize(target), null);
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

    private static void BroadcastJailBreakProgress(GameSession gameSession, string roomId)
    {
        var syncData = new JailBreakProgressSync
        {
            RoomId = roomId,
            ProgressByRescuer = new Dictionary<string, float>(gameSession.JailBreakProgressByRescuer)
        };

        BroadcastTcp(gameSession, 9, JsonSerializer.Serialize(syncData), null);
    }

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

    private static bool IsPlayerInActiveArrest(GameSession gameSession, string playerId)
    {
        return gameSession.ActiveArrestsByRobberId.ContainsKey(playerId) ||
               gameSession.ActiveArrestsByRobberId.Values.Any(a => a.PoliceId == playerId);
    }

    private bool IsPlayerMovementLocked(GameSession gameSession, Player player)
    {
        return IsPlayerInActiveArrest(gameSession, player.Id) ||
               (player.Role == PlayerRole.Robber && IsInJail(player));
    }

    private bool IsInJail(Player player)
    {
        return player.X >= _map.Jail.LeftTop.X &&
               player.X <= _map.Jail.RightBottom.X &&
               player.Y >= _map.Jail.LeftTop.Y &&
               player.Y <= _map.Jail.RightBottom.Y;
    }

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

    private static float GetVisionRange(Player player)
    {
        return player.Radius * 2f * VisionRangePlayerSizeMultiplier;
    }

    private static float GetFacingAngle(Player player)
    {
        return NormalizeDegrees(player.Angle + 90f);
    }

    private static float NormalizeDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees < 0)
        {
            degrees += 360f;
        }

        return degrees;
    }

    private static float ShortestAngleDifference(float fromDegrees, float toDegrees)
    {
        var difference = NormalizeDegrees(toDegrees - fromDegrees);
        return difference > 180f ? difference - 360f : difference;
    }

    private bool IsTouchingOrNearJail(Player player)
    {
        var closestX = Math.Max(_map.Jail.LeftTop.X, Math.Min(player.X, _map.Jail.RightBottom.X));
        var closestY = Math.Max(_map.Jail.LeftTop.Y, Math.Min(player.Y, _map.Jail.RightBottom.Y));
        var distanceX = player.X - closestX;
        var distanceY = player.Y - closestY;
        var allowedDistance = player.Radius + JailBreakContactTolerance;

        return distanceX * distanceX + distanceY * distanceY <= allowedDistance * allowedDistance;
    }

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

    private static bool IsCircleCollidingWithBuilding(float x, float y, float radius, MapBuilding building)
    {
        var closestX = Math.Max(building.LeftTop.X, Math.Min(x, building.RightBottom.X));
        var closestY = Math.Max(building.LeftTop.Y, Math.Min(y, building.RightBottom.Y));
        var distanceX = x - closestX;
        var distanceY = y - closestY;

        return distanceX * distanceX + distanceY * distanceY < radius * radius;
    }

    private static void SendTcp(BinaryWriter writer, byte type, string payload)
    {
        lock (writer)
        {
            writer.Write(payload.Length + 1);
            writer.Write(type);
            writer.Write(payload);
        }
    }

    private static void BroadcastTcp(GameSession gameSession, byte type, string payload, string? excludeId)
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
                    BroadcastTcp(gameSession, 8, authoritativePlayerJson, null);
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

    private static string NormalizeRoomId(string? roomId)
    {
        return string.IsNullOrWhiteSpace(roomId) ? DefaultRoomId : roomId;
    }
}

public class GameSession
{
    public object SyncRoot { get; } = new();
    public ConcurrentDictionary<string, PlayerSession> Sessions { get; } = new();
    public ConcurrentDictionary<string, DateTime> JailEntryTimes { get; } = new();
    public Dictionary<string, ArrestState> ActiveArrestsByRobberId { get; } = new();
    public Dictionary<string, DateTime> JailBreakStartedAtByRescuer { get; } = new();
    public Dictionary<string, float> JailBreakProgressByRescuer { get; } = new();
    public int GamePhase { get; set; }
    public int CountdownTime { get; set; } = 3;
    public int GameTime { get; set; } = 300;
}

public class ArrestState
{
    public string PoliceId { get; set; } = string.Empty;
    public string RobberId { get; set; } = string.Empty;
    public DateTime CompletesAtUtc { get; set; }
}

public class PlayerSession
{
    public TcpClient Client { get; set; } = null!;
    public BinaryWriter Writer { get; set; } = null!;
    public Player PlayerState { get; set; } = null!;
    public IPEndPoint? UdpEndPoint { get; set; }
}
