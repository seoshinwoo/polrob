using Microsoft.AspNetCore.Mvc;
using polrob.Shared;

namespace polrob.Server.Controllers;

[ApiController]
[Route("game-records")]
public sealed class GameRecordsController : ControllerBase
{
    private readonly GameRecordDbService _gameRecordDbService;

    public GameRecordsController(GameRecordDbService gameRecordDbService)
    {
        _gameRecordDbService = gameRecordDbService;
    }

    [HttpGet("me/stats")]
    public async Task<ActionResult<PlayerGameStats>> GetMyStats(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized("유효한 로그인 세션이 필요합니다.");
        }

        return Ok(await _gameRecordDbService.GetPlayerStatsAsync(userId, cancellationToken));
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
}
