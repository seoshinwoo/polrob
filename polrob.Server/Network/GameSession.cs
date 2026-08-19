using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using polrob.Shared;

namespace polrob.Server.Network;

public class GameSession
{
    public GameSession(int commandQueueCapacity)
    {
        Commands = Channel.CreateBounded<RoomCommand>(
            new BoundedChannelOptions(commandQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    public object CommandGate { get; } = new();
    public Channel<RoomCommand> Commands { get; } // 해당 방으로 들어온 입장, 퇴자 , 이동 명령이 잠시 쌓임. 
    public int QueuedCommandCount;
    public ConcurrentDictionary<string, PlayerSession> Sessions { get; } = new();
    public ConcurrentDictionary<string, DateTime> JailEntryTimes { get; } = new();
    public Dictionary<string, ArrestState> ActiveArrestsByRobberId { get; } = new();
    public Dictionary<string, DateTime> JailBreakStartedAtByRescuer { get; } = new();
    public Dictionary<string, float> JailBreakProgressByRescuer { get; } = new();
    public HashSet<string> PendingUdpMovementPlayerIds { get; } = new();
    public GamePhase GamePhase { get; set; }
    public int CountdownTime { get; set; } = 3;
    public int GameTime { get; set; } = 300;
    public PlayerRole? WinnerRole { get; set; }
    public int ElapsedGameTime { get; set; }
    public DateTime? GameStartedAtUtc { get; set; }
    public bool HasHadPlayers { get; set; }
    public DateTime? EmptySinceUtc { get; set; }
    public bool IsStopping { get; set; }
}

public abstract record RoomCommand;

public sealed record JoinRoomCommand(
    Player Player,
    TcpClient Client,
    BinaryWriter Writer) : RoomCommand;

public sealed record LeaveRoomCommand(string PlayerId) : RoomCommand;

public sealed record MoveRoomCommand(PlayerMovementInput Input, IPEndPoint RemoteEndPoint) : RoomCommand;

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
    public float InputX { get; set; }
    public float InputY { get; set; }
    public ulong LastMovementInputSequence { get; set; }
    public DateTime LastMovementInputAtUtc { get; set; } = DateTime.MinValue;
    public string MovementSessionToken { get; init; } = string.Empty;
    public List<Obstacle> NearbyCollisionObstacles { get; } = new();
    public HashSet<string> VisibleOpponentPlayerIds { get; } = new();
}
