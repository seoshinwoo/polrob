namespace polrob.Client;

public partial class GameCreate : ContentPage
{
    public GameCreate()
    {
        InitializeComponent();
    }

    private async void OnHomeClicked(object sender, EventArgs e)
    {
        // 명시적으로 animate 파라미터를 true로 설정하여 부드러운 전환을 유도하거나, Navigation.PopAsync 사용
        await Shell.Current.GoToAsync("..", true);
    }

    private void OnRandomClicked(object sender, EventArgs e)
    {
        // Random Join Logic
    }

    private async void OnCustomClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("GameLobby");
    }
}
