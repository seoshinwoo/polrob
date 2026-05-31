namespace polrob.Client;

public partial class GameJoin : ContentPage
{
    public GameJoin()
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
        await Shell.Current.GoToAsync("..", true);
    }

    private async void OnProfileClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private void OnRandomClicked(object sender, EventArgs e)
    {
        // Random Join Logic
    }

    private void OnCustomClicked(object sender, EventArgs e)
    {
        // Custom Join Logic
    }

    private void UpdateAuthHeader()
    {
        ProfileButton.IsVisible = AuthSession.IsLoggedIn;
        ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
    }
}
