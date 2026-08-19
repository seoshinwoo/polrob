using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using polrob.Shared;

namespace polrob.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    // 키 : sessionId(무작위 문자열 토큰), 값 : (유저 ID, 만료 시각)
    private static readonly ConcurrentDictionary<string, (string UserId, DateTime Expires)> Sessions = new();
    private readonly UserDbService _userDbService;
    private readonly BotIdentityService _botIdentityService;
    private readonly IConfiguration _configuration;

    public AuthController(
        UserDbService userDbService,
        BotIdentityService botIdentityService,
        IConfiguration configuration)
    {
        _userDbService = userDbService;
        _botIdentityService = botIdentityService;
        _configuration = configuration;
    }

    public record SignUpRequest(string Name, string Password);
    public record LoginRequest(string Name, string Password);
    public record LoginResponse(string SessionToken, string UserId, string Name);
    public record LogoutRequest(string SessionToken);
    public record BotLoginRequest(string? Name, PlayerRole Role);
    public record BotLoginResponse(string SessionToken, string UserId, string Name);

    [HttpPost("signup")]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequest req)
    {
        if (req is null)
        {
            return BadRequest("회원가입 정보를 입력해주세요.");
        }

        var validationError = ValidateCredentials(req.Name, req.Password);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var user = await _userDbService.CreateUserAsync(req.Name, req.Password);
        if (user is null)
        {
            return Conflict("이미 사용 중인 이름입니다.");
        }

        return Ok(CreateLoginResponse(user));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest("이름과 비밀번호를 입력해주세요.");
        }

        var user = await _userDbService.ValidateUserAsync(req.Name, req.Password);
        if (user is null)
        {
            return Unauthorized("이름 또는 비밀번호가 올바르지 않습니다.");
        }

        return Ok(CreateLoginResponse(user));
    }


    [HttpPost("logout")]
    public IActionResult Logout([FromBody] LogoutRequest req)
    {
        if (req is not null && !string.IsNullOrWhiteSpace(req.SessionToken))
        {
            Sessions.TryRemove(req.SessionToken, out _);
        }

        return NoContent();
    }

    [HttpPost("bot-login")]
    public IActionResult BotLogin([FromBody] BotLoginRequest? req)
    {
        if (!_configuration.GetValue<bool>("BotAuth:Enabled"))
        {
            return NotFound();
        }

        var configuredApiKey = _configuration["BotAuth:ApiKey"];
        var providedApiKey = Request.Headers["X-Polrob-Bot-Key"].ToString();
        if (!IsValidBotApiKey(configuredApiKey, providedApiKey))
        {
            return Unauthorized("봇 인증 키가 올바르지 않습니다.");
        }

        var bot = _botIdentityService.Create(req?.Name, req?.Role ?? PlayerRole.Robber);
        return Ok(new BotLoginResponse(CreateSession(bot.Id), bot.Id, bot.Name));
    }

    // 이 함수의 반환값은 2개.. 
    // 1. return bool : 세션이 유효한가?(true/false), 2. out userId : 유효하다면, 그 세션에 연결된 사용자 ID는 무엇인가?
    public static bool ValidateSession(string sessionToken, out string? userId)
    {
        userId = null;
        if (string.IsNullOrEmpty(sessionToken)) // sessionToken 과 sessionId는 같은 것이다..
        {
            return false;
        }

        if (Sessions.TryGetValue(sessionToken, out var entry))
        {
            if (entry.Expires < DateTime.UtcNow)
            {
                Sessions.TryRemove(sessionToken, out _);
                return false;
            }

            userId = entry.UserId;
            return true;
        }

        return false;
    }

    private static string? ValidateCredentials(string name, string password)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "이름을 입력해주세요.";
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length is < 4 or > 20)
        {
            return "이름은 4자 이상 20자 이하로 입력해주세요.";
        }

        if (!trimmedName.All(IsAllowedNameCharacter))
        {
            return "이름은 영문, 숫자, 밑줄, 하이픈만 사용할 수 있습니다.";
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return "비밀번호는 8자 이상이어야 합니다.";
        }

        return null;
    }

    private static bool IsAllowedNameCharacter(char ch)
    {
        return ch is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_'
            or '-';
    }

    private static bool IsValidBotApiKey(string? expected, string? provided)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return expectedBytes.Length == providedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static LoginResponse CreateLoginResponse(User user)
    {
        return new LoginResponse(CreateSession(user.Id), user.Id, user.Name);
    }

    private static string CreateSession(string userId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        Sessions[sessionId] = (userId, DateTime.UtcNow.AddHours(12));
        return sessionId;
    }
}
