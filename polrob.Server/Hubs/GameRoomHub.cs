using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using polrob.Server.Controllers;
using polrob.Shared;

namespace polrob.Server.Hubs;

public class GameRoomHub : Hub
{
    private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, RoomConnection> Connections = new();
    private static readonly ConcurrentDictionary<string, string> ActiveUserConnections = new();
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
        var httpContext = Context.GetHttpContext();
        var authorization = httpContext?.Request.Headers.Authorization.ToString();
        var token = httpContext?.Request.Query["access_token"].ToString();
        const string bearerPrefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(token) &&
            authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            token = authorization[bearerPrefix.Length..].Trim();
        }

        if (!AuthController.ValidateSession(token ?? string.Empty, out var userId) ||
            string.IsNullOrWhiteSpace(userId))
        {
            Context.Abort();
            return;
        }

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

        var startedAt = Stopwatch.GetTimestamp();
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
