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

    private async void OnHomeClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", true);
    }

    private async void OnProfileClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private void OnRandomClicked(object sender, EventArgs e)
    {
        if (RoleSelectionLayout.IsVisible)
        {
            ClearRandomSelection();
            return;
        }

        RoleSelectionLayout.IsVisible = true;
    }

    private void OnCustomClicked(object sender, EventArgs e)
    {
        ClearRandomSelection();
        // Custom Join Logic
    }

    private void OnPoliceRoleClicked(object sender, EventArgs e)
    {
        SelectRole(PlayerRole.Police);
    }

    private void OnRobberRoleClicked(object sender, EventArgs e)
    {
        SelectRole(PlayerRole.Robber);
    }

    private async void OnMatchingClicked(object sender, EventArgs e)
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
        ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
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
}
