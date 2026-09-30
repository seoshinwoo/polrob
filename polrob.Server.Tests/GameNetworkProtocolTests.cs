using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NUnit.Framework;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Tests;

public sealed class GameNetworkProtocolTests
{
    private static readonly MethodInfo ReadTcpFrameMethod = typeof(GameNetworkServer).GetMethod(
        "ReadTcpFrame", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo ParseUdpMovementMethod = typeof(GameNetworkServer).GetMethod(
        "ParseUdpMovement", BindingFlags.Static | BindingFlags.NonPublic)!;

    [TestCase(false)]
    [TestCase(true)]
    public void TcpJoinFrameAcceptsCurrentAndLegacyLength(bool legacyLength)
    {
        var payload = JsonSerializer.Serialize(
            new GameJoinRequest
            {
                SessionToken = "login-token",
                RoomId = "한글 방",
                MapId = "town"
            },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.That(Encoding.UTF8.GetByteCount(payload), Is.GreaterThan(payload.Length));
        using var stream = new MemoryStream(CreateTcpFrame(TcpMessageType.Join, payload, legacyLength));
        using var reader = new BinaryReader(stream);

        var (type, decodedPayload) = ReadTcpFrame(reader);

        Assert.Multiple(() =>
        {
            Assert.That(type, Is.EqualTo(TcpMessageType.Join));
            Assert.That(decodedPayload, Is.EqualTo(payload));
            Assert.That(stream.Position, Is.EqualTo(stream.Length));
        });
    }

    [Test]
    public void TcpFrameRejectsOversizedDeclaredLengthBeforeReadingBody()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(int.MaxValue));
        using var reader = new BinaryReader(stream);

        Assert.That(() => ReadTcpFrame(reader), Throws.TypeOf<InvalidDataException>());
        Assert.That(stream.Position, Is.EqualTo(sizeof(int)));
    }

    [Test]
    public void TcpFrameRejectsOversizedStringLengthBeforeReadingBody()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(1);
            writer.Write((byte)TcpMessageType.Join);
            writer.Write((byte)0x81); // 4097 UTF-8 bytes, beyond the 4096-byte limit.
            writer.Write((byte)0x20);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.That(() => ReadTcpFrame(reader), Throws.TypeOf<InvalidDataException>());
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void TcpFrameRejectsLengthMismatch()
    {
        var frame = CreateTcpFrame(TcpMessageType.Join, "{}", false);
        BitConverter.GetBytes(100).CopyTo(frame, 0);
        using var stream = new MemoryStream(frame);
        using var reader = new BinaryReader(stream);

        Assert.That(() => ReadTcpFrame(reader), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void TcpFrameRejectsMalformedStringLengthPrefix()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(6);
            writer.Write((byte)TcpMessageType.Join);
            writer.Write(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80 });
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.That(() => ReadTcpFrame(reader), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void TcpFrameRejectsLengthPrefixOutsideInt32Range()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(6);
            writer.Write((byte)TcpMessageType.Join);
            writer.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x08 });
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        Assert.That(() => ReadTcpFrame(reader), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void TcpFrameRejectsInvalidUtf8AndTruncatedPayload()
    {
        using var invalidUtf8Stream = new MemoryStream(new byte[]
        {
            3, 0, 0, 0, (byte)TcpMessageType.Join, 1, 0xFF
        });
        using var invalidUtf8Reader = new BinaryReader(invalidUtf8Stream);
        Assert.That(() => ReadTcpFrame(invalidUtf8Reader), Throws.TypeOf<InvalidDataException>());

        var truncatedFrame = CreateTcpFrame(TcpMessageType.Join, "{\"x\":1}", false);
        using var truncatedStream = new MemoryStream(truncatedFrame[..^1]);
        using var truncatedReader = new BinaryReader(truncatedStream);
        Assert.That(() => ReadTcpFrame(truncatedReader), Throws.TypeOf<EndOfStreamException>());
    }

    [Test]
    public void UdpMovementAcceptsValidPacketAtLimitAndRejectsOversizedPacket()
    {
        var json = JsonSerializer.Serialize(new PlayerMovementInput
        {
            Id = "player-1",
            X = 0.5f,
            Y = -0.5f,
            Sequence = 1,
            Token = "movement-token"
        });
        var exactLimit = Encoding.UTF8.GetBytes(json.PadRight(2048));
        var overLimit = Encoding.UTF8.GetBytes(json.PadRight(2049));

        var movement = ParseUdpMovement(exactLimit);

        Assert.Multiple(() =>
        {
            Assert.That(movement?.Id, Is.EqualTo("player-1"));
            Assert.That(movement?.Token, Is.EqualTo("movement-token"));
        });
        Assert.That(() => ParseUdpMovement(overLimit), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void UdpMovementRejectsEmptyAndMalformedPackets()
    {
        Assert.That(() => ParseUdpMovement(Array.Empty<byte>()), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => ParseUdpMovement(Encoding.UTF8.GetBytes("{")), Throws.TypeOf<JsonException>());
        Assert.That(ParseUdpMovement(Encoding.UTF8.GetBytes("null")), Is.Null);
    }

    private static (TcpMessageType Type, string Payload) ReadTcpFrame(BinaryReader reader)
    {
        try
        {
            return ((TcpMessageType Type, string Payload))ReadTcpFrameMethod.Invoke(null, [reader])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static PlayerMovementInput? ParseUdpMovement(byte[] datagram)
    {
        try
        {
            return (PlayerMovementInput?)ParseUdpMovementMethod.Invoke(null, [datagram]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static byte[] CreateTcpFrame(TcpMessageType type, string payload, bool legacyLength)
    {
        var payloadByteLength = Encoding.UTF8.GetByteCount(payload);
        var prefixByteLength = 1;
        for (var remaining = payloadByteLength; remaining >= 128; remaining >>= 7)
        {
            prefixByteLength++;
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(legacyLength
                ? 1 + payload.Length
                : 1 + prefixByteLength + payloadByteLength);
            writer.Write((byte)type);
            writer.Write(payload);
        }

        return stream.ToArray();
    }
}
