using System.Net.Http.Json;
using polrob.Shared;

namespace polrob.Client;

[QueryProperty(nameof(RoomId), "roomId")]
[QueryProperty(nameof(RoomCode), "roomCode")]
[QueryProperty(nameof(Role), "role")]
[QueryProperty(nameof(GameType), "gameType")]
[QueryProperty(nameof(IsHost), "isHost")]
[QueryProperty(nameof(WinnerRole), "winnerRole")]
[QueryProperty(nameof(RemainingTime), "remainingTime")]
[QueryProperty(nameof(CapturedRobbers), "capturedRobbers")]
[QueryProperty(nameof(TotalRobbers), "totalRobbers")]
public partial class GameOver : ContentPage
{
    private string _roomId = string.Empty;
    private string _roomCode = string.Empty;
    private PlayerRole _role = PlayerRole.Robber;
    private string _gameType = string.Empty;
    private bool _isHost;
    private PlayerRole _winnerRole = PlayerRole.Robber;
    private int _remainingTime;
    private int _capturedRobbers;
    private int _totalRobbers;
    private GameOverLayoutDensity? _appliedLayoutDensity;

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

    public string WinnerRole
    {
        set
        {
            if (Enum.TryParse<PlayerRole>(value, true, out var winnerRole))
            {
                _winnerRole = winnerRole;
            }
        }
    }

    public string RemainingTime
    {
        set => _remainingTime = ParseNonNegativeInt(value);
    }

    public string CapturedRobbers
    {
        set => _capturedRobbers = ParseNonNegativeInt(value);
    }

    public string TotalRobbers
    {
        set => _totalRobbers = ParseNonNegativeInt(value);
    }

    public GameOver()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        var density = height < 760
            ? GameOverLayoutDensity.ExtraCompact
            : height < 900 || width < 380
                ? GameOverLayoutDensity.Compact
                : GameOverLayoutDensity.Regular;

        if (_appliedLayoutDensity == density)
        {
            return;
        }

        _appliedLayoutDensity = density;
        ApplyLayoutDensity(density);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AuthSession.LoadAsync();
        UpdateAuthHeader();
        UpdateResultDisplay();
    }

    private async void OnHomeClicked(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage", true);
    }

    private async void OnProfileClicked(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private async void OnPlayAgainClicked(object? sender, EventArgs e)
    {
        await AuthSession.LoadAsync();
        if (!AuthSession.IsLoggedIn || string.IsNullOrWhiteSpace(AuthSession.UserId))
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
        ProfileNameLabel.Text = AuthSession.Name ?? string.Empty;
    }

    private void UpdateResultDisplay()
    {
        var policeWon = _winnerRole == PlayerRole.Police;

        ResultTitleLabel.Text = policeWon ? "경찰 승리!" : "도둑 승리!";
        VictoryImage.Source = policeWon
            ? "gameover_police_victory.png"
            : "gameover_robber_victory.png";
        ResultBannerInner.BackgroundColor = Color.FromArgb(policeWon ? "#0D58C5" : "#C9430C");
        VictoryGlow.Brush = Color.FromArgb(policeWon ? "#D0188EFF" : "#D0FF6A16");

        PlayAgainButton.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(policeWon ? Color.FromArgb("#168DF8") : Color.FromArgb("#FF8618"), 0),
                new(policeWon ? Color.FromArgb("#0647AD") : Color.FromArgb("#B62E05"), 1)
            },
            new Point(0, 0),
            new Point(0, 1));
        PlayAgainButton.BorderColor = Color.FromArgb(policeWon ? "#52C9FF" : "#FFC34F");

        RemainingTimeLabel.Text = FormatTime(_remainingTime);
        CapturedRobbersLabel.Text = $"{Math.Min(_capturedRobbers, _totalRobbers)} / {_totalRobbers}";
    }

    private void ApplyLayoutDensity(GameOverLayoutDensity density)
    {
        var compact = density != GameOverLayoutDensity.Regular;
        var extraCompact = density == GameOverLayoutDensity.ExtraCompact;

        GameOverContent.Padding = extraCompact
            ? new Thickness(12, 88, 12, 12)
            : compact
                ? new Thickness(14, 94, 14, 18)
                : new Thickness(18, 102, 18, 30);
        GameOverContent.Spacing = extraCompact ? 6 : compact ? 8 : 13;

        ResultBannerInner.HeightRequest = extraCompact ? 62 : compact ? 72 : 84;
        ResultTitleLabel.FontSize = extraCompact ? 28 : compact ? 32 : 38;
        VictoryBadgeLabel.FontSize = extraCompact ? 29 : compact ? 33 : 37;

        HeroGrid.HeightRequest = extraCompact ? 174 : compact ? 220 : 286;
        VictoryBackdrop.WidthRequest = extraCompact ? 158 : compact ? 198 : 258;
        VictoryBackdrop.HeightRequest = VictoryBackdrop.WidthRequest;
        VictoryImage.HeightRequest = HeroGrid.HeightRequest;
        VictoryGlow.Radius = extraCompact ? 18 : compact ? 24 : 32;

        SetStatsRowHeights(extraCompact ? 44 : compact ? 50 : 58);
        StatsGrid.Padding = extraCompact
            ? new Thickness(10, 1)
            : compact
                ? new Thickness(12, 1)
                : new Thickness(14, 2);
        StatsGrid.ColumnSpacing = extraCompact ? 7 : compact ? 8 : 10;
        TimerIcon.WidthRequest = TimerIcon.HeightRequest = extraCompact ? 29 : compact ? 32 : 36;
        HandcuffsIcon.WidthRequest = HandcuffsIcon.HeightRequest = extraCompact ? 30 : compact ? 33 : 37;
        RemainingTimeCaption.FontSize = CapturedCaption.FontSize = extraCompact ? 17 : compact ? 18 : 20;
        RemainingTimeLabel.FontSize = CapturedRobbersLabel.FontSize = extraCompact ? 21 : compact ? 23 : 26;

        PlayAgainFrame.HeightRequest = extraCompact ? 58 : compact ? 66 : 78;
        PlayAgainFrame.Margin = extraCompact
            ? new Thickness(8, 1, 8, 0)
            : compact
                ? new Thickness(10, 2, 10, 0)
                : new Thickness(12, 3, 12, 0);
        PlayAgainButton.FontSize = extraCompact ? 24 : compact ? 27 : 31;

        BottomHomeFrame.WidthRequest = extraCompact ? 166 : compact ? 184 : 210;
        BottomHomeFrame.HeightRequest = extraCompact ? 46 : compact ? 50 : 58;
        BottomHomeLabel.FontSize = extraCompact ? 18 : compact ? 20 : 23;
    }

    private void SetStatsRowHeights(double rowHeight)
    {
        StatsGrid.RowDefinitions[0].Height = new GridLength(rowHeight);
        StatsGrid.RowDefinitions[2].Height = new GridLength(rowHeight);
    }

    private static string FormatTime(int totalSeconds)
    {
        var safeSeconds = Math.Max(0, totalSeconds);
        return $"{safeSeconds / 60:00}:{safeSeconds % 60:00}";
    }

    private static int ParseNonNegativeInt(string? value)
    {
        return int.TryParse(value, out var result) ? Math.Max(0, result) : 0;
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
            AuthSession.ApplyAuthorization(httpClient);
            var response = await httpClient.PostAsJsonAsync(
                "game/reset-room",
                new ResetRoomRequest(_roomId, _role));

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

    private sealed record ResetRoomRequest(string RoomId, PlayerRole Role);

    private enum GameOverLayoutDensity
    {
        Regular,
        Compact,
        ExtraCompact
    }
}
