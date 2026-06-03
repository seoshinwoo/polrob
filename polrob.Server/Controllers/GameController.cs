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
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new ServerResponse
            {
                Success = false,
                Message = "사용자 ID가 필요합니다."
            });
        }

        var response = await _gameRoomService.CreateRoom(
            request.UserId,
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
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new ServerResponse
            {
                Success = false,
                Message = "사용자 ID가 필요합니다."
            });
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
            request.UserId,
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
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new ServerResponse
            {
                Success = false,
                Message = "사용자 ID가 필요합니다."
            });
        }

        var response = await _gameRoomService.JoinRandomGame(request.UserId, string.Empty, request.Role);
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
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new ServerResponse
            {
                Success = false,
                Message = "사용자 ID가 필요합니다."
            });
        }

        if (string.IsNullOrWhiteSpace(request.RoomId))
        {
            return BadRequest(new ServerResponse
            {
                Success = false,
                Message = "방 ID가 필요합니다."
            });
        }

        var response = await _gameRoomService.ResetRoomForReplay(request.RoomId, request.UserId, request.Role);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        await _gameRoomHubContext.Clients
            .Group(request.RoomId)
            .SendAsync("RoomStatusUpdated", response);

        return Ok(response);
    }

    public sealed record CreateRoomRequest(
        string UserId,
        string Type = "custom",
        PlayerRole Role = PlayerRole.Police,
        bool IsPrivate = true);

    public sealed record JoinCustomGameRequest(string UserId, string RoomCode, PlayerRole Role = PlayerRole.Robber);
    public sealed record JoinRandomGameRequest(string UserId, PlayerRole Role);
    public sealed record ResetRoomRequest(string UserId, string RoomId, PlayerRole Role);
}
