using System.Net.Http.Json;
using polrob.Shared;

namespace polrob.Client;

[QueryProperty(nameof(Role), "role")]
public partial class GameMatching : ContentPage
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri(AuthSession.ApiBaseUrl)
    };

    private bool _hasRequestedMatching;
    private PlayerRole _selectedRole = PlayerRole.Robber;

    public string Role
    {
        set
        {
            if (Enum.TryParse<PlayerRole>(value, true, out var role))
            {
                _selectedRole = role;
            }
        }
    }

    public GameMatching()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasRequestedMatching)
        {
            return;
        }

        await AuthSession.LoadAsync();

        if (!AuthSession.IsLoggedIn || string.IsNullOrWhiteSpace(AuthSession.PlayerId))
        {
            await Shell.Current.GoToAsync("Login");
            return;
        }

        await JoinRandomGameAsync(AuthSession.PlayerId, _selectedRole);
    }

    private void OnCancelMatchingClicked(object sender, EventArgs e)
    {
        // Matching cancel logic
    }

    public void UpdateMatchingCount(int currentCount, int maxCount = 6)
    {
        MatchingStatusLabel.Text = $"매칭 중({currentCount}/{maxCount})";
    }

    private async Task JoinRandomGameAsync(string userId, PlayerRole role)
    {
        try
        {
            _hasRequestedMatching = true;

            var response = await _httpClient.PostAsJsonAsync(
                "game/join-random",
                new JoinRandomGameRequest(userId, role));

            if (!response.IsSuccessStatusCode)
            {
                MatchingStatusLabel.Text = await ReadErrorMessageAsync(response);
                MatchingActivityIndicator.IsRunning = false;
                _hasRequestedMatching = false;
                return;
            }

            var serverResponse = await response.Content.ReadFromJsonAsync<ServerResponse>();
            if (serverResponse?.Success == false && !string.IsNullOrWhiteSpace(serverResponse.Message))
            {
                MatchingStatusLabel.Text = serverResponse.Message;
                MatchingActivityIndicator.IsRunning = false;
                _hasRequestedMatching = false;
            }
        }
        catch (HttpRequestException)
        {
            MatchingStatusLabel.Text = "서버에 연결할 수 없습니다.";
            MatchingActivityIndicator.IsRunning = false;
            _hasRequestedMatching = false;
        }
        catch (Exception ex)
        {
            MatchingStatusLabel.Text = $"매칭 요청 중 오류가 발생했습니다: {ex.Message}";
            MatchingActivityIndicator.IsRunning = false;
            _hasRequestedMatching = false;
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var message = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(message)
            ? $"매칭 요청이 실패했습니다. ({(int)response.StatusCode})"
            : message.Trim('"');
    }

    private sealed record JoinRandomGameRequest(string UserId, PlayerRole Role);
}
