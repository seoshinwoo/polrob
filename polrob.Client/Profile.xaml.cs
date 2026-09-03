using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using polrob.Shared;

namespace polrob.Client;

public partial class Profile : ContentPage
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri(AuthSession.ApiBaseUrl)
    };
    private CancellationTokenSource? _statsLoadCancellation;
    private int _statsRequestVersion;
    private bool _isProfileVisible;

    public Profile()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isProfileVisible = true;
        await AuthSession.LoadAsync();

        if (!_isProfileVisible)
        {
            return;
        }

        if (!AuthSession.IsLoggedIn)
        {
            await Shell.Current.GoToAsync("//MainPage");
            return;
        }

        NameLabel.Text = AuthSession.Name ?? string.Empty;
        NameValueLabel.Text = AuthSession.Name ?? string.Empty;
        UserIdLabel.Text = AuthSession.UserId ?? string.Empty;

        await LoadGameStatsAsync();
    }

    protected override void OnDisappearing()
    {
        _isProfileVisible = false;
        CancelStatsLoad();
        base.OnDisappearing();
    }

    private async void OnBackClicked(object? sender, TappedEventArgs e)
    {
        CancelStatsLoad();
        await Shell.Current.GoToAsync("..", true);
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        CancelStatsLoad();
        await AuthSession.LogoutAsync();
        await Shell.Current.GoToAsync("//MainPage");
    }

    private async void OnStatsRefreshClicked(object? sender, EventArgs e)
    {
        await LoadGameStatsAsync();
    }

    private async Task LoadGameStatsAsync()
    {
        CancelStatsLoad();

        var sessionToken = AuthSession.SessionToken;
        var userId = AuthSession.UserId;
        if (!_isProfileVisible || string.IsNullOrWhiteSpace(sessionToken) || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var requestVersion = Interlocked.Increment(ref _statsRequestVersion);
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _statsLoadCancellation = cancellation;
        SetStatsLoading();

        try
        {
            using var response = await SendStatsRequestAsync(sessionToken, cancellation.Token);
            if (!IsCurrentStatsRequest(requestVersion, cancellation, sessionToken, userId))
            {
                return;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                AuthSession.ClearLocalSession();
                ShowStatsError("로그인 세션이 만료되었습니다.");
                await DisplayAlertAsync("프로필", "로그인 세션이 만료되었습니다. 다시 로그인해주세요.", "확인");
                await Shell.Current.GoToAsync("Login");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                ShowStatsError($"전적을 불러오지 못했습니다. ({(int)response.StatusCode})");
                return;
            }

            var stats = await response.Content.ReadFromJsonAsync<PlayerGameStats>(cancellation.Token);
            if (!IsCurrentStatsRequest(requestVersion, cancellation, sessionToken, userId))
            {
                return;
            }

            if (stats is null)
            {
                ShowStatsError("전적 응답을 읽을 수 없습니다.");
                return;
            }

            ShowStats(stats);
        }
        catch (HttpRequestException)
        {
            if (IsCurrentStatsRequest(requestVersion, cancellation, sessionToken, userId))
            {
                ShowStatsError("서버에 연결할 수 없어 전적을 불러오지 못했습니다.");
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentStatsRequest(requestVersion, cancellation, sessionToken, userId))
            {
                ShowStatsError("전적 요청 시간이 초과되었습니다.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Game stats loading failed: {ex}");
            if (IsCurrentStatsRequest(requestVersion, cancellation, sessionToken, userId))
            {
                ShowStatsError("전적을 불러오는 중 오류가 발생했습니다.");
            }
        }
        finally
        {
            if (ReferenceEquals(_statsLoadCancellation, cancellation))
            {
                _statsLoadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> SendStatsRequestAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "game-records/me/stats");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
                var response = await HttpClient.SendAsync(request, cancellationToken);

                if (attempt >= maxAttempts || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                var retryDelay = response.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromMilliseconds(300 * attempt);
                retryDelay = TimeSpan.FromMilliseconds(Math.Clamp(
                    retryDelay.TotalMilliseconds,
                    100,
                    3_000));
                response.Dispose();
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private bool IsCurrentStatsRequest(
        int requestVersion,
        CancellationTokenSource cancellation,
        string sessionToken,
        string userId)
    {
        return _isProfileVisible &&
               requestVersion == Volatile.Read(ref _statsRequestVersion) &&
               ReferenceEquals(_statsLoadCancellation, cancellation) &&
               string.Equals(AuthSession.SessionToken, sessionToken, StringComparison.Ordinal) &&
               string.Equals(AuthSession.UserId, userId, StringComparison.Ordinal);
    }

    private void CancelStatsLoad()
    {
        Interlocked.Increment(ref _statsRequestVersion);
        Interlocked.Exchange(ref _statsLoadCancellation, null)?.Cancel();
    }

    private void SetStatsLoading()
    {
        StatsGrid.IsVisible = false;
        StatsStatusLabel.Text = "전적을 불러오는 중...";
        StatsStatusLabel.TextColor = Color.FromArgb("#B9C7DB");
        StatsStatusLabel.IsVisible = true;
        StatsLoadingIndicator.IsVisible = true;
        StatsLoadingIndicator.IsRunning = true;
        StatsRefreshButton.IsEnabled = false;
    }

    private void ShowStats(PlayerGameStats stats)
    {
        SetBreakdownLabels(stats.Overall, OverallRecordLabel, OverallWinRateLabel);
        SetBreakdownLabels(stats.Police, PoliceRecordLabel, PoliceWinRateLabel);
        SetBreakdownLabels(stats.Robber, RobberRecordLabel, RobberWinRateLabel);

        StatsLoadingIndicator.IsRunning = false;
        StatsLoadingIndicator.IsVisible = false;
        StatsStatusLabel.IsVisible = false;
        StatsGrid.IsVisible = true;
        StatsRefreshButton.IsEnabled = true;
    }

    private void ShowStatsError(string message)
    {
        StatsLoadingIndicator.IsRunning = false;
        StatsLoadingIndicator.IsVisible = false;
        StatsGrid.IsVisible = false;
        StatsStatusLabel.Text = message;
        StatsStatusLabel.TextColor = Color.FromArgb("#FFB1B1");
        StatsStatusLabel.IsVisible = true;
        StatsRefreshButton.IsEnabled = true;
    }

    private static void SetBreakdownLabels(
        GameStatsBreakdown? breakdown,
        Label recordLabel,
        Label winRateLabel)
    {
        breakdown ??= new GameStatsBreakdown();
        var totalGames = Math.Max(0, breakdown.TotalGames);
        var wins = Math.Clamp(breakdown.Wins, 0, totalGames);
        var losses = Math.Clamp(breakdown.Losses, 0, totalGames);
        var winRate = double.IsFinite(breakdown.WinRate)
            ? Math.Clamp(breakdown.WinRate, 0d, 100d)
            : 0d;

        recordLabel.Text = $"{totalGames}전 {wins}승 {losses}패";
        winRateLabel.Text = $"{winRate:0.#}%";
    }
}
