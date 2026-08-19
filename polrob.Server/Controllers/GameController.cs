using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using polrob.Server.Hubs;
using polrob.Shared;

namespace polrob.Server.Controllers;

[ApiController]
[Route("game")]
public class GameController : ControllerBase
{
    private readonly GameRoomService _gameRoomService;
    private readonly IHubContext<GameRoomHub> _gameRoomHubContext;

    public GameController(
        GameRoomService gameRoomService,
        IHubContext<GameRoomHub> gameRoomHubContext)
    {
        _gameRoomService = gameRoomService;
        _gameRoomHubContext = gameRoomHubContext;
    }

    [HttpPost("create")]
    public async Task<ActionResult<ServerResponse>> CreateRoom([FromBody] CreateRoomRequest request)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized("유효한 로그인 세션이 필요합니다.");
        }

        var response = await _gameRoomService.CreateRoom(
            userId,
            request.Type,
            request.Role,
            request.IsPrivate);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpPost("join-custom")]
    public async Task<ActionResult<ServerResponse>> JoinCustomGame([FromBody] JoinCustomGameRequest request)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized("유효한 로그인 세션이 필요합니다.");
        }

        if (string.IsNullOrWhiteSpace(request.RoomCode))
        {
            return BadRequest(new ServerResponse
            {
                Success = false,
                Message = "방 코드가 필요합니다."
            });
        }

        var response = await _gameRoomService.JoinCustomGame(
            userId,
            request.RoomCode,
            request.Role);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        if (!string.IsNullOrWhiteSpace(response.RoomId))
        {
            var roomStatus = _gameRoomService.GetRoomStatus(response.RoomId);
            await _gameRoomHubContext.Clients
                .Group(response.RoomId)
                .SendAsync("RoomStatusUpdated", roomStatus);
        }

        return Ok(response);
    }

    [HttpPost("join-random")]
    public async Task<ActionResult<ServerResponse>> JoinRandomGame([FromBody] JoinRandomGameRequest request)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized("유효한 로그인 세션이 필요합니다.");
        }

        var response = await _gameRoomService.JoinRandomGame(userId, string.Empty, request.Role);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        if (!string.IsNullOrWhiteSpace(response.RoomId))
        {
            var roomStatus = _gameRoomService.GetRoomStatus(response.RoomId);
            await _gameRoomHubContext.Clients
                .Group(response.RoomId)
                .SendAsync("RoomStatusUpdated", roomStatus);

            if (roomStatus.Matched)
            {
                var gameStartStatus = _gameRoomService.StartGameIfMatched(response.RoomId);
                await _gameRoomHubContext.Clients
                    .Group(response.RoomId)
                    .SendAsync("GameStarted", gameStartStatus);
            }
        }

        return Ok(response);
    }

    [HttpPost("reset-room")]
    public async Task<ActionResult<ServerResponse>> ResetRoom([FromBody] ResetRoomRequest request)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized("유효한 로그인 세션이 필요합니다.");
        }

        if (string.IsNullOrWhiteSpace(request.RoomId))
        {
            return BadRequest(new ServerResponse
            {
                Success = false,
                Message = "방 ID가 필요합니다."
            });
        }

        var response = await _gameRoomService.RejoinCustomRoomForReplay(request.RoomId, userId, request.Role);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        await _gameRoomHubContext.Clients
            .Group(request.RoomId)
            .SendAsync("RoomStatusUpdated", response);

        return Ok(response);
    }

    private bool TryGetAuthenticatedUserId(out string userId)
    {
        userId = string.Empty;
        var authorization = Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!AuthController.ValidateSession(
                authorization[bearerPrefix.Length..].Trim(),
                out var authenticatedUserId) || string.IsNullOrWhiteSpace(authenticatedUserId))
        {
            return false;
        }

        userId = authenticatedUserId;
        return true;
    }

    public sealed record CreateRoomRequest(
        string Type = "custom",
        PlayerRole Role = PlayerRole.Police,
        bool IsPrivate = true);

    public sealed record JoinCustomGameRequest(string RoomCode, PlayerRole Role = PlayerRole.Robber);
    public sealed record JoinRandomGameRequest(PlayerRole Role);
    public sealed record ResetRoomRequest(string RoomId, PlayerRole Role);
}
