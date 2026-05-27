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
    private readonly ConcurrentDictionary<string, PlayerSession> _sessions = new();
    private readonly ConcurrentDictionary<string, DateTime> _jailEntryTimes = new();
    private readonly GameMap _map = new();

    private int _gamePhase = 0; // 0=Waiting, 1=Countdown, 2=Playing, 3=Ended
    private int _countdownTime = 3;
    private int _gameTime = 300;
    private Timer? _stateTimer;
    private DateTime _lastJailBreakAt = DateTime.MinValue;
    private const float JailBreakReleaseOffset = 20f;
    private const float JailBreakContactTolerance = 90f;
    private const double JailBreakRequestCooldownSeconds = 3d;

    public GameNetworkServer()
    {
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

        _ = Task.Run(() => AcceptTcpClientsAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => ReceiveUdpAsync(stoppingToken), stoppingToken);
    }

    private void GameStateSyncCallback(object? state)
    {
        lock (_sessions)
        {
            if (_sessions.Count == 0)
            {
                _gamePhase = 0;
                _countdownTime = 3;
                _gameTime = 300;
                return; // wait for players
            }

            if (_gamePhase == 0)
            {
                _gamePhase = 1; // First player joined, start countdown
                _countdownTime = 3;
                _gameTime = 300;
            }
            else if (_gamePhase == 1) // Countdown phase
            {
                _countdownTime--;
                if (_countdownTime < 0)
                {
                    _gamePhase = 2; // Transition to Playing
                    _countdownTime = 0;
                }
            }
            else if (_gamePhase == 2) // Playing phase
            {
                _gameTime--;

                // Check if all robbers are caught
                var robbers = _sessions.Values.Where(s => s.PlayerState.Role == PlayerRole.Robber).ToList();
                bool allRobbersCaught = false;

                if (robbers.Count > 0)
                {
                    foreach (var robber in robbers)
                    {
                        RefreshJailEntry(robber.PlayerState);
                    }

                    allRobbersCaught = robbers.All(p => IsInJail(p.PlayerState));
                }

                if (_gameTime <= 0 || allRobbersCaught)
                {
                    _gamePhase = 3; // Game ended
                    _gameTime = 0;
                }
            }

            var syncData = new GameStateSync
            {
                Phase = _gamePhase,
                CountdownTime = _countdownTime,
                GameTime = _gameTime
            };

            BroadcastTcp(6, JsonSerializer.Serialize(syncData), null);
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
            catch { /* Ignore when cancelling */ }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        using var stream = client.GetStream();
        using var reader = new BinaryReader(stream);
        using var writer = new BinaryWriter(stream);
        string? playerId = null;

        try
        {
            while (client.Connected && !stoppingToken.IsCancellationRequested)
            {
                // Simple binary protocol frame:
                // [Int32 Payload Length]
                // [Byte Packet Type: 1=Join, 2=Joined, 3=Left, 4=InitialState, 5=Arrested, 6=GameState, 7=JailBreak]
                // [String JSON Payload]
                int length = reader.ReadInt32();
                byte type = reader.ReadByte();
                string json = reader.ReadString();

                if (type == 1) // Join Request
                {
                    var player = JsonSerializer.Deserialize<Player>(json);
                    if (player != null)
                    {
                        playerId = player.Id;

                        lock (_sessions)
                        {
                            player.Role = _sessions.Count() == 0 ? PlayerRole.Police : PlayerRole.Robber; // Test

                            int policeCount = _sessions.Values.Count(s => s.PlayerState.Role == PlayerRole.Police);
                            int robberCount = _sessions.Values.Count(s => s.PlayerState.Role == PlayerRole.Robber);

                            float gap = 150f;

                            if (player.Role == PlayerRole.Police)
                            {
                                int myIndex = policeCount;
                                float startX = _map.PoliceStation.Center.X - (gap / 2f);
                                player.X = startX + (myIndex * gap);
                                player.Y = _map.PoliceStation.RightBottom.Y + 200f;
                            }
                            else if (player.Role == PlayerRole.Robber)
                            {
                                int myIndex = robberCount;
                                float startX = (_map.Width / 2f) - (gap * 1.5f);
                                player.X = startX + (myIndex * gap);
                                player.Y = _map.Height / 2f;
                            }

                            _sessions[playerId] = new PlayerSession { Client = client, Writer = writer, PlayerState = player };
                        }

                        Console.WriteLine($"Player Connected [TCP]: {playerId}");

                        // Send all current players to the new player
                        var allPlayers = _sessions.Values.Select(s => s.PlayerState).ToList();
                        SendTcp(writer, 4, JsonSerializer.Serialize(allPlayers));
                        Console.WriteLine($"{allPlayers.Count}명에게 플레이어 초기화!!");

                        // Send current game state right away
                        var syncData = new GameStateSync
                        {
                            Phase = _gamePhase,
                            CountdownTime = _countdownTime,
                            GameTime = _gameTime
                        };
                        SendTcp(writer, 6, JsonSerializer.Serialize(syncData));

                        // Broadcast new player join to others
                        BroadcastTcp(2, JsonSerializer.Serialize(player), playerId);
                        Console.WriteLine($"{allPlayers.Count}명에게 브로드캐스트!!");
                    }
                }
                else if (type == 5) // Arrest Request
                {
                    // Relay the arrest event to everyone
                    BroadcastTcp(5, json, null);
                }
                else if (type == 7) // JailBreak Request
                {
                    HandleJailBreakRequest(json);
                }
            }
        }
        catch { /* Disconnected */ }
        finally
        {
            if (playerId != null && _sessions.TryRemove(playerId, out _))
            {
                _jailEntryTimes.TryRemove(playerId, out _);
                Console.WriteLine($"Player Disconnected: {playerId}");
                BroadcastTcp(3, playerId, null);
            }
            client.Close();
        }
    }

    private void HandleJailBreakRequest(string rescuerId)
    {
        lock (_sessions)
        {
            if (_gamePhase != 2)
            {
                return;
            }

            if (!_sessions.TryGetValue(rescuerId, out var rescuerSession))
            {
                return;
            }

            var rescuer = rescuerSession.PlayerState;
            if (rescuer.Role != PlayerRole.Robber || !rescuer.IsMoving || IsInJail(rescuer) || !IsTouchingOrNearJail(rescuer))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _lastJailBreakAt).TotalSeconds < JailBreakRequestCooldownSeconds)
            {
                return;
            }

            var activeRescuers = _sessions.Values
                .Select(s => s.PlayerState)
                .Where(p => p.Role == PlayerRole.Robber &&
                            p.IsMoving &&
                            !IsInJail(p) &&
                            IsTouchingOrNearJail(p))
                .OrderBy(p => p.Id)
                .ToList();

            if (activeRescuers.Count == 0)
            {
                return;
            }

            var targetSessions = _sessions.Values
                .Where(s => s.PlayerState.Role == PlayerRole.Robber &&
                            IsInJail(s.PlayerState))
                .Select(s => new
                {
                    Session = s,
                    EnteredAt = _jailEntryTimes.GetOrAdd(s.PlayerState.Id, now)
                })
                .OrderBy(s => s.EnteredAt)
                .ThenBy(s => s.Session.PlayerState.Id)
                .Take(activeRescuers.Count)
                .ToList();

            if (targetSessions.Count == 0)
            {
                return;
            }

            _lastJailBreakAt = now;

            for (int i = 0; i < targetSessions.Count; i++)
            {
                var target = targetSessions[i].Session.PlayerState;
                var releasePosition = GetJailReleasePosition(target.Radius, i);

                target.X = releasePosition.X;
                target.Y = releasePosition.Y;
                target.Angle = 0f;
                target.IsMoving = false;
                _jailEntryTimes.TryRemove(target.Id, out _);

                var syncData = new JailBreakSync
                {
                    RescuerId = activeRescuers[Math.Min(i, activeRescuers.Count - 1)].Id,
                    RobberId = target.Id,
                    X = target.X,
                    Y = target.Y
                };

                BroadcastTcp(7, JsonSerializer.Serialize(syncData), null);
            }
        }
    }

    private void RefreshJailEntry(Player player)
    {
        if (player.Role != PlayerRole.Robber)
        {
            return;
        }

        if (IsInJail(player))
        {
            _jailEntryTimes.TryAdd(player.Id, DateTime.UtcNow);
        }
        else
        {
            _jailEntryTimes.TryRemove(player.Id, out _);
        }
    }

    private bool IsInJail(Player player)
    {
        return player.X >= _map.Jail.LeftTop.X &&
               player.X <= _map.Jail.RightBottom.X &&
               player.Y >= _map.Jail.LeftTop.Y &&
               player.Y <= _map.Jail.RightBottom.Y;
    }

    private bool IsTouchingOrNearJail(Player player)
    {
        float closestX = Math.Max(_map.Jail.LeftTop.X, Math.Min(player.X, _map.Jail.RightBottom.X));
        float closestY = Math.Max(_map.Jail.LeftTop.Y, Math.Min(player.Y, _map.Jail.RightBottom.Y));
        float distanceX = player.X - closestX;
        float distanceY = player.Y - closestY;
        float allowedDistance = player.Radius + JailBreakContactTolerance;

        return (distanceX * distanceX) + (distanceY * distanceY) <= allowedDistance * allowedDistance;
    }

    private (float X, float Y) GetJailReleasePosition(float radius, int releaseIndex)
    {
        var jail = _map.Jail;
        float startY = jail.RightBottom.Y + radius + JailBreakReleaseOffset;
        var candidates = new List<(float X, float Y)>();
        float[][] rowOffsets =
        {
            new[] { 0f, -jail.Width / 4f, jail.Width / 4f, -jail.Width / 2f + radius, jail.Width / 2f - radius },
            new[] { -jail.Width / 6f, jail.Width / 6f, -jail.Width / 3f, jail.Width / 3f, 0f }
        };

        for (int row = 0; row < 5; row++)
        {
            float y = Math.Clamp(startY + (row * radius * 1.5f), radius, _map.Height - radius);
            var offsets = rowOffsets[row % rowOffsets.Length];

            foreach (float offset in offsets)
            {
                float x = Math.Clamp(jail.Center.X + offset, radius, _map.Width - radius);
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
                float closestX = Math.Max(obs.LeftTop.X, Math.Min(x, obs.RightBottom.X));
                float closestY = Math.Max(obs.LeftTop.Y, Math.Min(y, obs.RightBottom.Y));
                float distanceX = x - closestX;
                float distanceY = y - closestY;

                if ((distanceX * distanceX) + (distanceY * distanceY) < radius * radius)
                {
                    return true;
                }
            }
            else if (obs.Type == "Circle")
            {
                float dx = x - obs.CenterX.X;
                float dy = y - obs.CenterX.Y;
                float radiusSum = radius + obs.Radius;

                if ((dx * dx) + (dy * dy) < radiusSum * radiusSum)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCircleCollidingWithBuilding(float x, float y, float radius, MapBuilding building)
    {
        float closestX = Math.Max(building.LeftTop.X, Math.Min(x, building.RightBottom.X));
        float closestY = Math.Max(building.LeftTop.Y, Math.Min(y, building.RightBottom.Y));
        float distanceX = x - closestX;
        float distanceY = y - closestY;

        return (distanceX * distanceX) + (distanceY * distanceY) < radius * radius;
    }

    private void SendTcp(BinaryWriter writer, byte type, string payload)
    {
        lock (writer)
        {
            writer.Write(payload.Length + 1); // Length doesn't strictly match byte count for string vs binary, but works as an indicator 
            writer.Write(type);
            writer.Write(payload);
        }
    }

    private void BroadcastTcp(byte type, string payload, string? excludeId)
    {
        foreach (var kvp in _sessions)
        {
            if (kvp.Key == excludeId) continue;
            try
            {
                SendTcp(kvp.Value.Writer, type, payload);
            }
            catch { }
        }
    }

    private async Task ReceiveUdpAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(stoppingToken);
                string json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                var player = JsonSerializer.Deserialize<Player>(json);

                if (player != null && _sessions.TryGetValue(player.Id, out var session))
                {
                    // Update server state 
                    session.PlayerState.X = player.X;
                    session.PlayerState.Y = player.Y;
                    session.PlayerState.Angle = player.Angle;
                    session.PlayerState.IsMoving = player.IsMoving;
                    RefreshJailEntry(session.PlayerState);

                    // Register UDP endpoint to map TCP connection to UDP connection
                    if (session.UdpEndPoint == null || !session.UdpEndPoint.Equals(result.RemoteEndPoint))
                    {
                        session.UdpEndPoint = result.RemoteEndPoint;
                        Console.WriteLine($"UDP Endpoint registered for {player.Id}: {result.RemoteEndPoint}");
                    }

                    // Forward to other players via UDP for low latency
                    BroadcastUdp(result.Buffer, player.Id);
                }
            }
            catch { }
        }
    }

    private void BroadcastUdp(byte[] buffer, string excludeId)
    {
        foreach (var kvp in _sessions)
        {
            if (kvp.Key == excludeId) continue;
            if (kvp.Value.UdpEndPoint != null)
            {
                _udpClient.SendAsync(buffer, buffer.Length, kvp.Value.UdpEndPoint);
            }
        }
    }
}

public class PlayerSession
{
    public TcpClient Client { get; set; } = null!;
    public BinaryWriter Writer { get; set; } = null!;
    public Player PlayerState { get; set; } = null!;
    public IPEndPoint? UdpEndPoint { get; set; }
}
