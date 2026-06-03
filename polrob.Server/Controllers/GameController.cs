using Microsoft.AspNetCore.Mvc;
using polrob.Shared;

namespace polrob.Server.Controllers;

[ApiController]
[Route("game")]
public class GameController : ControllerBase
{
    private readonly GameRoomService _gameRoomService;

    public GameController(GameRoomService gameRoomService)
    {
        _gameRoomService = gameRoomService;
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

        return Ok(response);
    }

    public sealed record JoinRandomGameRequest(string UserId, PlayerRole Role);
}
