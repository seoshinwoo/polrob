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
        ClearCustomSelection();

        if (RoleSelectionLayout.IsVisible)
        {
            ClearRandomSelection();
            return;
        }

        RoleSelectionLayout.IsVisible = true;
    }

    private void OnCustomClicked(object? sender, EventArgs e)
    {
        ClearRandomSelection();
        CustomStatusLabel.Text = string.Empty;
        CustomJoinLayout.IsVisible = !CustomJoinLayout.IsVisible;

        if (CustomJoinLayout.IsVisible)
        {
            RoomCodeEntry.Focus();
        }
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
            var response = await httpClient.PostAsJsonAsync(
                "game/join-custom",
                new JoinCustomGameRequest(AuthSession.UserId, roomCode, PlayerRole.Robber));

            if (!response.IsSuccessStatusCode)
            {
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
        ProfileButton.Text = AuthSession.Name ?? string.Empty;
    }

    private void SelectRole(PlayerRole role)
    {
        _selectedRole = role;
        PoliceRoleFrame.BackgroundColor = role == PlayerRole.Police ? Colors.Red : Colors.Transparent;
        RobberRoleFrame.BackgroundColor = role == PlayerRole.Robber ? Colors.Red : Colors.Transparent;
        MatchingButton.IsVisible = true;
    }

    private void ClearRandomSelection()
    {
        _selectedRole = null;
        RoleSelectionLayout.IsVisible = false;
        MatchingButton.IsVisible = false;
        PoliceRoleFrame.BackgroundColor = Colors.Transparent;
        RobberRoleFrame.BackgroundColor = Colors.Transparent;
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

    private sealed record JoinCustomGameRequest(string UserId, string RoomCode, PlayerRole Role);
}
