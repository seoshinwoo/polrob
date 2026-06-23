using System.Net;
using System.Net.Http.Json;
using Microsoft.Maui.Storage;
using polrob.Shared;

namespace polrob.Client;

public partial class GameJoin : ContentPage
{
    private PlayerRole? _selectedRole;
    private bool _roleImagesLoaded;

    public GameJoin()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AuthSession.LoadAsync();
        await LoadRoleImagesAsync();
        UpdateAuthHeader();
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", true);
    }

    private async void OnProfileClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private void OnRandomClicked(object? sender, EventArgs e)
    {
        ClearRandomSelection();
        ClearCustomSelection();
        ModeSelectionLayout.IsVisible = false;
        RandomJoinLayout.IsVisible = true;
        ChangeMethodButton.IsVisible = true;
    }

    private void OnCustomClicked(object? sender, EventArgs e)
    {
        ClearRandomSelection();
        ClearCustomSelection();
        ModeSelectionLayout.IsVisible = false;
        CustomJoinLayout.IsVisible = true;
        ChangeMethodButton.IsVisible = true;

        Dispatcher.Dispatch(() => RoomCodeEntry.Focus());
    }

    private void OnChangeMethodClicked(object? sender, EventArgs e)
    {
        ClearRandomSelection();
        ClearCustomSelection();
        RoomCodeEntry.Text = string.Empty;
        ModeSelectionLayout.IsVisible = true;
        ChangeMethodButton.IsVisible = false;
    }

    private async void OnJoinCustomClicked(object? sender, EventArgs e)
    {
        await AuthSession.LoadAsync();
        if (!AuthSession.IsLoggedIn || string.IsNullOrWhiteSpace(AuthSession.UserId))
        {
            await Shell.Current.GoToAsync("Login");
            return;
        }

        var roomCode = NormalizeRoomCode(RoomCodeEntry.Text);
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            CustomStatusLabel.Text = "방 코드를 입력해 주세요.";
            return;
        }

        try
        {
            JoinCustomButton.IsEnabled = false;
            CustomStatusLabel.Text = "입장 중...";

            using var httpClient = new HttpClient { BaseAddress = new Uri(AuthSession.ApiBaseUrl) };
            AuthSession.ApplyAuthorization(httpClient);
            var response = await httpClient.PostAsJsonAsync(
                "game/join-custom",
                new JoinCustomGameRequest(roomCode, PlayerRole.Robber));

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    AuthSession.ClearLocalSession();
                    CustomStatusLabel.Text = "로그인 세션이 만료되었습니다. 다시 로그인해주세요.";
                    await Shell.Current.GoToAsync("Login");
                    return;
                }

                CustomStatusLabel.Text = await ReadErrorMessageAsync(response);
                return;
            }

            var serverResponse = await response.Content.ReadFromJsonAsync<ServerResponse>();
            if (serverResponse?.Success != true || string.IsNullOrWhiteSpace(serverResponse.RoomId))
            {
                CustomStatusLabel.Text = serverResponse?.Message ?? "방에 입장할 수 없습니다.";
                return;
            }

            var roomId = Uri.EscapeDataString(serverResponse.RoomId);
            var encodedRoomCode = Uri.EscapeDataString(serverResponse.RoomCode ?? roomCode);
            var role = serverResponse.Role ?? PlayerRole.Robber;
            await Shell.Current.GoToAsync($"GameLobby?roomId={roomId}&roomCode={encodedRoomCode}&role={role}&isHost=false");
        }
        catch (HttpRequestException)
        {
            CustomStatusLabel.Text = "서버에 연결할 수 없습니다.";
        }
        catch (Exception ex)
        {
            CustomStatusLabel.Text = $"입장 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            JoinCustomButton.IsEnabled = true;
        }
    }

    private void OnRoomCodeTextChanged(object? sender, TextChangedEventArgs e)
    {
        var normalized = NormalizeRoomCode(e.NewTextValue);
        if (RoomCodeEntry.Text != normalized)
        {
            RoomCodeEntry.Text = normalized;
        }
    }

    private void OnPoliceRoleClicked(object? sender, EventArgs e)
    {
        SelectRole(PlayerRole.Police);
    }

    private void OnRobberRoleClicked(object? sender, EventArgs e)
    {
        SelectRole(PlayerRole.Robber);
    }

    private async void OnMatchingClicked(object? sender, EventArgs e)
    {
        if (_selectedRole is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"GameMatching?role={_selectedRole}");
    }

    private void UpdateAuthHeader()
    {
        ProfileButton.IsVisible = AuthSession.IsLoggedIn;
        ProfileNameLabel.Text = AuthSession.Name ?? string.Empty;
    }

    private void SelectRole(PlayerRole role)
    {
        _selectedRole = role;
        PoliceRoleFrame.BackgroundColor = role == PlayerRole.Police
            ? Color.FromArgb("#145FAD")
            : Color.FromArgb("#132D56");
        RobberRoleFrame.BackgroundColor = role == PlayerRole.Robber
            ? Color.FromArgb("#9B3518")
            : Color.FromArgb("#132D56");
        PoliceRoleFrame.Stroke = role == PlayerRole.Police
            ? Color.FromArgb("#FFD95A")
            : Color.FromArgb("#506B91");
        RobberRoleFrame.Stroke = role == PlayerRole.Robber
            ? Color.FromArgb("#FFD95A")
            : Color.FromArgb("#506B91");
        MatchingButton.IsEnabled = true;
        MatchingButton.Opacity = 1;
    }

    private void ClearRandomSelection()
    {
        _selectedRole = null;
        RandomJoinLayout.IsVisible = false;
        MatchingButton.IsEnabled = false;
        MatchingButton.Opacity = 0.55;
        PoliceRoleFrame.BackgroundColor = Color.FromArgb("#132D56");
        RobberRoleFrame.BackgroundColor = Color.FromArgb("#132D56");
        PoliceRoleFrame.Stroke = Color.FromArgb("#506B91");
        RobberRoleFrame.Stroke = Color.FromArgb("#506B91");
    }

    private void ClearCustomSelection()
    {
        CustomJoinLayout.IsVisible = false;
        CustomStatusLabel.Text = string.Empty;
    }

    private async Task LoadRoleImagesAsync()
    {
        if (_roleImagesLoaded)
        {
            return;
        }

        PoliceRoleImage.Source = await LoadPackageImageSourceAsync("police-icon.png");
        RobberRoleImage.Source = await LoadPackageImageSourceAsync("robber-icon.png");
        _roleImagesLoaded = true;
    }

    private static async Task<ImageSource> LoadPackageImageSourceAsync(string fileName)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);

        var imageBytes = memoryStream.ToArray();
        return ImageSource.FromStream(() => new MemoryStream(imageBytes));
    }

    private static string NormalizeRoomCode(string? roomCode)
    {
        return (roomCode ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var message = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(message)
            ? $"요청이 실패했습니다. ({(int)response.StatusCode})"
            : message.Trim('"');
    }

    private sealed record JoinCustomGameRequest(string RoomCode, PlayerRole Role);
}
