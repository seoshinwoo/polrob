namespace polrob.Client;

public partial class GameLobby : ContentPage
{
    public GameLobby()
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

    private void OnPoliceAreaTapped(object sender, EventArgs e)
    {
        // Police 영역 터치 시 처리 (팀 이동)
    }

    private void OnRobberAreaTapped(object sender, EventArgs e)
    {
        // Robber 영역 터치 시 처리 (팀 이동)
    }

    private void OnGameStartClicked(object sender, EventArgs e)
    {
        // 게임 시작 로직 처리
    }

    private void UpdateAuthHeader()
    {
        ProfileButton.IsVisible = AuthSession.IsLoggedIn;
        ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
    }
}
