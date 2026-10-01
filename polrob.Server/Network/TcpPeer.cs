using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using polrob.Shared;

namespace polrob.Server.Network;

// A slow reader may fill only this bounded queue, never block a room tick.
public sealed class TcpPeer
{
    private readonly Channel<byte[]> _outgoing;
    private readonly TcpClient _client;
    private readonly TimeSpan _sendTimeout;
    private readonly int _maxQueuedBytes;
    private int _queuedBytes;
    private readonly Action _onFailure;
    private readonly Action _onSent;

    public TcpPeer(TcpClient client, int capacity, int maxQueuedBytes, TimeSpan sendTimeout,
        Action onFailure, Action onSent)
    {
        _client = client;
        _maxQueuedBytes = maxQueuedBytes;
        _sendTimeout = sendTimeout;
        _onFailure = onFailure;
        _onSent = onSent;
        _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public bool TrySend(TcpMessageType type, string payload)
    {
        var length = Encoding.UTF8.GetByteCount(payload);
        if (length > 65_536) return Fail();
        using var buffer = new MemoryStream(length + 10);
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
        var prefixBytes = 1;
        for (var remaining = length; remaining >= 128; remaining >>= 7) prefixBytes++;
        writer.Write(checked(1 + prefixBytes + length));
        writer.Write((byte)type);
        writer.Write(payload);
        var frame = buffer.ToArray();
        var queued = Interlocked.Add(ref _queuedBytes, frame.Length);
        if (queued <= _maxQueuedBytes && _outgoing.Writer.TryWrite(frame)) return true;
        Interlocked.Add(ref _queuedBytes, -frame.Length);
        return Fail();
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            var stream = _client.GetStream();
            await foreach (var frame in _outgoing.Reader.ReadAllAsync(stoppingToken))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                deadline.CancelAfter(_sendTimeout);
                await stream.WriteAsync(frame, deadline.Token);
                Interlocked.Add(ref _queuedBytes, -frame.Length);
                _onSent();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        { Fail(); }
        finally { _outgoing.Writer.TryComplete(); }
    }

    public void Complete() => _outgoing.Writer.TryComplete();
    private bool Fail()
    {
        _onFailure();
        _outgoing.Writer.TryComplete();
        _client.Close();
        return false;
    }
}
