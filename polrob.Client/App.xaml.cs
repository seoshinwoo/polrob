using Microsoft.Extensions.DependencyInjection;

namespace polrob.Client;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
#if ANDROID
		return new Window(new AndroidStartupSplashPage());
#else
		return new Window(new AppShell());
#endif
	}
}
