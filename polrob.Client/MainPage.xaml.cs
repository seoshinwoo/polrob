namespace polrob.Client;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		AuthSession.Changed -= UpdateAuthHeader;
		AuthSession.Changed += UpdateAuthHeader;
		await AuthSession.LoadAsync();
		UpdateAuthHeader();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		AuthSession.Changed -= UpdateAuthHeader;
	}

	private async void OnLoginClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("Login");
	}

	private async void OnProfileClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("Profile");
	}

	private async void OnCreateClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("GameCreate");
	}

	private async void OnJoinClicked(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync("GameJoin");
	}

	private void UpdateAuthHeader()
	{
		LoginButton.IsVisible = !AuthSession.IsLoggedIn;
		ProfileButton.IsVisible = AuthSession.IsLoggedIn;
		ProfileButton.Text = AuthSession.DisplayName ?? string.Empty;
	}
}
