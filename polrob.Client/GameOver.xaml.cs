namespace polrob.Client;

public partial class GameOver : ContentPage
{
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
        await Shell.Current.GoToAsync("..", true);
    }

    private async void OnProfileClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("Profile");
    }

    private void OnPlayAgainClicked(object sender, EventArgs e)
    {
        // 다시 플레이 로직
    }

    private void UpdateAuthHeader()
    {
        ProfileButton.IsVisible = AuthSession.IsLoggedIn;
        ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
    }
}
