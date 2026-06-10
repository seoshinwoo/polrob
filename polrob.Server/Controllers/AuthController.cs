using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace polrob.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
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
    public record BotLoginRequest(string? Name);
    public record LoginResponse(string SessionToken, string UserId, string Name);
    public record LogoutRequest(string SessionToken);

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

        var bot = _botIdentityService.Create(req?.Name);
        return Ok(CreateLoginResponse(bot));
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

    public static bool ValidateSession(string sessionToken, out string? userId)
    {
        userId = null;
        if (string.IsNullOrEmpty(sessionToken))
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
        var sessionId = Guid.NewGuid().ToString("N");
        Sessions[sessionId] = (user.Id, DateTime.UtcNow.AddHours(12));

        return new LoginResponse(sessionId, user.Id, user.Name);
    }
}
