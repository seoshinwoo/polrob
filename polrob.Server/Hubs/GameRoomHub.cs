using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using polrob.Shared;

namespace polrob.Server.Hubs;

public class GameRoomHub : Hub
{
    private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, RoomConnection> Connections = new();
    private static readonly ConcurrentDictionary<string, string> ActiveUserConnections = new();

    private readonly GameRoomService _gameRoomService;

    public GameRoomHub(GameRoomService gameRoomService)
    {
        _gameRoomService = gameRoomService;
    }

    public async Task JoinRoom(string roomId, string userId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(userId))
        {
            await Clients.Caller.SendAsync("RoomStatusUpdated", new ServerResponse
            {
                Success = false,
                Message = "방 ID와 사용자 ID가 필요합니다."
            });
            return;
        }

        var userKey = CreateUserKey(roomId, userId);
        Connections[Context.ConnectionId] = new RoomConnection(roomId, userId);
        ActiveUserConnections[userKey] = Context.ConnectionId;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        var status = _gameRoomService.GetRoomStatus(roomId);
        await Clients.Caller.SendAsync("RoomStatusUpdated", status);
    }

    public Task LeaveRoom(string roomId)
    {
        return string.IsNullOrWhiteSpace(roomId)
            ? Task.CompletedTask
            : Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
    }

    public async Task CancelMatching(string roomId, string userId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        RemoveConnectionTracking(Context.ConnectionId, roomId, userId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);

        var status = _gameRoomService.RemovePlayer(roomId, userId);
        await Clients.Group(roomId).SendAsync("RoomStatusUpdated", status);
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

    private sealed record RoomConnection(string RoomId, string UserId);
}
