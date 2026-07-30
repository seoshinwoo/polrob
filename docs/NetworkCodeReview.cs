// 동시 실행에 안전한 사전 자료구조를 사용하기 위해 네임스페이스를 가져옵니다.
using System.Collections.Concurrent;
// 좌표와 점 자료형을 사용하기 위해 그리기 네임스페이스를 가져옵니다.
using System.Drawing;
// IP 주소 자료형을 사용하기 위해 네트워크 네임스페이스를 가져옵니다.
using System.Net;
// TCP와 UDP 소켓 자료형을 사용하기 위해 소켓 네임스페이스를 가져옵니다.
using System.Net.Sockets;
// 타이머와 원자적 카운터 기능을 사용하기 위해 스레딩 네임스페이스를 가져옵니다.
using System.Threading;
// 호스팅되는 백그라운드 서비스 기반 클래스를 사용하기 위해 네임스페이스를 가져옵니다.
using Microsoft.Extensions.Hosting;
// 클라이언트와 공유하는 메시지 및 게임 모델을 사용하기 위해 네임스페이스를 가져옵니다.
using polrob.Shared;

// 이 파일의 형식들을 서버 네트워크 영역에 넣습니다.
namespace polrob.Server.Network;

// 게임 네트워크 서버의 한 부분을 선언하고 호스트의 백그라운드 서비스로 실행되게 합니다.
public partial class GameNetworkServer : BackgroundService
// 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
{
    // 7777 포트에서 새 TCP 연결을 받는 리스너를 보관합니다.
    private readonly TcpListener _tcpListener;
    // 7778 포트에서 빠른 이동 패킷을 주고받는 UDP 소켓을 보관합니다.
    private readonly UdpClient _udpClient;
    // 방 ID로 각 방의 실시간 게임 상태를 바로 찾는 동시성 사전입니다.
    private readonly ConcurrentDictionary<string, GameSession> _gameSessions = new();
    // 플레이어 ID로 소속 방 ID를 빠르게 찾는 역방향 인덱스입니다.
    private readonly ConcurrentDictionary<string, string> _playerRooms = new();
    // 플레이어별 UDP 전송량 제한 상태를 보관합니다.
    private readonly ConcurrentDictionary<string, UdpRateLimitState> _udpRateLimits = new();
    // 로비와 방 메타데이터를 관리하는 서비스를 참조합니다.
    private readonly GameRoomService _gameRoomService;
    // 서버 실행 정보와 오류를 기록하는 로거입니다.
    private readonly ILogger<GameNetworkServer> _logger;
    // 맵 크기, 건물, 장애물과 충돌 판정을 제공하는 게임 맵입니다.
    private readonly GameMap _map = new();
    // CPU와 메모리 같은 런타임 지표를 수집합니다.
    private readonly RuntimeMetricSampler _runtimeMetrics = new();
    // 방 하나가 대기시킬 수 있는 명령 수의 상한입니다.
    private readonly int _roomCommandQueueCapacity;
    // 플레이어 한 명에게 초당 허용할 UDP 패킷 수입니다.
    private readonly double _udpPacketsPerSecond;
    // 짧은 순간에 몰려도 허용할 UDP 패킷 여유량입니다.
    private readonly double _udpBurstSize;

    // 1초마다 부하 지표를 출력하는 타이머입니다.
    private Timer? _metricsTimer;
    // 이번 1초 구간에 받은 UDP 패킷 수를 셉니다.
    private long _udpPacketsReceivedThisSecond;
    // 이번 1초 구간에 보낸 UDP 패킷 수를 셉니다.
    private long _udpPacketsSentThisSecond;
    // 이번 1초 구간에 받은 UDP 바이트 수를 셉니다.
    private long _udpBytesReceivedThisSecond;
    // 이번 1초 구간에 보낸 UDP 바이트 수를 셉니다.
    private long _udpBytesSentThisSecond;
    // 전송량 제한으로 거절한 UDP 패킷 수를 셉니다.
    private long _udpPacketsRateLimitedThisSecond;
    // 값이 잘못되어 거절한 UDP 패킷 수를 셉니다.
    private long _udpPacketsInvalidThisSecond;
    // 중복되었거나 늦게 도착한 UDP 패킷 수를 셉니다.
    private long _udpPacketsDuplicateOrLateThisSecond;
    // 방 명령 큐가 가득 차 버린 명령 수를 셉니다.
    private long _roomCommandsDroppedThisSecond;
    // 이번 1초 구간의 TCP 전송 실패 수를 셉니다.
    private long _tcpSendFailuresThisSecond;
    // 이번 1초 구간에 보낸 TCP 패킷 수를 셉니다.
    private long _tcpPacketsSentThisSecond;
    // 이번 1초 구간에 JSON으로 직렬화한 횟수를 셉니다.
    private long _jsonSerializationsThisSecond;
    // 현재 연결된 TCP 클라이언트 수를 셉니다.
    private int _currentTcpConnections;
    // 각 방의 입력과 규칙을 처리하는 틱 간격을 50ms로 정합니다.
    private static readonly TimeSpan RoomTickInterval = TimeSpan.FromMilliseconds(50);
    // 누적 이동 결과를 UDP로 방송하는 간격을 100ms로 정합니다.
    private static readonly TimeSpan UdpMovementBroadcastInterval = TimeSpan.FromMilliseconds(100);
    // 체포와 탈옥 같은 게임 규칙 검사 간격을 100ms로 정합니다.
    private static readonly TimeSpan GameRuleTickInterval = TimeSpan.FromMilliseconds(100);
    // 게임 단계와 남은 시간을 동기화하는 간격을 1초로 정합니다.
    private static readonly TimeSpan GameStateSyncInterval = TimeSpan.FromSeconds(1);
    // 빈 방의 실행 루프를 끝내기 전에 2초 동안 재입장을 기다립니다.
    private static readonly TimeSpan EmptyRoomStopDelay = TimeSpan.FromSeconds(2);
    // 별도 방 정보가 없을 때 사용할 기본 방 ID입니다.
    private const string DefaultRoomId = "default";
    // 플레이어 크기로 시야 거리를 계산할 때 쓰는 배수입니다.
    private const float VisionRangePlayerSizeMultiplier = 2.5f;
    // 경찰 시야 원뿔의 전체 각도를 90도로 정합니다.
    private const float VisionConeAngleDegrees = 90f;
    // 한 게임의 제한 시간을 300초로 정합니다.
    private const int GameDurationSeconds = 300;
    // 체포 시작부터 감옥 이동까지 걸리는 시간을 2초로 정합니다.
    private const double ArrestDurationSeconds = 2d;
    // 탈옥 구조를 완료하는 데 필요한 시간을 3초로 정합니다.
    private const double JailBreakDurationSeconds = 3d;
    // 석방 위치를 감옥 경계에서 떨어뜨릴 추가 거리입니다.
    private const float JailBreakReleaseOffset = 20f;
    // 탈옥 구조 접촉 판정에 더해 주는 허용 거리입니다.
    private const float JailBreakContactTolerance = 90f;
    // 클라이언트가 바꿀 수 없도록 서버가 적용하는 이동 속도입니다.
    private const float ServerPlayerSpeed = 7f;
    // 클라이언트가 바꿀 수 없도록 서버가 적용하는 플레이어 반지름입니다.
    private const float ServerPlayerRadius = 50f;
    // 기존 속도 값을 초당 맵 이동량으로 바꾸는 배수입니다.
    private const float MovementUnitsPerSecondMultiplier = 60f;
    // 마지막 입력 후 250ms가 지나면 이동 입력을 만료시킵니다.
    private static readonly TimeSpan MovementInputTimeout = TimeSpan.FromMilliseconds(250);
    // 운영체제가 대기시킬 TCP 연결 요청 수의 상한입니다.
    private const int TcpListenBacklog = 2048;

    // TCP/UDP 소켓과 방 서비스를 준비합니다.
    // 의존성과 설정값을 받아 게임 네트워크 서버를 초기화합니다.
    public GameNetworkServer(
        // 게임방과 로비 상태를 관리하는 서비스를 주입받습니다.
        GameRoomService gameRoomService,
        // 서버 설정을 읽을 설정 객체를 주입받습니다.
        IConfiguration configuration,
        // 실행 정보와 오류를 남길 로거를 주입받습니다.
        ILogger<GameNetworkServer> logger)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // `_gameRoomService` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _gameRoomService = gameRoomService;
        // `_logger` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _logger = logger;
        // `_roomCommandQueueCapacity` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _roomCommandQueueCapacity = Math.Max(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            256,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            configuration.GetValue("GameNetwork:RoomCommandQueueCapacity", 4096));
        // `_udpPacketsPerSecond` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _udpPacketsPerSecond = Math.Max(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            1d,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            configuration.GetValue("GameNetwork:UdpPacketsPerSecond", 30d));
        // `_udpBurstSize` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _udpBurstSize = Math.Max(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            1d,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            configuration.GetValue("GameNetwork:UdpBurstSize", 20d));
        // 입장, 퇴장, 초기 상태처럼 반드시 도착해야 하는 메시지는 7777번 TCP 포트를 사용합니다.
        // `_tcpListener` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _tcpListener = new TcpListener(IPAddress.Any, 7777);
        // 빠른 전달이 더 중요한 이동 입력은 7778번 UDP 포트를 사용합니다.
        // `_udpClient` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _udpClient = new UdpClient(7778);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 백그라운드 서비스가 시작될 때 TCP/UDP 수신 루프와 메트릭 타이머를 켭니다.
    // 호스팅 서비스가 시작되면 TCP와 UDP 수신 작업을 실행합니다.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        _tcpListener.Start(TcpListenBacklog);
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        _logger.LogInformation(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            "Game network server started. tcp=7777 udp=7778 room_queue_capacity={RoomCommandQueueCapacity} udp_rate={UdpPacketsPerSecond}/s burst={UdpBurstSize}",
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            _roomCommandQueueCapacity,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            _udpPacketsPerSecond,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            _udpBurstSize);

        // `_metricsTimer` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _metricsTimer = new Timer(LogLoadMetricsCallback, null, 1000, 1000);

        // 별도 작업에서 TCP 연결 수락 루프를 시작하며 완료를 기다리지는 않습니다.
        _ = Task.Run(() => AcceptTcpClientsAsync(stoppingToken), stoppingToken);
        // 별도 작업에서 UDP 수신 루프를 시작하며 완료를 기다리지는 않습니다.
        _ = Task.Run(() => ReceiveUdpAsync(stoppingToken), stoppingToken);

        // 비동기 메서드의 시작 작업을 마친 상태로 제어권을 반환합니다.
        await Task.CompletedTask;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 서버가 종료될 때 주기적으로 돌던 타이머를 정리합니다.
    // 서버 종료 시 타이머와 런타임 지표 수집기를 정리합니다.
    public override void Dispose()
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        _metricsTimer?.Dispose();
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        _runtimeMetrics.Dispose();
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        base.Dispose();
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 방 명령 큐에서 꺼낸 입장 명령을 실제 게임 세션에 반영합니다.
    private void HandleRoomJoin(string roomId, GameSession gameSession, JoinRoomCommand command)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `player`에 저장해 아래 코드에서 다시 사용합니다.
        var player = command.Player;
        // 계산 결과를 지역 변수 `playerId`에 저장해 아래 코드에서 다시 사용합니다.
        var playerId = player.Id;

        // 이동에 영향을 주는 값은 Join payload를 신뢰하지 않고 서버가 고정합니다.
        // `player.Speed` 상태에 오른쪽에서 계산한 값을 반영합니다.
        player.Speed = ServerPlayerSpeed;
        // `player.Radius` 상태에 오른쪽에서 계산한 값을 반영합니다.
        player.Radius = ServerPlayerRadius;
        // `player.Angle` 상태에 오른쪽에서 계산한 값을 반영합니다.
        player.Angle = 0f;
        // `player.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
        player.IsMoving = false;

        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        PositionPlayerForRoom(player, gameSession);

        // 계산 결과를 지역 변수 `playerSession`에 저장해 아래 코드에서 다시 사용합니다.
        var playerSession = new PlayerSession
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 플레이어의 TCP 연결 객체를 보관합니다.
            Client = command.Client,
            // 이 연결에 메시지를 쓸 스트림 작성기를 보관합니다.
            Writer = command.Writer,
            // 서버가 관리할 플레이어 상태를 연결 세션에 넣습니다.
            PlayerState = player,
            // UDP 이동 패킷을 연결과 묶는 임의 토큰을 만듭니다.
            MovementSessionToken = Guid.NewGuid().ToString("N")
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        };
        // `gameSession.Sessions[playerId]` 상태에 오른쪽에서 계산한 값을 반영합니다.
        gameSession.Sessions[playerId] = playerSession;
        // `gameSession.HasHadPlayers` 상태에 오른쪽에서 계산한 값을 반영합니다.
        gameSession.HasHadPlayers = true;
        // `gameSession.EmptySinceUtc` 상태에 오른쪽에서 계산한 값을 반영합니다.
        gameSession.EmptySinceUtc = null;
        // `_playerRooms[playerId]` 상태에 오른쪽에서 계산한 값을 반영합니다.
        _playerRooms[playerId] = roomId;
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        _udpRateLimits.TryRemove(playerId, out _);

        // 계산 결과를 지역 변수 `visiblePlayers`에 저장해 아래 코드에서 다시 사용합니다.
        var visiblePlayers = gameSession.Sessions.Values
            // 원본 컬렉션의 각 항목을 필요한 값으로 변환합니다.
            .Select(s => s.PlayerState)
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            .Where(p => p.Role == player.Role ||
                        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                        IsPlayerVisibleToTeam(gameSession.Sessions.Values, player.Role, p))
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var visibleOpponent in visiblePlayers.Where(p => p.Role != player.Role))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            playerSession.VisibleOpponentPlayerIds.Add(visibleOpponent.Id);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 서버 상태 확인에 쓸 실행 정보를 콘솔에 출력합니다.
        Console.WriteLine($"Player Connected [TCP]: {playerId} / room {roomId}");

        // 현재 TCP 연결 하나에 메시지를 전송합니다.
        TrySendTcp(command.Writer, TcpMessageType.MovementSession, playerSession.MovementSessionToken);
        // 메시지 전송에 필요한 객체를 JSON 문자열로 직렬화합니다.
        TrySendTcp(command.Writer, TcpMessageType.InitialState, SerializeForMetrics(visiblePlayers));
        // 서버 상태 확인에 쓸 실행 정보를 콘솔에 출력합니다.
        Console.WriteLine($"{roomId} 방 {player.Role} 역할 {visiblePlayers.Count}명으로 플레이어 초기화!!");

        // 계산 결과를 지역 변수 `syncData`에 저장해 아래 코드에서 다시 사용합니다.
        var syncData = new GameStateSync
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 메시지가 속한 방 ID를 넣습니다.
            RoomId = roomId,
            // 현재 게임 진행 단계를 넣습니다.
            Phase = gameSession.GamePhase,
            // 남은 시작 카운트다운 시간을 넣습니다.
            CountdownTime = gameSession.CountdownTime,
            // 남은 게임 시간을 넣습니다.
            GameTime = gameSession.GameTime,
            // 승리 역할을 넣습니다.
            WinnerRole = gameSession.WinnerRole,
            // 실제 진행된 게임 시간을 넣습니다.
            ElapsedGameTime = gameSession.ElapsedGameTime
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        };
        // 메시지 전송에 필요한 객체를 JSON 문자열로 직렬화합니다.
        TrySendTcp(command.Writer, TcpMessageType.GameState, SerializeForMetrics(syncData));

        // 같은 역할의 TCP 클라이언트들에게 메시지를 방송합니다.
        BroadcastTcpToRole(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            gameSession,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            player.Role,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            TcpMessageType.Joined,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            SerializeForMetrics(player),
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            playerId);
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        RefreshOpponentVisibility(gameSession);
        // 서버 상태 확인에 쓸 실행 정보를 콘솔에 출력합니다.
        Console.WriteLine($"{roomId} 방 {player.Role} 역할에 플레이어 입장 브로드캐스트!!");
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 퇴장 플레이어의 세션과 게임 중간 상태를 모두 정리합니다.
    private void HandleRoomLeave(string roomId, GameSession gameSession, LeaveRoomCommand command)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (!gameSession.Sessions.TryRemove(command.PlayerId, out var removedSession))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            _playerRooms.TryRemove(command.PlayerId, out _);
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        gameSession.JailEntryTimes.TryRemove(command.PlayerId, out _);
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        gameSession.JailBreakStartedAtByRescuer.Remove(command.PlayerId);
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        gameSession.JailBreakProgressByRescuer.Remove(command.PlayerId);
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        gameSession.PendingUdpMovementPlayerIds.Remove(command.PlayerId);
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        _udpRateLimits.TryRemove(command.PlayerId, out _);
        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var arrest in gameSession.ActiveArrestsByRobberId.Values
                     // 조건을 만족하는 항목만 남깁니다.
                     .Where(a => a.RobberId == command.PlayerId || a.PoliceId == command.PlayerId)
                     // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                     .ToList())
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.ActiveArrestsByRobberId.Remove(arrest.RobberId);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        _playerRooms.TryRemove(command.PlayerId, out _);
        // 서버 상태 확인에 쓸 실행 정보를 콘솔에 출력합니다.
        Console.WriteLine($"Player Disconnected: {command.PlayerId} / room {roomId}");

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (TryAbortRandomGameStart(roomId, gameSession, command.PlayerId))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 같은 역할의 TCP 클라이언트들에게 메시지를 방송합니다.
        BroadcastTcpToRole(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            gameSession,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            removedSession.PlayerState.Role,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            TcpMessageType.Left,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            command.PlayerId,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            null);

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var remainingSession in gameSession.Sessions.Values)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (remainingSession.VisibleOpponentPlayerIds.Remove(command.PlayerId))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 현재 TCP 연결 하나에 메시지를 전송합니다.
                TrySendTcp(remainingSession.Writer, TcpMessageType.Left, command.PlayerId);
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 랜덤 매칭 시작 전에 이탈자가 생겼으면 시작을 취소합니다.
    private bool TryAbortRandomGameStart(string roomId, GameSession gameSession, string leavingPlayerId)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (gameSession.GamePhase is not (GamePhase.Waiting or GamePhase.Countdown) ||
            // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
            string.Equals(roomId, DefaultRoomId, StringComparison.Ordinal))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
            return false;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `roomStatus`에 저장해 아래 코드에서 다시 사용합니다.
        var roomStatus = _gameRoomService.GetRoomStatus(roomId);
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (!roomStatus.Success || roomStatus.IsPrivate || !roomStatus.Matched)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
            return false;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `resetStatus`에 저장해 아래 코드에서 다시 사용합니다.
        var resetStatus = _gameRoomService.AbortRandomGameStart(roomId, leavingPlayerId);
        // `gameSession.GamePhase` 상태에 오른쪽에서 계산한 값을 반영합니다.
        gameSession.GamePhase = GamePhase.Rematching;
        // `gameSession.CountdownTime` 상태에 오른쪽에서 계산한 값을 반영합니다.
        gameSession.CountdownTime = 0;
        // `gameSession.GameStartedAtUtc` 상태에 오른쪽에서 계산한 값을 반영합니다.
        gameSession.GameStartedAtUtc = null;
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        ClearJailBreakProgress(gameSession, roomId);

        // 계산 결과를 지역 변수 `syncData`에 저장해 아래 코드에서 다시 사용합니다.
        var syncData = new GameStateSync
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 메시지가 속한 방 ID를 넣습니다.
            RoomId = roomId,
            // 현재 게임 진행 단계를 넣습니다.
            Phase = GamePhase.Rematching,
            // 남은 시작 카운트다운 시간을 넣습니다.
            CountdownTime = 0,
            // 남은 게임 시간을 넣습니다.
            GameTime = gameSession.GameTime,
            // 승리 역할을 넣습니다.
            WinnerRole = null,
            // 실제 진행된 게임 시간을 넣습니다.
            ElapsedGameTime = 0
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        };

        // 메시지 전송에 필요한 객체를 JSON 문자열로 직렬화합니다.
        BroadcastTcp(gameSession, TcpMessageType.GameState, SerializeForMetrics(syncData), null);
        // 서버 상태 확인에 쓸 실행 정보를 콘솔에 출력합니다.
        Console.WriteLine(
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            $"Random game start aborted: {roomId}, leaving player {leavingPlayerId}, remaining lobby players {resetStatus.CurrentCount}");
        // 조건이 성립했음을 호출자에게 돌려줍니다.
        return true;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // UDP 이동 입력의 신원, 순서와 값을 검사해 세션에 저장합니다.
    private void HandleRoomMove(string roomId, GameSession gameSession, MoveRoomCommand command)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `input`에 저장해 아래 코드에서 다시 사용합니다.
        var input = command.Input;
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (!gameSession.Sessions.TryGetValue(input.Id, out var session))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (!string.Equals(input.Token, session.MovementSessionToken, StringComparison.Ordinal))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (session.UdpEndPoint == null)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // `session.UdpEndPoint` 상태에 오른쪽에서 계산한 값을 반영합니다.
            session.UdpEndPoint = command.RemoteEndPoint;
            // 서버 상태 확인에 쓸 실행 정보를 콘솔에 출력합니다.
            Console.WriteLine($"UDP Endpoint registered for {input.Id} / room {roomId}: {command.RemoteEndPoint}");
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 앞 조건이 거짓이고 이 조건이 참일 때 다음 블록을 실행합니다.
        else if (!session.UdpEndPoint.Equals(command.RemoteEndPoint))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (!float.IsFinite(input.X) || !float.IsFinite(input.Y))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 공유 카운터를 여러 스레드가 안전하게 1 증가시킵니다.
            Interlocked.Increment(ref _udpPacketsInvalidThisSecond);
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (input.Sequence <= session.LastMovementInputSequence)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 공유 카운터를 여러 스레드가 안전하게 1 증가시킵니다.
            Interlocked.Increment(ref _udpPacketsDuplicateOrLateThisSecond);
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `length`에 저장해 아래 코드에서 다시 사용합니다.
        var length = MathF.Sqrt((input.X * input.X) + (input.Y * input.Y));
        // `session.InputX` 상태에 오른쪽에서 계산한 값을 반영합니다.
        session.InputX = length > 1f ? input.X / length : input.X;
        // `session.InputY` 상태에 오른쪽에서 계산한 값을 반영합니다.
        session.InputY = length > 1f ? input.Y / length : input.Y;
        // `session.LastMovementInputSequence` 상태에 오른쪽에서 계산한 값을 반영합니다.
        session.LastMovementInputSequence = input.Sequence;
        // `session.LastMovementInputAtUtc` 상태에 오른쪽에서 계산한 값을 반영합니다.
        session.LastMovementInputAtUtc = DateTime.UtcNow;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 마지막으로 받은 조이스틱 입력을 사용해 좌표, 속도, 각도, 충돌을 서버가 계산합니다.
    // 저장된 입력으로 서버 권위 좌표와 충돌 결과를 계산합니다.
    private void SimulateAuthoritativeMovement(GameSession gameSession, TimeSpan elapsed, DateTime now)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `deltaSeconds`에 저장해 아래 코드에서 다시 사용합니다.
        var deltaSeconds = Math.Clamp((float)elapsed.TotalSeconds, 0f, 0.1f);

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var session in gameSession.Sessions.Values)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 계산 결과를 지역 변수 `player`에 저장해 아래 코드에서 다시 사용합니다.
            var player = session.PlayerState;
            // 계산 결과를 지역 변수 `wasMoving`에 저장해 아래 코드에서 다시 사용합니다.
            var wasMoving = player.IsMoving;
            // 계산 결과를 지역 변수 `inputExpired`에 저장해 아래 코드에서 다시 사용합니다.
            var inputExpired = now - session.LastMovementInputAtUtc > MovementInputTimeout;

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (gameSession.GamePhase != GamePhase.Playing ||
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                IsPlayerMovementLocked(gameSession, player) || inputExpired)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // `session.InputX` 상태에 오른쪽에서 계산한 값을 반영합니다.
                session.InputX = 0f;
                // `session.InputY` 상태에 오른쪽에서 계산한 값을 반영합니다.
                session.InputY = 0f;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 계산 결과를 지역 변수 `hasInput`에 저장해 아래 코드에서 다시 사용합니다.
            var hasInput = MathF.Abs(session.InputX) > 0.001f || MathF.Abs(session.InputY) > 0.001f;
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (!hasInput)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // `player.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
                player.IsMoving = false;
                // 조건이 참인 경우에만 다음 블록을 실행합니다.
                if (wasMoving)
                // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
                {
                    // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                    gameSession.PendingUdpMovementPlayerIds.Add(player.Id);
                    // 현재 코드 블록 또는 객체 초기화를 닫습니다.
                }
                // 현재 반복의 나머지를 건너뛰고 다음 항목으로 넘어갑니다.
                continue;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 계산 결과를 지역 변수 `distance`에 저장해 아래 코드에서 다시 사용합니다.
            var distance = player.Speed * MovementUnitsPerSecondMultiplier * deltaSeconds;
            // 계산 결과를 지역 변수 `nextX`에 저장해 아래 코드에서 다시 사용합니다.
            var nextX = Math.Clamp(player.X + session.InputX * distance, player.Radius, _map.Width - player.Radius);
            // 계산 결과를 지역 변수 `nextY`에 저장해 아래 코드에서 다시 사용합니다.
            var nextY = Math.Clamp(player.Y + session.InputY * distance, player.Radius, _map.Height - player.Radius);
            // 계산 결과를 지역 변수 `moved`에 저장해 아래 코드에서 다시 사용합니다.
            var moved = false;

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (!IsMovementPositionBlocked(nextX, player.Y, player.Radius, session.NearbyCollisionObstacles))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // `moved` 상태에 오른쪽에서 계산한 값을 반영합니다.
                moved |= MathF.Abs(nextX - player.X) > 0.001f;
                // `player.X` 상태에 오른쪽에서 계산한 값을 반영합니다.
                player.X = nextX;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (!IsMovementPositionBlocked(player.X, nextY, player.Radius, session.NearbyCollisionObstacles))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // `moved` 상태에 오른쪽에서 계산한 값을 반영합니다.
                moved |= MathF.Abs(nextY - player.Y) > 0.001f;
                // `player.Y` 상태에 오른쪽에서 계산한 값을 반영합니다.
                player.Y = nextY;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // `player.Angle` 상태에 오른쪽에서 계산한 값을 반영합니다.
            player.Angle = MathF.Atan2(session.InputY, session.InputX) * 180f / MathF.PI - 90f;
            // `player.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
            player.IsMoving = moved;
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            RefreshJailEntry(gameSession, player);
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.PendingUdpMovementPlayerIds.Add(player.Id);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 후보 좌표가 맵 또는 장애물과 충돌하는지 묻습니다.
    private bool IsMovementPositionBlocked(float x, float y, float radius, List<Obstacle> nearbyObstacles)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return _map.IsMovementPositionBlocked(x, y, radius, nearbyObstacles);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 한 틱 동안 바뀐 플레이어 위치를 모아 UDP로 방송합니다.
    private void FlushPendingUdpMovementBroadcasts(GameSession gameSession)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (gameSession.PendingUdpMovementPlayerIds.Count == 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `playerIds`에 저장해 아래 코드에서 다시 사용합니다.
        var playerIds = gameSession.PendingUdpMovementPlayerIds.ToList();
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        gameSession.PendingUdpMovementPlayerIds.Clear();
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        RefreshOpponentVisibility(gameSession);

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var playerId in playerIds)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (!gameSession.Sessions.TryGetValue(playerId, out var session))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 현재 반복의 나머지를 건너뛰고 다음 항목으로 넘어갑니다.
                continue;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 메시지 전송에 필요한 객체를 JSON 문자열로 직렬화합니다.
            var authoritativePlayerJson = SerializeForMetrics(PlayerMovementSync.FromPlayer(session.PlayerState));
            // 계산 결과를 지역 변수 `authoritativeBuffer`에 저장해 아래 코드에서 다시 사용합니다.
            var authoritativeBuffer = System.Text.Encoding.UTF8.GetBytes(authoritativePlayerJson);
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            BroadcastUdpToVisiblePlayers(gameSession, session.PlayerState, authoritativeBuffer);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 방에 입장한 플레이어를 역할별 시작 위치에 배치합니다.
    // 역할과 기존 인원수에 따라 입장 위치를 정합니다.
    private void PositionPlayerForRoom(Player player, GameSession gameSession)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `policeCount`에 저장해 아래 코드에서 다시 사용합니다.
        var policeCount = gameSession.Sessions.Values.Count(s => s.PlayerState.Role == PlayerRole.Police);
        // 계산 결과를 지역 변수 `robberCount`에 저장해 아래 코드에서 다시 사용합니다.
        var robberCount = gameSession.Sessions.Values.Count(s => s.PlayerState.Role == PlayerRole.Robber);
        // 계산 결과를 지역 변수 `gap`에 저장해 아래 코드에서 다시 사용합니다.
        const float gap = 150f;

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (player.Role == PlayerRole.Police)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 계산 결과를 지역 변수 `startX`에 저장해 아래 코드에서 다시 사용합니다.
            var startX = _map.PoliceStation.Center.X - (gap / 2f);
            // `player.X` 상태에 오른쪽에서 계산한 값을 반영합니다.
            player.X = startX + policeCount * gap;
            // 경찰 시작 위치는 콘셉트 맵에서 가져온 경찰차 충돌 영역보다 아래쪽의 앞 도로로 잡습니다.
            // `player.Y` 상태에 오른쪽에서 계산한 값을 반영합니다.
            player.Y = _map.PoliceStation.RightBottom.Y + 350f;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 앞 조건들이 성립하지 않았을 때 다음 블록을 실행합니다.
        else
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 계산 결과를 지역 변수 `startX`에 저장해 아래 코드에서 다시 사용합니다.
            var startX = _map.Width / 2f - gap * 1.5f;
            // `player.X` 상태에 오른쪽에서 계산한 값을 반영합니다.
            player.X = startX + robberCount * gap;
            // `player.Y` 상태에 오른쪽에서 계산한 값을 반영합니다.
            player.Y = _map.Height / 2f;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 완료 시간이 지난 체포를 감옥 이동으로 확정합니다.
    // 완료 시간이 된 체포를 확정해 도둑을 감옥으로 옮깁니다.
    private void CompletePendingArrests(GameSession gameSession)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `now`에 저장해 아래 코드에서 다시 사용합니다.
        var now = DateTime.UtcNow;
        // 계산 결과를 지역 변수 `completedArrests`에 저장해 아래 코드에서 다시 사용합니다.
        var completedArrests = gameSession.ActiveArrestsByRobberId.Values
            // 조건을 만족하는 항목만 남깁니다.
            .Where(a => a.CompletesAtUtc <= now)
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var arrest in completedArrests)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (!gameSession.Sessions.TryGetValue(arrest.RobberId, out var robberSession))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                gameSession.ActiveArrestsByRobberId.Remove(arrest.RobberId);
                // 현재 반복의 나머지를 건너뛰고 다음 항목으로 넘어갑니다.
                continue;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 계산 결과를 지역 변수 `robber`에 저장해 아래 코드에서 다시 사용합니다.
            var robber = robberSession.PlayerState;
            // 계산 결과를 지역 변수 `jailPosition`에 저장해 아래 코드에서 다시 사용합니다.
            var jailPosition = GetJailHoldingPosition(robber, gameSession);
            // `robber.X` 상태에 오른쪽에서 계산한 값을 반영합니다.
            robber.X = jailPosition.X;
            // `robber.Y` 상태에 오른쪽에서 계산한 값을 반영합니다.
            robber.Y = jailPosition.Y;
            // `robber.Angle` 상태에 오른쪽에서 계산한 값을 반영합니다.
            robber.Angle = 0f;
            // `robber.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
            robber.IsMoving = false;

            // `gameSession.JailEntryTimes[robber.Id]` 상태에 오른쪽에서 계산한 값을 반영합니다.
            gameSession.JailEntryTimes[robber.Id] = now;
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.ActiveArrestsByRobberId.Remove(robber.Id);

            // 서버가 계산한 플레이어 상태를 관련 클라이언트에 방송합니다.
            BroadcastPlayerState(gameSession, robber);

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (gameSession.Sessions.TryGetValue(arrest.PoliceId, out var policeSession))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // `policeSession.PlayerState.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
                policeSession.PlayerState.IsMoving = false;
                // 서버가 계산한 플레이어 상태를 관련 클라이언트에 방송합니다.
                BroadcastPlayerState(gameSession, policeSession.PlayerState);
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 경찰 시야 안에 들어온 도둑을 찾아 체포를 시작합니다.
    // 경찰의 시야와 장애물을 검사해 체포 대상을 찾습니다.
    private void DetectRobbersForArrest(GameSession gameSession)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `policePlayers`에 저장해 아래 코드에서 다시 사용합니다.
        var policePlayers = gameSession.Sessions.Values
            // 원본 컬렉션의 각 항목을 필요한 값으로 변환합니다.
            .Select(s => s.PlayerState)
            // 조건을 만족하는 항목만 남깁니다.
            .Where(p => p.Role == PlayerRole.Police && !IsPlayerInActiveArrest(gameSession, p.Id))
            // 지정한 값을 기준으로 오름차순 정렬을 시작합니다.
            .OrderBy(p => p.Id)
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 계산 결과를 지역 변수 `robbers`에 저장해 아래 코드에서 다시 사용합니다.
        var robbers = gameSession.Sessions.Values
            // 원본 컬렉션의 각 항목을 필요한 값으로 변환합니다.
            .Select(s => s.PlayerState)
            // 조건을 만족하는 항목만 남깁니다.
            .Where(p => p.Role == PlayerRole.Robber)
            // 지정한 값을 기준으로 오름차순 정렬을 시작합니다.
            .OrderBy(p => p.Id)
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var police in policePlayers)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
            foreach (var robber in robbers)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 참인 경우에만 다음 블록을 실행합니다.
                if (IsInJail(robber) || gameSession.ActiveArrestsByRobberId.ContainsKey(robber.Id))
                // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
                {
                    // 현재 반복의 나머지를 건너뛰고 다음 항목으로 넘어갑니다.
                    continue;
                    // 현재 코드 블록 또는 객체 초기화를 닫습니다.
                }

                // 계산 결과를 지역 변수 `robberBush`에 저장해 아래 코드에서 다시 사용합니다.
                var robberBush = _map.FindBushContainingPoint(robber.X, robber.Y);
                // 조건이 참인 경우에만 다음 블록을 실행합니다.
                if (robberBush != null && !GameMap.ContainsPoint(robberBush, police.X, police.Y))
                // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
                {
                    // 현재 반복의 나머지를 건너뛰고 다음 항목으로 넘어갑니다.
                    continue;
                    // 현재 코드 블록 또는 객체 초기화를 닫습니다.
                }

                // 조건이 참인 경우에만 다음 블록을 실행합니다.
                if (IsPointInVision(police, robber.X, robber.Y) &&
                    // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                    !IsVisionBlockedByObstacle(police, robber))
                // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
                {
                    // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                    StartArrest(gameSession, police, robber);
                    // 현재 반복문을 끝냅니다.
                    break;
                    // 현재 코드 블록 또는 객체 초기화를 닫습니다.
                }
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 경찰과 도둑을 멈추고 일정 시간 뒤 완료될 체포 상태를 등록합니다.
    // 경찰과 도둑을 멈추고 완료 예정인 체포 상태를 등록합니다.
    private void StartArrest(GameSession gameSession, Player police, Player robber)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `now`에 저장해 아래 코드에서 다시 사용합니다.
        var now = DateTime.UtcNow;
        // `gameSession.ActiveArrestsByRobberId[robber.Id]` 상태에 오른쪽에서 계산한 값을 반영합니다.
        gameSession.ActiveArrestsByRobberId[robber.Id] = new ArrestState
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 체포를 시작한 경찰 ID를 기록합니다.
            PoliceId = police.Id,
            // 체포 또는 석방 대상 도둑 ID를 기록합니다.
            RobberId = robber.Id,
            // 체포가 완료될 UTC 시각을 기록합니다.
            CompletesAtUtc = now.AddSeconds(ArrestDurationSeconds)
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        };

        // `police.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
        police.IsMoving = false;
        // `robber.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
        robber.IsMoving = false;

        // 방 안의 TCP 클라이언트들에게 메시지를 방송합니다.
        BroadcastTcp(gameSession, TcpMessageType.Arrested, $"{police.Id},{robber.Id}", null);
        // 서버가 계산한 플레이어 상태를 관련 클라이언트에 방송합니다.
        BroadcastPlayerState(gameSession, police);
        // 서버가 계산한 플레이어 상태를 관련 클라이언트에 방송합니다.
        BroadcastPlayerState(gameSession, robber);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 감옥 안에서 도둑들이 겹치지 않도록 수용 위치를 계산합니다.
    // 수감된 도둑끼리 겹치지 않을 감옥 내부 좌표를 계산합니다.
    private (float X, float Y) GetJailHoldingPosition(Player robber, GameSession gameSession)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `robbers`에 저장해 아래 코드에서 다시 사용합니다.
        var robbers = gameSession.Sessions.Values
            // 원본 컬렉션의 각 항목을 필요한 값으로 변환합니다.
            .Select(s => s.PlayerState)
            // 조건을 만족하는 항목만 남깁니다.
            .Where(p => p.Role == PlayerRole.Robber)
            // 지정한 값을 기준으로 오름차순 정렬을 시작합니다.
            .OrderBy(p => p.Id)
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 계산 결과를 지역 변수 `index`에 저장해 아래 코드에서 다시 사용합니다.
        var index = robbers.FindIndex(p => p.Id == robber.Id);
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (index < 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // `index` 상태에 오른쪽에서 계산한 값을 반영합니다.
            index = 0;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `columns`에 저장해 아래 코드에서 다시 사용합니다.
        const int columns = 3;
        // 계산 결과를 지역 변수 `gap`에 저장해 아래 코드에서 다시 사용합니다.
        const float gap = 150f;
        // 계산 결과를 지역 변수 `rows`에 저장해 아래 코드에서 다시 사용합니다.
        var rows = Math.Max(1, (int)Math.Ceiling(robbers.Count / (double)columns));
        // 계산 결과를 지역 변수 `column`에 저장해 아래 코드에서 다시 사용합니다.
        var column = index % columns;
        // 계산 결과를 지역 변수 `row`에 저장해 아래 코드에서 다시 사용합니다.
        var row = index / columns;
        // 계산 결과를 지역 변수 `offsetX`에 저장해 아래 코드에서 다시 사용합니다.
        var offsetX = (column - ((columns - 1) / 2f)) * gap;
        // 계산 결과를 지역 변수 `offsetY`에 저장해 아래 코드에서 다시 사용합니다.
        var offsetY = (row - ((rows - 1) / 2f)) * gap;
        // 계산 결과를 지역 변수 `jailBounds`에 저장해 아래 코드에서 다시 사용합니다.
        var jailBounds = GameMap.GetBuildingCollisionBounds(_map.Jail);
        // 계산 결과를 지역 변수 `minX`에 저장해 아래 코드에서 다시 사용합니다.
        var minX = jailBounds.Left + robber.Radius;
        // 계산 결과를 지역 변수 `maxX`에 저장해 아래 코드에서 다시 사용합니다.
        var maxX = jailBounds.Right - robber.Radius;
        // 계산 결과를 지역 변수 `minY`에 저장해 아래 코드에서 다시 사용합니다.
        var minY = jailBounds.Top + robber.Radius;
        // 계산 결과를 지역 변수 `maxY`에 저장해 아래 코드에서 다시 사용합니다.
        var maxY = jailBounds.Bottom - robber.Radius;

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return (
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            Math.Clamp(_map.Jail.CollisionCenter.X + offsetX, minX, maxX),
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            Math.Clamp(_map.Jail.CollisionCenter.Y + offsetY, minY, maxY));
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 감옥 근처에서 구조 중인 도둑들의 탈옥 진행률을 갱신하고 완료 시 석방합니다.
    // 구조 조건을 만족한 도둑별 탈옥 진행률을 갱신합니다.
    private void UpdateJailBreakProgress(string roomId, GameSession gameSession)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `now`에 저장해 아래 코드에서 다시 사용합니다.
        var now = DateTime.UtcNow;
        // 계산 결과를 지역 변수 `jailedRobberCount`에 저장해 아래 코드에서 다시 사용합니다.
        var jailedRobberCount = gameSession.Sessions.Values
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            .Count(s => s.PlayerState.Role == PlayerRole.Robber && IsInJail(s.PlayerState));

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (jailedRobberCount == 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            ClearJailBreakProgress(gameSession, roomId);
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `activeRescuers`에 저장해 아래 코드에서 다시 사용합니다.
        var activeRescuers = gameSession.Sessions.Values
            // 원본 컬렉션의 각 항목을 필요한 값으로 변환합니다.
            .Select(s => s.PlayerState)
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            .Where(p => p.Role == PlayerRole.Robber &&
                        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                        !IsInJail(p) &&
                        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                        !IsPlayerInActiveArrest(gameSession, p.Id) &&
                        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                        IsTouchingOrNearJail(p))
            // 지정한 값을 기준으로 오름차순 정렬을 시작합니다.
            .OrderBy(p => p.Id)
            // 필요한 개수만큼만 앞에서 가져옵니다.
            .Take(jailedRobberCount)
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (activeRescuers.Count == 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            ClearJailBreakProgress(gameSession, roomId);
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 열거 가능한 값을 빠른 포함 검사에 쓸 집합으로 만듭니다.
        var activeRescuerIds = activeRescuers.Select(p => p.Id).ToHashSet();
        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var rescuerId in gameSession.JailBreakStartedAtByRescuer.Keys
                     // 조건을 만족하는 항목만 남깁니다.
                     .Where(id => !activeRescuerIds.Contains(id))
                     // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                     .ToList())
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailBreakStartedAtByRescuer.Remove(rescuerId);
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailBreakProgressByRescuer.Remove(rescuerId);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var rescuer in activeRescuers)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (!gameSession.JailBreakStartedAtByRescuer.TryGetValue(rescuer.Id, out var startedAt))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // `startedAt` 상태에 오른쪽에서 계산한 값을 반영합니다.
                startedAt = now;
                // `gameSession.JailBreakStartedAtByRescuer[rescuer.Id]` 상태에 오른쪽에서 계산한 값을 반영합니다.
                gameSession.JailBreakStartedAtByRescuer[rescuer.Id] = startedAt;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 계산 결과를 지역 변수 `elapsedSeconds`에 저장해 아래 코드에서 다시 사용합니다.
            var elapsedSeconds = (now - startedAt).TotalSeconds;
            // `gameSession.JailBreakProgressByRescuer[rescuer.Id]` 상태에 오른쪽에서 계산한 값을 반영합니다.
            gameSession.JailBreakProgressByRescuer[rescuer.Id] =
                // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                Math.Clamp((float)(elapsedSeconds / JailBreakDurationSeconds), 0f, 1f);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `readyRescuers`에 저장해 아래 코드에서 다시 사용합니다.
        var readyRescuers = activeRescuers
            // 조건을 만족하는 항목만 남깁니다.
            .Where(p => gameSession.JailBreakProgressByRescuer.TryGetValue(p.Id, out var progress) && progress >= 1f)
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (readyRescuers.Count > 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            ReleaseJailedRobbers(roomId, gameSession, readyRescuers, now);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        BroadcastJailBreakProgress(gameSession, roomId);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 구조 조건이 깨졌을 때 탈옥 진행 상태를 초기화하고 클라이언트에 알립니다.
    // 구조 조건이 깨지면 진행 중인 탈옥 상태를 초기화합니다.
    private void ClearJailBreakProgress(GameSession gameSession, string roomId)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (gameSession.JailBreakStartedAtByRescuer.Count == 0 &&
            // `gameSession.JailBreakProgressByRescuer.Count` 상태에 오른쪽에서 계산한 값을 반영합니다.
            gameSession.JailBreakProgressByRescuer.Count == 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        gameSession.JailBreakStartedAtByRescuer.Clear();
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        gameSession.JailBreakProgressByRescuer.Clear();
        // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
        BroadcastJailBreakProgress(gameSession, roomId);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 탈옥 진행을 완료한 구조자 수만큼 오래 갇힌 도둑을 감옥 밖으로 보냅니다.
    // 준비된 구조자 수만큼 오래 수감된 도둑을 석방합니다.
    private void ReleaseJailedRobbers(
        // 처리할 방을 식별하는 ID를 받습니다.
        string roomId,
        // 해당 방의 실시간 게임 상태를 받습니다.
        GameSession gameSession,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        List<Player> readyRescuers,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        DateTime now)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `targetSessions`에 저장해 아래 코드에서 다시 사용합니다.
        var targetSessions = gameSession.Sessions.Values
            // 조건을 만족하는 항목만 남깁니다.
            .Where(s => s.PlayerState.Role == PlayerRole.Robber && IsInJail(s.PlayerState))
            // 원본 컬렉션의 각 항목을 필요한 값으로 변환합니다.
            .Select(s => new
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 정렬 후 다시 사용할 플레이어 세션을 담습니다.
                Session = s,
                // 감옥 입장 시각을 담고 기록이 없으면 현재 시각으로 만듭니다.
                EnteredAt = gameSession.JailEntryTimes.GetOrAdd(s.PlayerState.Id, now)
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
            })
            // 지정한 값을 기준으로 오름차순 정렬을 시작합니다.
            .OrderBy(s => s.EnteredAt)
            // 앞 정렬 결과가 같을 때 사용할 두 번째 정렬 기준을 지정합니다.
            .ThenBy(s => s.Session.PlayerState.Id)
            // 필요한 개수만큼만 앞에서 가져옵니다.
            .Take(readyRescuers.Count)
            // 지연 실행 중인 조회 결과를 지금 목록으로 만듭니다.
            .ToList();

        // 초깃값부터 종료 조건까지 인덱스를 바꾸며 다음 블록을 반복합니다.
        for (var i = 0; i < targetSessions.Count; i++)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 계산 결과를 지역 변수 `target`에 저장해 아래 코드에서 다시 사용합니다.
            var target = targetSessions[i].Session.PlayerState;
            // 계산 결과를 지역 변수 `releasePosition`에 저장해 아래 코드에서 다시 사용합니다.
            var releasePosition = GetJailReleasePosition(target.Radius, i);

            // `target.X` 상태에 오른쪽에서 계산한 값을 반영합니다.
            target.X = releasePosition.X;
            // `target.Y` 상태에 오른쪽에서 계산한 값을 반영합니다.
            target.Y = releasePosition.Y;
            // `target.Angle` 상태에 오른쪽에서 계산한 값을 반영합니다.
            target.Angle = 0f;
            // `target.IsMoving` 상태에 오른쪽에서 계산한 값을 반영합니다.
            target.IsMoving = false;
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailEntryTimes.TryRemove(target.Id, out _);

            // 계산 결과를 지역 변수 `syncData`에 저장해 아래 코드에서 다시 사용합니다.
            var syncData = new JailBreakSync
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 메시지가 속한 방 ID를 넣습니다.
                RoomId = roomId,
                // 탈옥을 완료한 구조자 ID를 넣습니다.
                RescuerId = readyRescuers[Math.Min(i, readyRescuers.Count - 1)].Id,
                // 체포 또는 석방 대상 도둑 ID를 기록합니다.
                RobberId = target.Id,
                // 동기화할 X 좌표를 넣습니다.
                X = target.X,
                // 동기화할 Y 좌표를 넣습니다.
                Y = target.Y
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            };

            // 같은 역할의 TCP 클라이언트들에게 메시지를 방송합니다.
            BroadcastTcpToRole(
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                gameSession,
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                PlayerRole.Robber,
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                TcpMessageType.JailBreak,
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                SerializeForMetrics(syncData),
                // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                null);
            // 서버가 계산한 플레이어 상태를 관련 클라이언트에 방송합니다.
            BroadcastPlayerState(gameSession, target);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var rescuer in readyRescuers)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailBreakStartedAtByRescuer.Remove(rescuer.Id);
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailBreakProgressByRescuer.Remove(rescuer.Id);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `remainingJailedRobbers`에 저장해 아래 코드에서 다시 사용합니다.
        var remainingJailedRobbers = gameSession.Sessions.Values
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            .Count(s => s.PlayerState.Role == PlayerRole.Robber && IsInJail(s.PlayerState));

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (remainingJailedRobbers == 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailBreakStartedAtByRescuer.Clear();
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailBreakProgressByRescuer.Clear();
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 현재 구조자별 탈옥 진행률을 같은 도둑 역할의 TCP 클라이언트에 보냅니다.
    // 현재 탈옥 진행률을 같은 도둑 팀에게 보냅니다.
    private void BroadcastJailBreakProgress(GameSession gameSession, string roomId)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `syncData`에 저장해 아래 코드에서 다시 사용합니다.
        var syncData = new JailBreakProgressSync
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 메시지가 속한 방 ID를 넣습니다.
            RoomId = roomId,
            // 구조자 ID별 현재 진행률 사본을 넣습니다.
            ProgressByRescuer = new Dictionary<string, float>(gameSession.JailBreakProgressByRescuer)
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        };

        // 같은 역할의 TCP 클라이언트들에게 메시지를 방송합니다.
        BroadcastTcpToRole(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            gameSession,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            PlayerRole.Robber,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            TcpMessageType.JailBreakProgress,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            SerializeForMetrics(syncData),
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            null);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 도둑의 현재 위치를 기준으로 감옥 입장 시간 기록을 추가하거나 제거합니다.
    // 도둑 위치에 맞춰 감옥 입장 시각 기록을 추가하거나 지웁니다.
    private void RefreshJailEntry(GameSession gameSession, Player player)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (player.Role != PlayerRole.Robber)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 현재 메서드 실행을 즉시 끝내고 호출한 곳으로 돌아갑니다.
            return;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (IsInJail(player))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailEntryTimes.TryAdd(player.Id, DateTime.UtcNow);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 앞 조건들이 성립하지 않았을 때 다음 블록을 실행합니다.
        else
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            gameSession.JailEntryTimes.TryRemove(player.Id, out _);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 플레이어가 현재 체포 중인 경찰이나 도둑인지 확인합니다.
    // 플레이어가 현재 체포 과정에 참여 중인지 확인합니다.
    private static bool IsPlayerInActiveArrest(GameSession gameSession, string playerId)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        return gameSession.ActiveArrestsByRobberId.ContainsKey(playerId) ||
               // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
               gameSession.ActiveArrestsByRobberId.Values.Any(a => a.PoliceId == playerId);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 체포 중이거나 감옥에 갇혀 있어 이동을 막아야 하는 플레이어인지 확인합니다.
    // 체포 또는 수감 때문에 이동을 막아야 하는지 확인합니다.
    private bool IsPlayerMovementLocked(GameSession gameSession, Player player)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        return IsPlayerInActiveArrest(gameSession, player.Id) ||
               // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
               (player.Role == PlayerRole.Robber && IsInJail(player));
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 플레이어의 현재 좌표가 감옥 사각형 안에 있는지 확인합니다.
    // 플레이어 중심점이 감옥 충돌 영역 안인지 확인합니다.
    private bool IsInJail(Player player)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return GameMap.IsPointInBuilding(player.X, player.Y, _map.Jail);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 특정 좌표가 플레이어의 시야 거리와 시야각 안에 들어오는지 계산합니다.
    // 대상 좌표가 플레이어의 시야 거리와 각도 안인지 계산합니다.
    private static bool IsPointInVision(Player player, float x, float y)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `dx`에 저장해 아래 코드에서 다시 사용합니다.
        var dx = x - player.X;
        // 계산 결과를 지역 변수 `dy`에 저장해 아래 코드에서 다시 사용합니다.
        var dy = y - player.Y;
        // 계산 결과를 지역 변수 `distanceSquared`에 저장해 아래 코드에서 다시 사용합니다.
        var distanceSquared = dx * dx + dy * dy;
        // 계산 결과를 지역 변수 `visionRange`에 저장해 아래 코드에서 다시 사용합니다.
        var visionRange = GetVisionRange(player);

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (distanceSquared > visionRange * visionRange)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
            return false;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `targetAngle`에 저장해 아래 코드에서 다시 사용합니다.
        var targetAngle = NormalizeDegrees((float)(Math.Atan2(dy, dx) * 180f / Math.PI));
        // 계산 결과를 지역 변수 `facingAngle`에 저장해 아래 코드에서 다시 사용합니다.
        var facingAngle = GetFacingAngle(player);
        // 계산 결과를 지역 변수 `angleDifference`에 저장해 아래 코드에서 다시 사용합니다.
        var angleDifference = Math.Abs(ShortestAngleDifference(facingAngle, targetAngle));

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return angleDifference <= VisionConeAngleDegrees / 2f;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 경찰과 도둑 사이의 선분을 실제로 가로막는 장애물이 있는지 확인합니다.
    // 경찰과 도둑 사이 선분을 가리는 장애물이 있는지 확인합니다.
    private bool IsVisionBlockedByObstacle(Player police, Player robber)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var building in _map.Buildings)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (building.BlocksVision &&
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                DoesSegmentIntersectBuilding(
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.Y,
                    // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                    building))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립했음을 호출자에게 돌려줍니다.
                return true;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
        foreach (var obstacle in _map.Obstacles)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (!obstacle.BlocksVision)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 현재 반복의 나머지를 건너뛰고 다음 항목으로 넘어갑니다.
                continue;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 경찰이 들어가 있는 부쉬 자체는 경찰과 도둑 사이의 장애물로 보지 않는다.
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (GameMap.IsBushObstacle(obstacle) &&
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                GameMap.ContainsPoint(obstacle, police.X, police.Y))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 현재 반복의 나머지를 건너뛰고 다음 항목으로 넘어갑니다.
                continue;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (obstacle.Type == "Polygon" &&
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                DoesSegmentIntersectPolygon(
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.Y,
                    // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                    obstacle.PolygonPoints))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립했음을 호출자에게 돌려줍니다.
                return true;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (obstacle.Type == "Rect" &&
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                DoesSegmentIntersectRectangle(
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    obstacle.LeftTop.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    obstacle.LeftTop.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    obstacle.RightBottom.X,
                    // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                    obstacle.RightBottom.Y))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립했음을 호출자에게 돌려줍니다.
                return true;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (obstacle.Type == "Circle" &&
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                DoesSegmentIntersectCircle(
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    police.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    robber.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    obstacle.CenterX.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    obstacle.CenterX.Y,
                    // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                    obstacle.Radius))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립했음을 호출자에게 돌려줍니다.
                return true;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
        return false;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 선분이 축에 평행한 사각형과 교차하는지 계산합니다.
    private static bool DoesSegmentIntersectRectangle(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float left,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float top,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float right,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        float bottom)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `directionX`에 저장해 아래 코드에서 다시 사용합니다.
        var directionX = endX - startX;
        // 계산 결과를 지역 변수 `directionY`에 저장해 아래 코드에서 다시 사용합니다.
        var directionY = endY - startY;
        // 계산 결과를 지역 변수 `minimum`에 저장해 아래 코드에서 다시 사용합니다.
        var minimum = 0f;
        // 계산 결과를 지역 변수 `maximum`에 저장해 아래 코드에서 다시 사용합니다.
        var maximum = 1f;

        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        return ClipSegmentToAxis(-directionX, startX - left, ref minimum, ref maximum) &&
               // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
               ClipSegmentToAxis(directionX, right - startX, ref minimum, ref maximum) &&
               // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
               ClipSegmentToAxis(-directionY, startY - top, ref minimum, ref maximum) &&
               // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
               ClipSegmentToAxis(directionY, bottom - startY, ref minimum, ref maximum);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 선분이 건물의 실제 충돌 모양과 교차하는지 계산합니다.
    private static bool DoesSegmentIntersectBuilding(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endY,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        MapBuilding building)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (building.CollisionPolygon.Length >= 3)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 오른쪽 계산 결과를 호출자에게 반환합니다.
            return DoesSegmentIntersectPolygon(
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                startX,
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                startY,
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                endX,
                // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                endY,
                // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                building.CollisionPolygon);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `localStart`에 저장해 아래 코드에서 다시 사용합니다.
        var localStart = GameMap.ToBuildingCollisionLocalPoint(startX, startY, building);
        // 계산 결과를 지역 변수 `localEnd`에 저장해 아래 코드에서 다시 사용합니다.
        var localEnd = GameMap.ToBuildingCollisionLocalPoint(endX, endY, building);
        // 계산 결과를 지역 변수 `halfWidth`에 저장해 아래 코드에서 다시 사용합니다.
        var halfWidth = building.EffectiveCollisionWidth / 2f;
        // 계산 결과를 지역 변수 `halfHeight`에 저장해 아래 코드에서 다시 사용합니다.
        var halfHeight = building.EffectiveCollisionHeight / 2f;

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return DoesSegmentIntersectRectangle(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            localStart.X,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            localStart.Y,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            localEnd.X,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            localEnd.Y,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            -halfWidth,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            -halfHeight,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            halfWidth,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            halfHeight);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 선분이 다각형 내부 또는 모서리와 만나는지 계산합니다.
    private static bool DoesSegmentIntersectPolygon(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endY,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        IReadOnlyList<PointF> polygon)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (polygon.Count < 3)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
            return false;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (IsPointInsidePolygon(startX, startY, polygon) ||
            // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
            IsPointInsidePolygon(endX, endY, polygon))
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립했음을 호출자에게 돌려줍니다.
            return true;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 초깃값부터 종료 조건까지 인덱스를 바꾸며 다음 블록을 반복합니다.
        for (var index = 0; index < polygon.Count; index++)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 계산 결과를 지역 변수 `edgeStart`에 저장해 아래 코드에서 다시 사용합니다.
            var edgeStart = polygon[index];
            // 계산 결과를 지역 변수 `edgeEnd`에 저장해 아래 코드에서 다시 사용합니다.
            var edgeEnd = polygon[(index + 1) % polygon.Count];
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (DoSegmentsIntersect(
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    startX,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    startY,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    endX,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    endY,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    edgeStart.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    edgeStart.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    edgeEnd.X,
                    // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                    edgeEnd.Y))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립했음을 호출자에게 돌려줍니다.
                return true;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
        return false;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 광선 교차 방식으로 점이 다각형 안인지 확인합니다.
    private static bool IsPointInsidePolygon(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float pointX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float pointY,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        IReadOnlyList<PointF> polygon)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `isInside`에 저장해 아래 코드에서 다시 사용합니다.
        var isInside = false;

        // 초깃값부터 종료 조건까지 인덱스를 바꾸며 다음 블록을 반복합니다.
        for (var index = 0; index < polygon.Count; index++)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 계산 결과를 지역 변수 `current`에 저장해 아래 코드에서 다시 사용합니다.
            var current = polygon[index];
            // 계산 결과를 지역 변수 `previous`에 저장해 아래 코드에서 다시 사용합니다.
            var previous = polygon[(index + polygon.Count - 1) % polygon.Count];

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (IsPointOnSegment(
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    pointX,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    pointY,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    previous.X,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    previous.Y,
                    // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                    current.X,
                    // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                    current.Y))
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립했음을 호출자에게 돌려줍니다.
                return true;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if ((current.Y > pointY) != (previous.Y > pointY) &&
                // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                pointX < (previous.X - current.X) * (pointY - current.Y) /
                         // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                         (previous.Y - current.Y) + current.X)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // `isInside` 상태에 오른쪽에서 계산한 값을 반영합니다.
                isInside = !isInside;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return isInside;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 외적을 이용해 두 선분이 서로 만나는지 확인합니다.
    private static bool DoSegmentsIntersect(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float firstStartX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float firstStartY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float firstEndX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float firstEndY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float secondStartX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float secondStartY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float secondEndX,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        float secondEndY)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `firstStartSide`에 저장해 아래 코드에서 다시 사용합니다.
        var firstStartSide = CrossProduct(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstStartX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstStartY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstEndX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstEndY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondStartX,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            secondStartY);
        // 계산 결과를 지역 변수 `firstEndSide`에 저장해 아래 코드에서 다시 사용합니다.
        var firstEndSide = CrossProduct(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstStartX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstStartY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstEndX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstEndY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondEndX,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            secondEndY);
        // 계산 결과를 지역 변수 `secondStartSide`에 저장해 아래 코드에서 다시 사용합니다.
        var secondStartSide = CrossProduct(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondStartX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondStartY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondEndX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondEndY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstStartX,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            firstStartY);
        // 계산 결과를 지역 변수 `secondEndSide`에 저장해 아래 코드에서 다시 사용합니다.
        var secondEndSide = CrossProduct(
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondStartX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondStartY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondEndX,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            secondEndY,
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            firstEndX,
            // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
            firstEndY);

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (firstStartSide * firstEndSide < 0f &&
            // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
            secondStartSide * secondEndSide < 0f)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립했음을 호출자에게 돌려줍니다.
            return true;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        return Math.Abs(firstStartSide) <= 0.001f &&
                   // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                   IsPointOnSegment(secondStartX, secondStartY, firstStartX, firstStartY, firstEndX, firstEndY) ||
               // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
               Math.Abs(firstEndSide) <= 0.001f &&
                   // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                   IsPointOnSegment(secondEndX, secondEndY, firstStartX, firstStartY, firstEndX, firstEndY) ||
               // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
               Math.Abs(secondStartSide) <= 0.001f &&
                   // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
                   IsPointOnSegment(firstStartX, firstStartY, secondStartX, secondStartY, secondEndX, secondEndY) ||
               // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
               Math.Abs(secondEndSide) <= 0.001f &&
                   // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                   IsPointOnSegment(firstEndX, firstEndY, secondStartX, secondStartY, secondEndX, secondEndY);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 점이 방향 선분의 어느 쪽에 있는지 나타내는 2차원 외적을 구합니다.
    private static float CrossProduct(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float pointX,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        float pointY)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return (endX - startX) * (pointY - startY) -
               // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
               (endY - startY) * (pointX - startX);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 점이 주어진 선분 위에 놓여 있는지 확인합니다.
    private static bool IsPointOnSegment(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float pointX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float pointY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endX,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        float endY)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (Math.Abs(CrossProduct(startX, startY, endX, endY, pointX, pointY)) > 0.001f)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
            return false;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        return pointX >= Math.Min(startX, endX) - 0.001f &&
               // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
               pointX <= Math.Max(startX, endX) + 0.001f &&
               // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
               pointY >= Math.Min(startY, endY) - 0.001f &&
               // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
               pointY <= Math.Max(startY, endY) + 0.001f;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 선분의 교차 가능 구간을 한 축의 경계에 맞춰 줄입니다.
    private static bool ClipSegmentToAxis(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float direction,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float distance,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        ref float minimum,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        ref float maximum)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (Math.Abs(direction) < float.Epsilon)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 오른쪽 계산 결과를 호출자에게 반환합니다.
            return distance >= 0f;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `ratio`에 저장해 아래 코드에서 다시 사용합니다.
        var ratio = distance / direction;
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (direction < 0f)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (ratio > maximum)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
                return false;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // `minimum` 상태에 오른쪽에서 계산한 값을 반영합니다.
            minimum = Math.Max(minimum, ratio);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }
        // 앞 조건들이 성립하지 않았을 때 다음 블록을 실행합니다.
        else
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 참인 경우에만 다음 블록을 실행합니다.
            if (ratio < minimum)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
                return false;
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }

            // `maximum` 상태에 오른쪽에서 계산한 값을 반영합니다.
            maximum = Math.Min(maximum, ratio);
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 성립했음을 호출자에게 돌려줍니다.
        return true;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 선분에서 원 중심에 가장 가까운 점으로 교차 여부를 계산합니다.
    private static bool DoesSegmentIntersectCircle(
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float startY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float endY,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float centerX,
        // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
        float centerY,
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        float radius)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `directionX`에 저장해 아래 코드에서 다시 사용합니다.
        var directionX = endX - startX;
        // 계산 결과를 지역 변수 `directionY`에 저장해 아래 코드에서 다시 사용합니다.
        var directionY = endY - startY;
        // 계산 결과를 지역 변수 `segmentLengthSquared`에 저장해 아래 코드에서 다시 사용합니다.
        var segmentLengthSquared = directionX * directionX + directionY * directionY;

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (segmentLengthSquared <= float.Epsilon)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 조건이 성립하지 않았음을 호출자에게 돌려줍니다.
            return false;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 계산 결과를 지역 변수 `projection`에 저장해 아래 코드에서 다시 사용합니다.
        var projection = ((centerX - startX) * directionX + (centerY - startY) * directionY) /
                         // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
                         segmentLengthSquared;
        // `projection` 상태에 오른쪽에서 계산한 값을 반영합니다.
        projection = Math.Clamp(projection, 0f, 1f);

        // 계산 결과를 지역 변수 `closestX`에 저장해 아래 코드에서 다시 사용합니다.
        var closestX = startX + projection * directionX;
        // 계산 결과를 지역 변수 `closestY`에 저장해 아래 코드에서 다시 사용합니다.
        var closestY = startY + projection * directionY;
        // 계산 결과를 지역 변수 `distanceX`에 저장해 아래 코드에서 다시 사용합니다.
        var distanceX = centerX - closestX;
        // 계산 결과를 지역 변수 `distanceY`에 저장해 아래 코드에서 다시 사용합니다.
        var distanceY = centerY - closestY;

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return distanceX * distanceX + distanceY * distanceY <= radius * radius;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 플레이어 크기를 기준으로 시야 거리를 계산합니다.
    // 플레이어 반지름을 기준으로 실제 시야 거리를 구합니다.
    private static float GetVisionRange(Player player)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return player.Radius * 2f * VisionRangePlayerSizeMultiplier;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 플레이어 회전값을 실제 바라보는 방향 각도로 변환합니다.
    // 스프라이트 회전값을 수학적인 진행 방향 각도로 바꿉니다.
    private static float GetFacingAngle(Player player)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return NormalizeDegrees(player.Angle + 90f);
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 각도를 0도 이상 360도 미만 범위로 맞춥니다.
    // 각도를 0도 이상 360도 미만 범위로 맞춥니다.
    private static float NormalizeDegrees(float degrees)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
        degrees %= 360f;
        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (degrees < 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // `degrees` 상태에 오른쪽에서 계산한 값을 반영합니다.
            degrees += 360f;
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return degrees;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 두 각도 사이의 가장 짧은 방향 차이를 -180도부터 180도 범위로 계산합니다.
    // 두 방향 사이의 가장 짧은 각도 차이를 구합니다.
    private static float ShortestAngleDifference(float fromDegrees, float toDegrees)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `difference`에 저장해 아래 코드에서 다시 사용합니다.
        var difference = NormalizeDegrees(toDegrees - fromDegrees);
        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return difference > 180f ? difference - 360f : difference;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 플레이어가 탈옥 구조를 진행할 만큼 감옥에 가까이 붙어 있는지 확인합니다.
    // 도둑이 탈옥 구조가 가능한 감옥 접촉 거리 안인지 확인합니다.
    private bool IsTouchingOrNearJail(Player player)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `allowedDistance`에 저장해 아래 코드에서 다시 사용합니다.
        var allowedDistance = player.Radius + JailBreakContactTolerance;

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return GameMap.GetDistanceSquaredToBuilding(player.X, player.Y, _map.Jail) <=
               // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
               allowedDistance * allowedDistance;
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 감옥 아래쪽에서 장애물과 겹치지 않는 석방 위치를 찾습니다.
    // 장애물과 겹치지 않는 감옥 밖 석방 후보를 찾습니다.
    private (float X, float Y) GetJailReleasePosition(float radius, int releaseIndex)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 계산 결과를 지역 변수 `jail`에 저장해 아래 코드에서 다시 사용합니다.
        var jail = _map.Jail;
        // 계산 결과를 지역 변수 `jailBounds`에 저장해 아래 코드에서 다시 사용합니다.
        var jailBounds = GameMap.GetBuildingCollisionBounds(jail);
        // 계산 결과를 지역 변수 `startY`에 저장해 아래 코드에서 다시 사용합니다.
        var startY = jailBounds.Bottom + radius + JailBreakReleaseOffset;
        // 계산 결과를 지역 변수 `candidates`에 저장해 아래 코드에서 다시 사용합니다.
        var candidates = new List<(float X, float Y)>();
        // 계산 결과를 지역 변수 `rowOffsets`에 저장해 아래 코드에서 다시 사용합니다.
        float[][] rowOffsets =
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 다음 줄에도 같은 조건식이나 호출 인수가 이어집니다.
            new[] { 0f, -jail.EffectiveCollisionWidth / 4f, jail.EffectiveCollisionWidth / 4f, -jail.EffectiveCollisionWidth / 2f + radius, jail.EffectiveCollisionWidth / 2f - radius },
            // 이 C# 문장을 실행해 현재 메서드의 상태나 흐름을 갱신합니다.
            new[] { -jail.EffectiveCollisionWidth / 6f, jail.EffectiveCollisionWidth / 6f, -jail.EffectiveCollisionWidth / 3f, jail.EffectiveCollisionWidth / 3f, 0f }
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        };

        // 초깃값부터 종료 조건까지 인덱스를 바꾸며 다음 블록을 반복합니다.
        for (var row = 0; row < 5; row++)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 계산 결과를 지역 변수 `y`에 저장해 아래 코드에서 다시 사용합니다.
            var y = Math.Clamp(startY + row * radius * 1.5f, radius, _map.Height - radius);
            // 계산 결과를 지역 변수 `offsets`에 저장해 아래 코드에서 다시 사용합니다.
            var offsets = rowOffsets[row % rowOffsets.Length];

            // 컬렉션의 각 항목을 하나씩 꺼내 다음 블록을 반복합니다.
            foreach (var offset in offsets)
            // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
            {
                // 계산 결과를 지역 변수 `x`에 저장해 아래 코드에서 다시 사용합니다.
                var x = Math.Clamp(jail.CollisionCenter.X + offset, radius, _map.Width - radius);
                // 조건이 참인 경우에만 다음 블록을 실행합니다.
                if (!IsReleasePositionBlocked(x, y, radius))
                // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
                {
                    // 앞줄에서 시작한 호출이나 식을 이 값으로 마무리합니다.
                    candidates.Add((x, y));
                    // 현재 코드 블록 또는 객체 초기화를 닫습니다.
                }
                // 현재 코드 블록 또는 객체 초기화를 닫습니다.
            }
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 조건이 참인 경우에만 다음 블록을 실행합니다.
        if (candidates.Count > 0)
        // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
        {
            // 오른쪽 계산 결과를 호출자에게 반환합니다.
            return candidates[Math.Min(releaseIndex, candidates.Count - 1)];
            // 현재 코드 블록 또는 객체 초기화를 닫습니다.
        }

        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return (Math.Clamp(jail.CollisionCenter.X, radius, _map.Width - radius), Math.Clamp(startY, radius, _map.Height - radius));
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 석방 후보 위치가 감옥이나 장애물과 충돌하는지 확인합니다.
    // 석방 후보 좌표가 맵 충돌 영역과 겹치는지 확인합니다.
    private bool IsReleasePositionBlocked(float x, float y, float radius)
    // 바로 위 선언이나 조건에 속한 코드 블록을 시작합니다.
    {
        // 오른쪽 계산 결과를 호출자에게 반환합니다.
        return _map.IsMovementPositionBlocked(x, y, radius, new List<Obstacle>());
        // 현재 코드 블록 또는 객체 초기화를 닫습니다.
    }

    // 현재 코드 블록 또는 객체 초기화를 닫습니다.
}
