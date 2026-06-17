using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using polrob.Shared;

namespace polrob.Client.Network;

public class GameNetworkClient
{
    private TcpClient? _tcpClient;
    private UdpClient? _udpClient;
    private BinaryReader? _reader;
    private BinaryWriter? _writer;
    private bool _isDisconnected;

    public event Action<List<Player>>? OnInitialStateReceived;
    public event Action<Player>? OnPlayerJoined;
    public event Action<string>? OnPlayerLeft;
    public event Action<Player>? OnPlayerMoved;
    public event Action<PlayerMovementSync>? OnPlayerMovementReceived;
    public event Action<string, string>? OnPlayerArrested;
    public event Action<JailBreakSync>? OnPlayerJailBroken;
    public event Action<JailBreakProgressSync>? OnJailBreakProgressReceived;
    public event Action<GameStateSync>? OnGameStateReceived;

    public async Task ConnectAsync(string ipAddress, Player localPlayer)
    {
        _isDisconnected = false;
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(ipAddress, 7777);

        var stream = _tcpClient.GetStream();
        _reader = new BinaryReader(stream);
        _writer = new BinaryWriter(stream);

        _udpClient = new UdpClient(ipAddress.Contains(":") ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
        _udpClient.Connect(IPAddress.Parse(ipAddress), 7778);

        _ = Task.Run(ReceiveTcpLoop);
        _ = Task.Run(ReceiveUdpLoop);

        SendTcp(TcpMessageType.Join, JsonSerializer.Serialize(localPlayer));
    }

    private void SendTcp(TcpMessageType type, string payload)
    {
        if (_isDisconnected || _writer == null) return;
        lock (_writer)
        {
            _writer.Write(payload.Length + 1);
            _writer.Write((byte)type);
            _writer.Write(payload);
        }
    }

    public void SendMoveUdp(Player player)
    {
        if (_isDisconnected || _udpClient == null) return;
        string json = JsonSerializer.Serialize(PlayerMovementSync.FromPlayer(player));
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _udpClient.SendAsync(bytes, bytes.Length);
    }

    public void Disconnect()
    {
        _isDisconnected = true;

        try { _udpClient?.Dispose(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }

        _udpClient = null;
        _tcpClient = null;
        _reader = null;
        _writer = null;
    }

    private void ReceiveTcpLoop()
    {
        if (_reader == null) return;
        try
        {
            while (!_isDisconnected)
            {
                int length = _reader.ReadInt32();
                var type = (TcpMessageType)_reader.ReadByte();
                string json = _reader.ReadString();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (type == TcpMessageType.InitialState)
                    {
                        var players = JsonSerializer.Deserialize<List<Player>>(json);
                        if (players != null) OnInitialStateReceived?.Invoke(players);
                    }
                    else if (type == TcpMessageType.Joined)
                    {
                        var player = JsonSerializer.Deserialize<Player>(json);
                        if (player != null) OnPlayerJoined?.Invoke(player);
                    }
                    else if (type == TcpMessageType.Left)
                    {
                        OnPlayerLeft?.Invoke(json);
                    }
                    else if (type == TcpMessageType.Arrested)
                    {
                        var ids = json.Split(',');
                        if (ids.Length == 2)
                        {
                            OnPlayerArrested?.Invoke(ids[0], ids[1]);
                        }
                    }
                    else if (type == TcpMessageType.GameState)
                    {
                        var syncData = JsonSerializer.Deserialize<GameStateSync>(json);
                        if (syncData != null) OnGameStateReceived?.Invoke(syncData);
                    }
                    else if (type == TcpMessageType.JailBreak)
                    {
                        var syncData = JsonSerializer.Deserialize<JailBreakSync>(json);
                        if (syncData != null) OnPlayerJailBroken?.Invoke(syncData);
                    }
                    else if (type == TcpMessageType.PlayerState)
                    {
                        var player = JsonSerializer.Deserialize<Player>(json);
                        if (player != null) OnPlayerMoved?.Invoke(player);
                    }
                    else if (type == TcpMessageType.JailBreakProgress)
                    {
                        var syncData = JsonSerializer.Deserialize<JailBreakProgressSync>(json);
                        if (syncData != null) OnJailBreakProgressReceived?.Invoke(syncData);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            if (!_isDisconnected)
            {
                System.Diagnostics.Debug.WriteLine($"TCP Receive error: {ex.Message}");
            }
        }
    }

    private async Task ReceiveUdpLoop()
    {
        if (_udpClient == null) return;
        while (!_isDisconnected)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync();
                string json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                var movement = JsonSerializer.Deserialize<PlayerMovementSync>(json);

                if (movement != null && !string.IsNullOrWhiteSpace(movement.Id))
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnPlayerMovementReceived?.Invoke(movement);
                    });
                    continue;
                }

                var player = JsonSerializer.Deserialize<Player>(json);
                if (player != null)
                {
                    MainThread.BeginInvokeOnMainThread(() => OnPlayerMoved?.Invoke(player));
                }
            }
            catch (Exception ex)
            {
                if (_isDisconnected || ex is ObjectDisposedException)
                {
                    break;
                }

                System.Diagnostics.Debug.WriteLine($"UDP Receive error: {ex.Message}");
            }
        }
    }
}
