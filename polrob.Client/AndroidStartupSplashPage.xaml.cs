namespace polrob.Client;

public partial class AndroidStartupSplashPage : ContentPage
{
    private bool _hasNavigated;

    public AndroidStartupSplashPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_hasNavigated)
        {
            return;
        }

        _hasNavigated = true;
        await Task.Delay(1500);

        if (Application.Current?.Windows.Count > 0)
        {
            Application.Current.Windows[0].Page = new AppShell();
        }
    }
}
