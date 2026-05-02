namespace polrob.Client;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("PlayPage", typeof(PlayPage));
	}
}
