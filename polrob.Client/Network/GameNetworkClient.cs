using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using polrob.Shared;

namespace polrob.Client.Network;

public class GameNetworkClient
{
    private TcpClient _tcpClient;
    private UdpClient _udpClient;
    private BinaryReader _reader;
    private BinaryWriter _writer;
    private IPEndPoint _serverUdpEndpoint;

    public event Action<List<Player>>? OnInitialStateReceived;
    public event Action<Player>? OnPlayerJoined;
    public event Action<string>? OnPlayerLeft;
    public event Action<Player>? OnPlayerMoved;

    public async Task ConnectAsync(string ipAddress, Player localPlayer)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(ipAddress, 7777);

        var stream = _tcpClient.GetStream();
        _reader = new BinaryReader(stream);
        _writer = new BinaryWriter(stream);

        _udpClient = new UdpClient(ipAddress.Contains(":") ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
        _udpClient.Connect(IPAddress.Parse(ipAddress), 7778);

        _ = Task.Run(ReceiveTcpLoop);
        _ = Task.Run(ReceiveUdpLoop);

        // 1 = Join
        SendTcp(1, JsonSerializer.Serialize(localPlayer));
    }

    private void SendTcp(byte type, string payload)
    {
        lock (_writer)
        {
            _writer.Write(payload.Length + 1);
            _writer.Write(type);
            _writer.Write(payload);
        }
    }

    public void SendMoveUdp(Player player)
    {
        if (_udpClient == null) return;
        string json = JsonSerializer.Serialize(player);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _udpClient.SendAsync(bytes, bytes.Length);
    }

    private void ReceiveTcpLoop()
    {
        try
        {
            while (true)
            {
                int length = _reader.ReadInt32();
                byte type = _reader.ReadByte();
                string json = _reader.ReadString();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (type == 4) // Initial State
                    {
                        var players = JsonSerializer.Deserialize<List<Player>>(json);
                        if (players != null) OnInitialStateReceived?.Invoke(players);
                    }
                    else if (type == 2) // Joined
                    {
                        var player = JsonSerializer.Deserialize<Player>(json);
                        if (player != null) OnPlayerJoined?.Invoke(player);
                    }
                    else if (type == 3) // Left
                    {
                        OnPlayerLeft?.Invoke(json);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TCP Receive error: {ex.Message}");
        }
    }

    private async Task ReceiveUdpLoop()
    {
        while (true)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync();
                string json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                var player = JsonSerializer.Deserialize<Player>(json);

                if (player != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnPlayerMoved?.Invoke(player);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UDP Receive error: {ex.Message}");
            }
        }
    }
}
