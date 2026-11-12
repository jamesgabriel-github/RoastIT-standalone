using Microsoft.Extensions.Logging;
using RoastIT.App.Services;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Services;

namespace RoastIT.App;

public partial class App : Application
{
	private static readonly TimeSpan CsvCheckInterval = TimeSpan.FromMinutes(1);

	private readonly IAppLock _appLock;
	private readonly IdleMonitor _idleMonitor;
	private readonly CsvBackupService _csv;
	private readonly ILogger<App> _logger;
	private readonly System.Threading.Timer _idleTimer;
	private readonly object _csvGate = new();
	private CancellationTokenSource? _csvLoop;

	public App(ThemeService theme, IAppLock appLock, IdleMonitor idleMonitor, CsvBackupService csv, ILogger<App> logger)
	{
		InitializeComponent();
		_appLock = appLock;
		_idleMonitor = idleMonitor;
		_csv = csv;
		_logger = logger;

		// Sets UserAppTheme from Preferences before the first window shows.
		theme.Attach(this);

		// Auto-lock after 30 minutes without taps (plan §9.4). The page reports activity; this checks.
		_idleTimer = new System.Threading.Timer(_ => idleMonitor.CheckAndLock(), null,
			TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

		// The CSV check is skipped while locked; unlocking catches up straight away (plan §10.6).
		_appLock.LockStateChanged += () =>
		{
			if (!_appLock.IsLocked) _ = Task.Run(CheckCsvAsync);
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "RoastIT" };

		// Switching to another app does not lock (D37). The app locks when it is closed (the window is destroyed; a
		// killed process starts locked anyway) and after 30 minutes idle. Time away counts as idle, so coming back
		// checks the idle time straight away instead of waiting for the next timer tick.
		window.Stopped += (_, _) => StopCsvLoop(); // nothing runs in the background (D27)
		window.Resumed += (_, _) =>
		{
			_idleMonitor.CheckAndLock();
			StartCsvLoop();
		};
		window.Destroying += (_, _) =>
		{
			StopCsvLoop();
			_appLock.Lock();
		};

		StartCsvLoop();
		return window;
	}

	// ---- Automatic CSV backup: hourly, only while the app is in the foreground (plan §10.6, D29) ----

	private void StartCsvLoop()
	{
		CancellationTokenSource cts;
		lock (_csvGate)
		{
			_csvLoop?.Cancel();
			_csvLoop = cts = new CancellationTokenSource();
		}
		_ = Task.Run(() => RunCsvLoopAsync(cts.Token));
	}

	private void StopCsvLoop()
	{
		lock (_csvGate)
		{
			_csvLoop?.Cancel();
			_csvLoop = null;
		}
	}

	private async Task RunCsvLoopAsync(CancellationToken ct)
	{
		try
		{
			await CheckCsvAsync();
			using var timer = new PeriodicTimer(CsvCheckInterval);
			while (await timer.WaitForNextTickAsync(ct)) await CheckCsvAsync();
		}
		catch (OperationCanceledException)
		{
			// The window stopped.
		}
	}

	private async Task CheckCsvAsync()
	{
		try
		{
			await _csv.CheckAsync(); // never throws by design; a save in progress is never cancelled half-way
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "csv.save_failed (check)");
		}
	}
}
