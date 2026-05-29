using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Collections.Concurrent;

namespace polrob.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    // Simple in-memory session store for demo. Replace with Redis/DB in production.
    private static readonly ConcurrentDictionary<string, (string PlayerId, DateTime Expires)> Sessions = new();

    private readonly IConfiguration _config;

    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    public record LoginRequest(string token);
    public record LoginResponse(string sessionToken, string playerId);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (req == null || string.IsNullOrEmpty(req.token))
            return BadRequest("missing token");

        // Read tenant and clientId from configuration (set in appsettings or environment)
        var tenant = _config["AzureAd:TenantId"] ?? _config["AzureAd:Tenant"] ?? "common";
        var clientId = _config["AzureAd:ClientId"] ?? _config["AzureAd:Client"];

        var metadataAddress = $"https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration";

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(metadataAddress, new OpenIdConnectConfigurationRetriever());
        OpenIdConnectConfiguration oidcConfig;
        try
        {
            oidcConfig = await configManager.GetConfigurationAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"failed to get OIDC configuration: {ex.Message}");
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateIssuer = true,
            ValidIssuers = new[] { oidcConfig.Issuer },
            ValidateAudience = true,
            ValidAudience = clientId ?? string.Empty,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        try
        {
            var principal = tokenHandler.ValidateToken(req.token, validationParameters, out var validatedToken);

            var sub = principal.FindFirst("sub")?.Value ?? principal.FindFirst("oid")?.Value;
            if (string.IsNullOrEmpty(sub))
                return BadRequest("token missing subject claim");

            // Map subject to player id. In production look up/create user record in DB.
            var playerId = "player-" + sub;

            // Issue server session token (simple GUID) and store mapping
            var sessionId = Guid.NewGuid().ToString("N");
            Sessions[sessionId] = (playerId, DateTime.UtcNow.AddHours(12));

            return Ok(new LoginResponse(sessionId, playerId));
        }
        catch (SecurityTokenException stex)
        {
            return Unauthorized(stex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    // Optional helper to validate a session token (used by game server)
    public static bool ValidateSession(string sessionToken, out string? playerId)
    {
        playerId = null;
        if (string.IsNullOrEmpty(sessionToken)) return false;
        if (Sessions.TryGetValue(sessionToken, out var entry))
        {
            if (entry.Expires < DateTime.UtcNow)
            {
                Sessions.TryRemove(sessionToken, out _);
                return false;
            }
            playerId = entry.PlayerId;
            return true;
        }
        return false;
    }
}
