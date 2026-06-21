using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using polrob.Shared;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace polrob.Client;

[QueryProperty(nameof(Role), "role")]
public partial class GameMatching : ContentPage
{
    private const int MatchingCapacity = 6;
    private const float SegmentAngle = 60f;
    private const float SegmentGapAngle = 8f;

    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri(AuthSession.ApiBaseUrl)
    };

    private bool _hasRequestedMatching;
    private PlayerRole _selectedRole = PlayerRole.Robber;
    private HubConnection? _hubConnection;
    private string? _roomId;
    private bool _isMatched;
    private bool _isNavigatingToGame;
    private int _currentMatchingCount;

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

        if (!AuthSession.IsLoggedIn || string.IsNullOrWhiteSpace(AuthSession.UserId))
        {
            await Shell.Current.GoToAsync("Login");
            return;
        }

        await JoinRandomGameAsync(AuthSession.UserId, _selectedRole);
    }

    private async void OnCancelMatchingClicked(object? sender, TappedEventArgs e)
    {
        MatchingStatusLabel.Text = "매칭 취소 중...";
        MatchingActivityIndicator.IsRunning = false;

        await DisconnectRoomUpdatesAsync(removePlayer: true);
        _hasRequestedMatching = false;

        await Shell.Current.GoToAsync("..");
    }

    public void UpdateMatchingCount(int currentCount, int maxCount = 6)
    {
        _currentMatchingCount = Math.Clamp(currentCount, 0, MatchingCapacity);
        MatchingCurrentCountLabel.Text = _currentMatchingCount.ToString();
        MatchingRingCanvas.InvalidateSurface();
    }

    private void OnMatchingRingPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var width = e.Info.Width;
        var height = e.Info.Height;
        var scale = Math.Min(width, height) / 350f;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var radius = Math.Min(width, height) / 2f - (28f * scale);
        var ringRect = new SKRect(
            centerX - radius,
            centerY - radius,
            centerX + radius,
            centerY + radius);

        canvas.Clear(SKColors.Transparent);

        using var outerRingPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 6f * scale,
            IsAntialias = true,
            Color = SKColor.Parse("#151922")
        };
        canvas.DrawCircle(centerX, centerY, radius + (15f * scale), outerRingPaint);

        for (var index = 0; index < MatchingCapacity; index++)
        {
            var isActive = index < _currentMatchingCount;
            var activeColor = index < 3
                ? SKColor.Parse("#159DFF")
                : SKColor.Parse("#FF8A16");
            var startAngle = -90f + (index * SegmentAngle) + (SegmentGapAngle / 2f);
            var sweepAngle = SegmentAngle - SegmentGapAngle;

            using var segmentPath = new SKPath();
            segmentPath.AddArc(ringRect, startAngle, sweepAngle);

            using var basePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 24f * scale,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true,
                Color = isActive ? activeColor : SKColor.Parse("#2B2E34")
            };

            if (isActive)
            {
                using var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 34f * scale,
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true,
                    Color = activeColor.WithAlpha(145),
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9f * scale)
                };
                canvas.DrawPath(segmentPath, glowPaint);
            }

            canvas.DrawPath(segmentPath, basePaint);

            using var highlightPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f * scale,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true,
                Color = isActive
                    ? SKColors.White.WithAlpha(195)
                    : SKColors.White.WithAlpha(32)
            };
            canvas.DrawPath(segmentPath, highlightPaint);
        }

        using var innerRingPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 5f * scale,
            IsAntialias = true,
            Color = SKColor.Parse("#080A0F")
        };
        canvas.DrawCircle(centerX, centerY, radius - (19f * scale), innerRingPaint);
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

            if (serverResponse?.Success == true)
            {
                _roomId = serverResponse.RoomId;
                _isMatched = serverResponse.Matched;
                UpdateMatchingCount(serverResponse.CurrentCount, serverResponse.MaxCount);

                if (!string.IsNullOrWhiteSpace(_roomId)
                    && !string.IsNullOrWhiteSpace(AuthSession.UserId))
                {
                    await StartRoomUpdatesAsync(_roomId, AuthSession.UserId);
                }
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _ = DisconnectRoomUpdatesAsync(removePlayer: !_isMatched);
    }

    private async Task StartRoomUpdatesAsync(string roomId, string userId)
    {
        await DisconnectRoomUpdatesAsync(removePlayer: false);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(AuthSession.ApiBaseUrl), "hubs/game-room"))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<ServerResponse>("RoomStatusUpdated", response =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (response.Success)
                {
                    UpdateMatchingCount(response.CurrentCount, response.MaxCount);
                    _isMatched = response.Matched;

                    if (response.Matched)
                    {
                        _ = NavigateToGameAsync(response);
                    }

                    return;
                }

                if (!string.IsNullOrWhiteSpace(response.Message))
                {
                    MatchingStatusLabel.Text = response.Message;
                    MatchingActivityIndicator.IsRunning = false;
                }
            });
        });

        _hubConnection.On<ServerResponse>("GameStarted", response =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (response.Success && response.Matched)
                {
                    _ = NavigateToGameAsync(response);
                }
            });
        });

        _hubConnection.Reconnected += async _ =>
        {
            if (!string.IsNullOrWhiteSpace(_roomId)
                && !string.IsNullOrWhiteSpace(AuthSession.UserId))
            {
                await _hubConnection.InvokeAsync("JoinRoom", _roomId, AuthSession.UserId);
            }
        };

        await _hubConnection.StartAsync();
        await _hubConnection.InvokeAsync("JoinRoom", roomId, userId);
    }

    private async Task NavigateToGameAsync(ServerResponse response)
    {
        if (_isNavigatingToGame || !response.Success || !response.Matched)
        {
            return;
        }

        _isNavigatingToGame = true;
        _isMatched = true;

        UpdateMatchingCount(response.CurrentCount, response.MaxCount);
        MatchingTitleLabel.Text = "매칭 완료";
        MatchingStatusLabel.Text = "게임을 시작합니다!";
        MatchingActivityIndicator.IsRunning = false;

        await DisconnectRoomUpdatesAsync(removePlayer: false);
        var roomId = Uri.EscapeDataString(_roomId ?? string.Empty);
        var role = Uri.EscapeDataString(_selectedRole.ToString());
        await Shell.Current.GoToAsync($"GamePlay?roomId={roomId}&role={role}&gameType=random");
    }

    private async Task DisconnectRoomUpdatesAsync(bool removePlayer)
    {
        if (_hubConnection == null)
        {
            return;
        }

        var connection = _hubConnection;
        _hubConnection = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(_roomId)
                && connection.State == HubConnectionState.Connected)
            {
                if (removePlayer
                    && !_isMatched
                    && !string.IsNullOrWhiteSpace(AuthSession.UserId))
                {
                    await connection.InvokeAsync("CancelMatching", _roomId, AuthSession.UserId);
                }
                else
                {
                    await connection.InvokeAsync("LeaveRoom", _roomId);
                }
            }

            await connection.DisposeAsync();
        }
        catch
        {
            // Closing the matching page should not surface transport cleanup errors.
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
