using System.Net.Http.Json;
using polrob.Shared;

namespace polrob.Client;

[QueryProperty(nameof(RoomId), "roomId")]
[QueryProperty(nameof(RoomCode), "roomCode")]
[QueryProperty(nameof(Role), "role")]
[QueryProperty(nameof(GameType), "gameType")]
[QueryProperty(nameof(IsHost), "isHost")]
public partial class GameOver : ContentPage
{
    private string _roomId = string.Empty;
    private string _roomCode = string.Empty;
    private PlayerRole _role = PlayerRole.Robber;
    private string _gameType = string.Empty;
    private bool _isHost;

    public string RoomId
    {
        set => _roomId = value ?? string.Empty;
    }

    public string RoomCode
    {
        set => _roomCode = value ?? string.Empty;
    }

    public string Role
    {
        set
        {
            if (Enum.TryParse<PlayerRole>(value, true, out var role))
            {
                _role = role;
            }
        }
    }

    public string GameType
    {
        set => _gameType = value ?? string.Empty;
    }

    public string IsHost
    {
        set => _isHost = bool.TryParse(value, out var isHost) && isHost;
    }

    public GameOver()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AuthSession.LoadAsync();
        UpdateAuthHeader();
    }

    private async void OnHomeClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage", true);
    }

    private async void OnProfileClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private async void OnPlayAgainClicked(object sender, EventArgs e)
    {
        await AuthSession.LoadAsync();
        if (!AuthSession.IsLoggedIn || string.IsNullOrWhiteSpace(AuthSession.PlayerId))
        {
            await Shell.Current.GoToAsync("Login");
            return;
        }

        if (string.Equals(_gameType, "random", StringComparison.OrdinalIgnoreCase))
        {
            await Shell.Current.GoToAsync($"GameMatching?role={_role}");
            return;
        }

        await NavigateToCustomLobbyAsync();
    }

    private void UpdateAuthHeader()
    {
        ProfileButton.IsVisible = AuthSession.IsLoggedIn;
        ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
    }

    private async Task NavigateToCustomLobbyAsync()
    {
        if (string.IsNullOrWhiteSpace(_roomId))
        {
            await DisplayAlertAsync("Play Again", "방 정보를 찾을 수 없습니다.", "OK");
            return;
        }

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(AuthSession.ApiBaseUrl) };
            var response = await httpClient.PostAsJsonAsync(
                "game/reset-room",
                new ResetRoomRequest(AuthSession.PlayerId!, _roomId, _role));

            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync("Play Again", await ReadErrorMessageAsync(response), "OK");
                return;
            }

            var serverResponse = await response.Content.ReadFromJsonAsync<ServerResponse>();
            if (serverResponse?.Success != true || string.IsNullOrWhiteSpace(serverResponse.RoomId))
            {
                await DisplayAlertAsync("Play Again", serverResponse?.Message ?? "방에 다시 들어갈 수 없습니다.", "OK");
                return;
            }

            var roomId = Uri.EscapeDataString(serverResponse.RoomId);
            var roomCode = Uri.EscapeDataString(serverResponse.RoomCode ?? _roomCode);
            var role = serverResponse.Role ?? _role;
            var isHost = _isHost.ToString().ToLowerInvariant();

            await Shell.Current.GoToAsync($"GameLobby?roomId={roomId}&roomCode={roomCode}&role={role}&isHost={isHost}");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync("Play Again", "서버에 연결할 수 없습니다.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Play Again", $"방 입장 중 오류가 발생했습니다: {ex.Message}", "OK");
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var message = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(message)
            ? $"요청이 실패했습니다. ({(int)response.StatusCode})"
            : message.Trim('"');
    }

    private sealed record ResetRoomRequest(string UserId, string RoomId, PlayerRole Role);
}
