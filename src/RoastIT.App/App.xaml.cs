using RoastIT.App.Services;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Services;

namespace RoastIT.App;

public partial class App : Application
{
	private readonly IAppLock _appLock;
	private readonly System.Threading.Timer _idleTimer;

	public App(ThemeService theme, IAppLock appLock, IdleMonitor idleMonitor)
	{
		InitializeComponent();
		_appLock = appLock;

		// Sets UserAppTheme from Preferences before the first window shows.
		theme.Attach(this);

		// Auto-lock after 10 minutes without taps (plan §9.4). The page reports activity; this checks.
		_idleTimer = new System.Threading.Timer(_ => idleMonitor.CheckAndLock(), null,
			TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "Roastline" };

		// The app locks on every reopen: leaving the foreground locks it, and so does coming back.
		window.Stopped += (_, _) => _appLock.Lock();
		window.Resumed += (_, _) => _appLock.Lock();
		return window;
	}
}
