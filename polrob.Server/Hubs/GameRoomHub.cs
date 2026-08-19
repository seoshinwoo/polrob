using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using polrob.Server.Controllers;
using polrob.Shared;

namespace polrob.Server.Hubs;

public class GameRoomHub : Hub
{
    private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromSeconds(10); // SignalR 연결이 끊어진 뒤 서버가 10초 기다리는 유예 시간..

    // 2개의 딕셔너리를 만든 이유.. 재연결 처리에서 필요한 정보를 서로 반대 방향으로 빠르게 찾기 위한 인덱스..
    // 특정 연결이 끊기면 서버는 이 연결이 누구의 것인지 알아야 함.. 이것은 Connections으로 알 수 있음..
    // 그 다음 서버는 10초 유예 시간 뒤 해당 방의 해당 유저가 다시 접속했는지 확인.. 이것은 ActiveUserConnections으로 알 수 있음..
    // 이때 연결문자열이 다르면 재접속한 것이므로, 이전 연결이 끊겼더라도 사용자를 방에서 제거하지 않음.. 
    // Connections 하나만 두고 전체를 순회해서 찾는 것도 가능은 하지만 그러면 접속시간 또는 순서를 따로 관리해야하고 매번 전체 연결을 검사해야함..
    private static readonly ConcurrentDictionary<string, RoomConnection> Connections = new(); // Key : Context.ConnectionId, Value : (RoomId, UserId)
    private static readonly ConcurrentDictionary<string, string> ActiveUserConnections = new(); // Key : "roomId:userId", Value : 가장 최근의 ConnectionId
    private const string AuthenticatedUserIdKey = "AuthenticatedUserId";
    private const string AuthenticatedSessionTokenKey = "AuthenticatedSessionToken";

    private readonly GameRoomService _gameRoomService;
    private readonly ILogger<GameRoomHub> _logger;

    public GameRoomHub(GameRoomService gameRoomService, ILogger<GameRoomHub> logger)
    {
        _gameRoomService = gameRoomService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext(); // SignalR 연결을 처음에는 HTTP 요청을 시작..
        var authorization = httpContext?.Request.Headers.Authorization.ToString(); // HTTP 헤더에서 토큰을 찾음..
        var token = httpContext?.Request.Query["access_token"].ToString(); // 쿼리 문자열에서 토큰을 찾음..

        const string bearerPrefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(token) &&
            authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            token = authorization[bearerPrefix.Length..].Trim();
        }

        if (!AuthController.ValidateSession(token ?? string.Empty, out var userId) ||
            string.IsNullOrWhiteSpace(userId)) // 토큰이 서버의 Sessions 딕셔너리에 있는지, 만료되지 않았는지 확인..
        {
            Context.Abort(); // 유효하지 않다면 SignalR 연결을 끊음..
            return;
        }

        // Context.Items는 현재 SignalR 연결 하나에만 연결된 임시 저장소.. 
        // 이 연결 이후 Hub 메소드를 호출하면, 매번 토큰 문자열을 클라이언트에게 다시 받지 않고 이곳의 값을 이용..
        Context.Items[AuthenticatedUserIdKey] = userId;
        Context.Items[AuthenticatedSessionTokenKey] = token;
        await base.OnConnectedAsync();
    }

    public async Task JoinRoom(string roomId)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            await Clients.Caller.SendAsync("RoomStatusUpdated", new ServerResponse
            {
                Success = false,
                Message = "방 ID가 필요합니다."
            });
            return;
        }

        if (_gameRoomService.GetAuthenticatedGamePlayer(roomId, userId) == null)
        {
            await Clients.Caller.SendAsync("RoomStatusUpdated", new ServerResponse
            {
                Success = false,
                Message = "이 방에 참여 중인 사용자가 아닙니다.",
                RoomId = roomId
            });
            return;
        }

        var userKey = CreateUserKey(roomId, userId);
        Connections[Context.ConnectionId] = new RoomConnection(roomId, userId);
        ActiveUserConnections[userKey] = Context.ConnectionId;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        var status = _gameRoomService.GetRoomStatus(roomId);
        await Clients.Caller.SendAsync("RoomStatusUpdated", status);

        if (status.Matched && !status.IsPrivate)
        {
            var gameStartStatus = _gameRoomService.StartGameIfMatched(roomId);
            await Clients.Caller.SendAsync("GameStarted", gameStartStatus);
        }
    }

    public async Task StartGame(string roomId)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            await Clients.Caller.SendAsync("RoomStatusUpdated", new ServerResponse
            {
                Success = false,
                Message = "방 ID가 필요합니다."
            });
            return;
        }

        if (!_gameRoomService.IsRoomHost(roomId, userId))
        {
            await Clients.Caller.SendAsync("RoomStatusUpdated", new ServerResponse
            {
                Success = false,
                Message = "방장만 게임을 시작할 수 있습니다.",
                RoomId = roomId
            });
            return;
        }

        var gameStartStatus = _gameRoomService.StartGameIfMatched(roomId);
        if (gameStartStatus.Success && gameStartStatus.Matched)
        {
            await Clients.Group(roomId).SendAsync("GameStarted", gameStartStatus);
            return;
        }

        await Clients.Caller.SendAsync("RoomStatusUpdated", gameStartStatus);
    }

    public async Task ChangeRole(string roomId, PlayerRole role)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            await Clients.Caller.SendAsync("RoomStatusUpdated", new ServerResponse
            {
                Success = false,
                Message = "방 ID가 필요합니다.",
                Role = role
            });
            return;
        }

        var status = _gameRoomService.ChangePlayerRole(roomId, userId, role);
        if (!status.Success)
        {
            await Clients.Caller.SendAsync("RoomStatusUpdated", status);
            return;
        }

        await Clients.Group(roomId).SendAsync("RoomStatusUpdated", status);
    }

    public Task LeaveRoom(string roomId)
    {
        return string.IsNullOrWhiteSpace(roomId)
            ? Task.CompletedTask
            : Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }

    public async Task CancelMatching(string roomId)
    {
        await CancelMatchingWithAcknowledgement(roomId);
    }

    public async Task<ServerResponse> CancelMatchingWithAcknowledgement(string roomId)
    {
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return new ServerResponse
            {
                Success = false,
                Message = "방 ID가 필요합니다.",
                RoomId = roomId
            };
        }

        var startedAt = Stopwatch.GetTimestamp(); // 취소 처리에 걸린 시간을 로그로 남기기 위한 시작 시각
        var status = _gameRoomService.RemovePlayer(roomId, userId);
        if (!status.Success)
        {
            return status;
        }

        RemoveConnectionTracking(Context.ConnectionId, roomId, userId);

        try
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
            await Clients.Group(roomId).SendAsync("RoomStatusUpdated", status);
        }
        catch (Exception ex)
        {
            // 방의 플레이어 제거는 이미 완료되었습니다. 그룹 정리나 다른 사용자에게
            // 상태를 알리는 과정의 실패 때문에 호출자에게 잘못된 실패를 반환하지 않습니다.
            _logger.LogWarning(ex, "Room {RoomId} leave notification failed after removing user {UserId}.", roomId, userId);
        }

        _logger.LogInformation(
            "Room {RoomId} removed user {UserId} in {ElapsedMilliseconds:F1} ms.",
            roomId,
            userId,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        return status;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (!Connections.TryGetValue(Context.ConnectionId, out var connection))
        {
            await base.OnDisconnectedAsync(exception);
            return;
        }

        await Task.Delay(DisconnectGracePeriod);

        var userKey = CreateUserKey(connection.RoomId, connection.UserId);
        var isSameConnectionStillActive = ActiveUserConnections.TryGetValue(userKey, out var activeConnectionId)
            && activeConnectionId == Context.ConnectionId;

        Connections.TryRemove(Context.ConnectionId, out _);

        if (isSameConnectionStillActive)
        {
            RemoveActiveUserConnection(connection.RoomId, connection.UserId, Context.ConnectionId);

            if (!_gameRoomService.IsRoomMatched(connection.RoomId))
            {
                var status = _gameRoomService.RemovePlayer(connection.RoomId, connection.UserId);
                await Clients.Group(connection.RoomId).SendAsync("RoomStatusUpdated", status);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static void RemoveConnectionTracking(string connectionId, string roomId, string userId)
    {
        Connections.TryRemove(connectionId, out _);
        RemoveActiveUserConnection(roomId, userId, connectionId);
    }

    private static void RemoveActiveUserConnection(string roomId, string userId, string connectionId)
    {
        var userKey = CreateUserKey(roomId, userId);
        if (ActiveUserConnections.TryGetValue(userKey, out var activeConnectionId)
            && activeConnectionId == connectionId)
        {
            ActiveUserConnections.TryRemove(userKey, out _);
        }
    }

    private static string CreateUserKey(string roomId, string userId)
    {
        return $"{roomId}:{userId}";
    }

    private string GetAuthenticatedUserId()
    {
        if (!Context.Items.TryGetValue(AuthenticatedUserIdKey, out var value) || value is not string userId ||
            !Context.Items.TryGetValue(AuthenticatedSessionTokenKey, out var tokenValue) || tokenValue is not string token ||
            !AuthController.ValidateSession(token, out var currentUserId) || currentUserId != userId)
        {
            throw new HubException("로그인 세션이 만료되었거나 유효하지 않습니다.");
        }

        return userId;
    }

    private sealed record RoomConnection(string RoomId, string UserId);
}
