namespace polrob.Client;

public partial class GameOver : ContentPage
{
    public GameOver()
    {
        InitializeComponent();
    }

    private async void OnHomeClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", true);
    }

    private void OnPlayAgainClicked(object sender, EventArgs e)
    {
        // 다시 플레이 로직
    }
}