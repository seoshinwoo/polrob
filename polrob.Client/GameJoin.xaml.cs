namespace polrob.Client;

public partial class GameJoin : ContentPage
{
    public GameJoin()
    {
        InitializeComponent();
    }

    private async void OnHomeClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", true);
    }

    private void OnRandomClicked(object sender, EventArgs e)
    {
        // Random Join Logic
    }

    private void OnCustomClicked(object sender, EventArgs e)
    {
        // Custom Join Logic
    }
}