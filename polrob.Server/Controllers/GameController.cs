using System.Collections.Concurrent;
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

    public async Task<ServerResponse?> JoinRandomGame(string userId, string roomId, PlayerRole role)
    {
        var response = new ServerResponse();

        return response;
    }
}