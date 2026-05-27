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

        _ = Task.Run(() => AcceptTcpClientsAsync(stoppingToken), stoppingToken);
        _ = Task.Run(() => ReceiveUdpAsync(stoppingToken), stoppingToken);
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
                // [Byte Packet Type: 1=Join, 2=Joined, 3=Left, 4=InitialState, 5=Arrested]
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
                        player.Role = _sessions.Count() == 0 ? PlayerRole.Police : PlayerRole.Robber; // Test

                        // 경찰은 경찰서 앞에 스폰
                        if (player.Role == PlayerRole.Police)
                        {
                            var map = new GameMap();
                            player.X = map.PoliceStation.Center.X;
                            player.Y = map.PoliceStation.RightBottom.Y + 200f;
                        }

                        _sessions[playerId] = new PlayerSession { Client = client, Writer = writer, PlayerState = player };

                        Console.WriteLine($"Player Connected [TCP]: {playerId}");

                        // Send all current players to the new player
                        var allPlayers = _sessions.Values.Select(s => s.PlayerState).ToList();
                        SendTcp(writer, 4, JsonSerializer.Serialize(allPlayers));
                        Console.WriteLine($"{allPlayers.Count}명에게 플레이어 초기화!!");

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
            }
        }
        catch { /* Disconnected */ }
        finally
        {
            if (playerId != null && _sessions.TryRemove(playerId, out _))
            {
                Console.WriteLine($"Player Disconnected: {playerId}");
                BroadcastTcp(3, playerId, null);
            }
            client.Close();
        }
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
