using System.Net.Http.Json;
using polrob.Shared;

namespace polrob.Test;

public class BotClient
{
    private const string DefaultServerUrl = "http://localhost:5174";
    private const string DefaultDevelopmentBotKey = "polrob-local-bot-key";

    public string Name { get; set; } = string.Empty;
    public string Id { get; private set; } = string.Empty;
    public string SessionToken { get; private set; } = string.Empty;
    public PlayerRole Role { get; set; }

    private readonly HttpClient _httpClient;

    public BotClient()
    {
        var serverUrl = Environment.GetEnvironmentVariable("POLROB_SERVER_URL")
            ?? DefaultServerUrl;
        var botKey = Environment.GetEnvironmentVariable("POLROB_BOT_KEY")
            ?? DefaultDevelopmentBotKey;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(serverUrl)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Polrob-Bot-Key", botKey);
    }

    public async Task Login()
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "auth/bot-login",
            new BotLoginRequest(Name));

        response.EnsureSuccessStatusCode();

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("봇 로그인 응답을 읽을 수 없습니다.");

        Id = loginResponse.UserId;
        SessionToken = loginResponse.SessionToken;
    }

    public async Task Matching()
    {

    }

    public async Task GamePlay()
    {

    }

    public async Task GameOver()
    {

    }

    private sealed record BotLoginRequest(string Name);
    private sealed record LoginResponse(string SessionToken, string UserId, string Name);
}
