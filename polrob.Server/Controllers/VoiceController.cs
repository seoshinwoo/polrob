using Microsoft.AspNetCore.Mvc;
using polrob.Shared;

namespace polrob.Server.Controllers;

[ApiController]
[Route("voice")]
public sealed class VoiceController : ControllerBase
{
    private readonly GameRoomService _gameRoomService;
    private readonly LiveKitTokenService _liveKitTokenService;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        GameRoomService gameRoomService,
        LiveKitTokenService liveKitTokenService,
        ILogger<VoiceController> logger)
    {
        _gameRoomService = gameRoomService;
        _liveKitTokenService = liveKitTokenService;
        _logger = logger;
    }

    [HttpPost("token")]
    public ActionResult<VoiceConnectionInfo> CreateToken([FromBody] VoiceTokenRequest request)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized("유효한 로그인 세션이 필요합니다.");
        }

        if (string.IsNullOrWhiteSpace(request.RoomId))
        {
            return BadRequest("방 ID가 필요합니다.");
        }

        // 클라이언트가 보낸 역할은 신뢰하지 않고 GameRoomService의 실제 참가 정보만 사용합니다.
        var player = _gameRoomService.GetAuthenticatedGamePlayer(request.RoomId, userId);
        if (player == null)
        {
            return Forbid();
        }

        try
        {
            return Ok(_liveKitTokenService.CreateTeamVoiceToken(player));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "LiveKit configuration is incomplete.");
            return Problem(
                title: "LiveKit 설정이 필요합니다.",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
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
