using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using polrob.Shared;

namespace polrob.Test;

public class BotClient
{
    private const string DefaultServerUrl = "http://localhost:5174";
    private const string DefaultDevelopmentBotKey = "polrob-local-bot-key";
    private HubConnection? _hubConnection;
    private readonly TaskCompletionSource _matchCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name { get; set; } = string.Empty;
    public string Id { get; private set; } = string.Empty;
    public PlayerRole Role { get; set; }
    public string RoomId { get; private set; } = string.Empty;
    public int CurrentRoomCount { get; private set; }
    public bool IsMatched { get; private set; }

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
            new BotLoginRequest(Name, Role));

        response.EnsureSuccessStatusCode();

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("봇 로그인 응답을 읽을 수 없습니다.");

        Id = loginResponse.UserId;
    }

    public async Task Matching()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException($"{Name} 봇이 로그인되지 않았습니다.");
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "game/join-random",
            new BotMatchingRequest(Id, Role));

        response.EnsureSuccessStatusCode();

        var matchingResponse = await response.Content.ReadFromJsonAsync<ServerResponse>()
            ?? throw new InvalidOperationException("매칭 실패");

        RoomId = matchingResponse.RoomId
            ?? throw new InvalidOperationException("RoomId가 없음");
        UpdateRoomStatus(matchingResponse);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(_httpClient.BaseAddress!, "hubs/game-room"))
            .Build();

        _hubConnection.On<ServerResponse>("RoomStatusUpdated", UpdateRoomStatus);
        _hubConnection.On<ServerResponse>("GameStarted", UpdateRoomStatus);

        await _hubConnection.StartAsync();
        await _hubConnection.InvokeAsync("JoinRoom", RoomId, Id);
    }

    public Task WaitForMatchAsync(TimeSpan timeout)
    {
        return _matchCompleted.Task.WaitAsync(timeout);
    }

    private void UpdateRoomStatus(ServerResponse response)
    {
        if (!response.Success)
        {
            return;
        }

        CurrentRoomCount = response.CurrentCount;
        IsMatched = response.Matched;

        if (IsMatched)
        {
            _matchCompleted.TrySetResult();
        }
    }

    public async Task GamePlay()
    {

    }

    public async Task GameOver()
    {

    }

    private sealed record BotLoginRequest(string Name, PlayerRole Role);
    private sealed record LoginResponse(string UserId, string Name);
    private sealed record BotMatchingRequest(string UserId, PlayerRole Role);
}
