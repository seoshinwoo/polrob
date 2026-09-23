using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using polrob.Shared;

namespace polrob.Client.Network;

public class GameNetworkClient
{
    private static readonly TimeSpan JoinAcknowledgementTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HeartbeatAcknowledgementTimeout = TimeSpan.FromSeconds(5);

    private TcpClient? _tcpClient;
    private UdpClient? _udpClient;
    private BinaryReader? _reader;
    private BinaryWriter? _writer;
    private bool _isDisconnected;
    private ulong _movementInputSequence;
    private string _movementSessionToken = string.Empty;
    private TaskCompletionSource _joinAcknowledged = CreateJoinAcknowledgementSource();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _heartbeatAcknowledgements = new();
    private CancellationTokenSource? _heartbeatCancellation;

    public event Action<List<Player>>? OnInitialStateReceived;
    public event Action<Player>? OnPlayerJoined;
    public event Action<string>? OnPlayerLeft;
    public event Action<Player>? OnPlayerMoved;
    public event Action<PlayerMovementSync>? OnPlayerMovementReceived;
    public event Action<string, string>? OnPlayerArrested;
    public event Action<JailBreakSync>? OnPlayerJailBroken;
    public event Action<JailBreakProgressSync>? OnJailBreakProgressReceived;
    public event Action<GameStateSync>? OnGameStateReceived;
    public event Action<OpponentProximitySync>? OnOpponentProximityReceived;

    public async Task ConnectAsync(
        string ipAddress,
        Player localPlayer,
        string sessionToken,
        CancellationToken cancellationToken = default,
        string mapId = MapRegistry.DefaultId)
    {
        _isDisconnected = false;
        _movementInputSequence = 0;
        _movementSessionToken = string.Empty;
        _joinAcknowledged = CreateJoinAcknowledgementSource();
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(ipAddress, 7777, cancellationToken);

        var stream = _tcpClient.GetStream();
        _reader = new BinaryReader(stream);
        _writer = new BinaryWriter(stream);

        _udpClient = new UdpClient(ipAddress.Contains(":") ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
        _udpClient.Connect(IPAddress.Parse(ipAddress), 7778);

        _ = Task.Run(ReceiveTcpLoop);
        _ = Task.Run(ReceiveUdpLoop);

        SendTcp(TcpMessageType.Join, JsonSerializer.Serialize(new GameJoinRequest
        {
            SessionToken = sessionToken,
            RoomId = localPlayer.RoomId,
            MapId = mapId
        }));

        try
        {
            // TCP 연결 완료는 소켓만 열렸다는 뜻입니다. 서버가 Join 명령을 처리해
            // 참가자 레지스트리에 등록한 뒤 보내는 MovementSession까지 기다려야
            // 바로 이어지는 팀 보이스 토큰 요청이 간헐적으로 403이 되지 않습니다.
            await _joinAcknowledged.Task.WaitAsync(
                JoinAcknowledgementTimeout,
                cancellationToken);
            var heartbeatCancellation = new CancellationTokenSource();
            _heartbeatCancellation = heartbeatCancellation;
            _ = Task.Run(() => RunHeartbeatLoopAsync(heartbeatCancellation.Token));
        }
        catch
        {
            Disconnect();
            throw;
        }
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

    public void SendMoveUdp(string playerId, float inputX, float inputY)
    {
        if (_isDisconnected || _udpClient == null) return;
        var input = new PlayerMovementInput
        {
            Id = playerId,
            X = inputX,
            Y = inputY,
            Sequence = ++_movementInputSequence,
            Token = _movementSessionToken
        };
        string json = JsonSerializer.Serialize(input);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _udpClient.SendAsync(bytes, bytes.Length);
    }

    public void Disconnect()
    {
        _isDisconnected = true;
        _joinAcknowledged.TrySetCanceled();
        _heartbeatCancellation?.Cancel();
        _heartbeatCancellation?.Dispose();
        _heartbeatCancellation = null;
        foreach (var (_, acknowledgement) in _heartbeatAcknowledgements)
        {
            acknowledgement.TrySetCanceled();
        }
        _heartbeatAcknowledgements.Clear();

        try { _udpClient?.Dispose(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }

        _udpClient = null;
        _tcpClient = null;
        _reader = null;
        _writer = null;
    }

    public async Task RefreshServerRegistrationAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isDisconnected || _writer == null)
        {
            throw new IOException("게임 서버에 연결되어 있지 않습니다.");
        }

        var requestId = Guid.NewGuid().ToString("N");
        var acknowledgement = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_heartbeatAcknowledgements.TryAdd(requestId, acknowledgement))
        {
            throw new IOException("게임 서버 연결 확인 요청을 만들지 못했습니다.");
        }

        try
        {
            SendTcp(TcpMessageType.Heartbeat, requestId);
            await acknowledgement.Task.WaitAsync(
                HeartbeatAcknowledgementTimeout,
                cancellationToken);
        }
        finally
        {
            _heartbeatAcknowledgements.TryRemove(requestId, out _);
        }
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

                if (type == TcpMessageType.MovementSession)
                {
                    _movementSessionToken = json;
                    _joinAcknowledged.TrySetResult();
                    continue;
                }
                if (type == TcpMessageType.HeartbeatAcknowledged)
                {
                    if (_heartbeatAcknowledgements.TryRemove(json, out var acknowledgement))
                    {
                        acknowledgement.TrySetResult();
                    }
                    continue;
                }

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
                    else if (type == TcpMessageType.OpponentProximity)
                    {
                        var proximity = JsonSerializer.Deserialize<OpponentProximitySync>(json);
                        if (proximity != null)
                        {
                            OnOpponentProximityReceived?.Invoke(proximity);
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            if (!_isDisconnected)
            {
                System.Diagnostics.Debug.WriteLine($"TCP Receive error: {ex.Message}");
                _joinAcknowledged.TrySetException(
                    new IOException("게임 서버가 입장을 승인하기 전에 연결이 종료되었습니다.", ex));
            }
        }
    }

    private static TaskCompletionSource CreateJoinAcknowledgementSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, cancellationToken);
                SendTcp(TcpMessageType.Heartbeat, string.Empty);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (!_isDisconnected)
        {
            System.Diagnostics.Debug.WriteLine($"TCP heartbeat error: {exception.Message}");
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
