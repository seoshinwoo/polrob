namespace polrob.Client;

public partial class GameLobby : ContentPage
{
    public GameLobby()
    {
        InitializeComponent();
    }

    private async void OnHomeClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", true);
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
}