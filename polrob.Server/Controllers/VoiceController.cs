using Microsoft.AspNetCore.Mvc;
using polrob.Server.Network;
using polrob.Shared;

namespace polrob.Server.Controllers;

[ApiController]
[Route("voice")]
public sealed class VoiceController : ControllerBase
{
    private readonly GameRoomService _gameRoomService;
    private readonly LiveKitTokenService _liveKitTokenService;
    private readonly ActiveGameParticipantRegistry _activeGameParticipants;
    private readonly ILogger<VoiceController> _logger;

    public VoiceController(
        GameRoomService gameRoomService,
        LiveKitTokenService liveKitTokenService,
        ActiveGameParticipantRegistry activeGameParticipants,
        ILogger<VoiceController> logger)
    {
        _gameRoomService = gameRoomService;
        _liveKitTokenService = liveKitTokenService;
        _activeGameParticipants = activeGameParticipants;
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

        // 활성 경기의 참가자와 서버가 확정한 역할만 사용합니다. 로비에서는 토큰을 발급하지 않습니다.
        if (!_gameRoomService.TryGetAuthenticatedTeamVoiceAccess(
            request.RoomId,
            userId,
            out var player,
            out var voiceSessionId) ||
            player == null ||
            !_activeGameParticipants.TryGetConnectionId(
                request.RoomId,
                userId,
                out var gameConnectionId))
        {
            // 수동 세션 인증을 사용하므로 등록된 ASP.NET 인증 scheme이 필요한 Forbid()는 호출하지 않습니다.
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            return Ok(_liveKitTokenService.CreateTeamVoiceToken(
                player,
                voiceSessionId,
                gameConnectionId));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "LiveKit configuration is incomplete.");
            return Problem(
                title: "LiveKit 설정이 필요합니다.",
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
