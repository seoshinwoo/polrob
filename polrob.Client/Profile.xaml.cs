namespace polrob.Client;

public partial class Profile : ContentPage
{
    public Profile()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await AuthSession.LoadAsync();

        if (!AuthSession.IsLoggedIn)
        {
            await Shell.Current.GoToAsync("//MainPage");
            return;
        }

        NameLabel.Text = AuthSession.Name ?? string.Empty;
        UserIdLabel.Text = AuthSession.UserId ?? string.Empty;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", true);
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        await AuthSession.LogoutAsync();
        await Shell.Current.GoToAsync("//MainPage");
    }
}
