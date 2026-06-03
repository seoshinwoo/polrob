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
    public async Task CreateRoom(string userId)
    {
        await _gameRoomService.CreateRoom(userId);
    }
    public async Task<ServerResponse?> JoinCustomGame(string userId, string roomId)
    {
        var response = new ServerResponse();

        return response;
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

    public sealed record JoinRandomGameRequest(string UserId, PlayerRole Role);
}
