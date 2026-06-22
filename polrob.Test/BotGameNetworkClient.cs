using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using polrob.Shared;

namespace polrob.Test;

public sealed class BotGameNetworkClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private UdpClient? _udpClient;
    private BinaryReader? _reader;
    private BinaryWriter? _writer;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _tcpReceiveTask;
    private Task? _udpReceiveTask;
    private ulong _movementInputSequence;
    private string _movementSessionToken = string.Empty;

    public event Action<List<Player>>? InitialStateReceived;
    public event Action<Player>? PlayerJoined;
    public event Action<string>? PlayerLeft;
    public event Action<Player>? PlayerStateReceived;
    public event Action<PlayerMovementSync>? PlayerMovementReceived;
    public event Action<string, string>? PlayerArrested;
    public event Action<JailBreakSync>? JailBreakReceived;
    public event Action<GameStateSync>? GameStateReceived;

    public async Task ConnectAsync(
        string serverHost,
        Player localPlayer,
        CancellationToken cancellationToken)
    {
        _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(serverHost, 7777, cancellationToken);

        var stream = _tcpClient.GetStream();
        _reader = new BinaryReader(stream);
        _writer = new BinaryWriter(stream);

        _udpClient = new UdpClient();
        _udpClient.Connect(serverHost, 7778);

        _tcpReceiveTask = Task.Run(
            () => ReceiveTcpLoop(_receiveCancellation.Token),
            CancellationToken.None);
        _udpReceiveTask = Task.Run(
            () => ReceiveUdpLoopAsync(_receiveCancellation.Token),
            CancellationToken.None);

        SendTcp(TcpMessageType.Join, JsonSerializer.Serialize(localPlayer));
    }

    public async ValueTask SendMoveAsync(Player player)
    {
        if (_udpClient == null)
        {
            return;
        }

        var angleRadians = (player.Angle + 90f) * MathF.PI / 180f;
        var input = new PlayerMovementInput
        {
            Id = player.Id,
            X = player.IsMoving ? MathF.Cos(angleRadians) : 0f,
            Y = player.IsMoving ? MathF.Sin(angleRadians) : 0f,
            Sequence = ++_movementInputSequence,
            Token = _movementSessionToken
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input));
        await _udpClient.SendAsync(bytes);
    }

    private void SendTcp(TcpMessageType type, string payload)
    {
        if (_writer == null)
        {
            return;
        }

        lock (_writer)
        {
            _writer.Write(payload.Length + 1);
            _writer.Write((byte)type);
            _writer.Write(payload);
        }
    }

    private void ReceiveTcpLoop(CancellationToken cancellationToken)
    {
        if (_reader == null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _ = _reader.ReadInt32();
                var type = (TcpMessageType)_reader.ReadByte();
                var payload = _reader.ReadString();
                DispatchTcpMessage(type, payload);
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DispatchTcpMessage(TcpMessageType type, string payload)
    {
        switch (type)
        {
            case TcpMessageType.InitialState:
                var players = JsonSerializer.Deserialize<List<Player>>(payload);
                if (players != null)
                {
                    InitialStateReceived?.Invoke(players);
                }
                break;

            case TcpMessageType.Joined:
                var joinedPlayer = JsonSerializer.Deserialize<Player>(payload);
                if (joinedPlayer != null)
                {
                    PlayerJoined?.Invoke(joinedPlayer);
                }
                break;

            case TcpMessageType.Left:
                PlayerLeft?.Invoke(payload);
                break;

            case TcpMessageType.Arrested:
                var playerIds = payload.Split(',');
                if (playerIds.Length == 2)
                {
                    PlayerArrested?.Invoke(playerIds[0], playerIds[1]);
                }
                break;

            case TcpMessageType.GameState:
                var gameState = JsonSerializer.Deserialize<GameStateSync>(payload);
                if (gameState != null)
                {
                    GameStateReceived?.Invoke(gameState);
                }
                break;

            case TcpMessageType.JailBreak:
                var jailBreak = JsonSerializer.Deserialize<JailBreakSync>(payload);
                if (jailBreak != null)
                {
                    JailBreakReceived?.Invoke(jailBreak);
                }
                break;

            case TcpMessageType.PlayerState:
                var playerState = JsonSerializer.Deserialize<Player>(payload);
                if (playerState != null)
                {
                    PlayerStateReceived?.Invoke(playerState);
                }
                break;

            case TcpMessageType.MovementSession:
                _movementSessionToken = payload;
                break;
        }
    }

    private async Task ReceiveUdpLoopAsync(CancellationToken cancellationToken)
    {
        if (_udpClient == null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                var movement = JsonSerializer.Deserialize<PlayerMovementSync>(result.Buffer);
                if (movement != null && !string.IsNullOrWhiteSpace(movement.Id))
                {
                    PlayerMovementReceived?.Invoke(movement);
                    continue;
                }

                var player = JsonSerializer.Deserialize<Player>(result.Buffer);
                if (player != null)
                {
                    PlayerStateReceived?.Invoke(player);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_receiveCancellation != null)
        {
            await _receiveCancellation.CancelAsync();
        }

        _udpClient?.Dispose();
        _tcpClient?.Dispose();
        _reader?.Dispose();
        _writer?.Dispose();

        var receiveTasks = new[] { _tcpReceiveTask, _udpReceiveTask }
            .Where(task => task != null)
            .Cast<Task>();

        try
        {
            await Task.WhenAll(receiveTasks);
        }
        catch
        {
            // Transport shutdown is expected to interrupt pending reads.
        }

        _receiveCancellation?.Dispose();
    }
}
