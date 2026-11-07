using Microsoft.Extensions.Logging;
using RoastIT.App.Services;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Services;

namespace RoastIT.App;

public partial class App : Application
{
	private static readonly TimeSpan CsvCheckInterval = TimeSpan.FromMinutes(1);

	private readonly IAppLock _appLock;
	private readonly ExternalActivityGuard _guard;
	private readonly CsvBackupService _csv;
	private readonly ILogger<App> _logger;
	private readonly System.Threading.Timer _idleTimer;
	private readonly object _csvGate = new();
	private CancellationTokenSource? _csvLoop;

	public App(ThemeService theme, IAppLock appLock, IdleMonitor idleMonitor, ExternalActivityGuard guard, CsvBackupService csv, ILogger<App> logger)
	{
		InitializeComponent();
		_appLock = appLock;
		_guard = guard;
		_csv = csv;
		_logger = logger;

		// Sets UserAppTheme from Preferences before the first window shows.
		theme.Attach(this);

		// Auto-lock after 10 minutes without taps (plan §9.4). The page reports activity; this checks.
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

		// The app locks on every reopen: leaving the foreground locks it, and so does coming back (D6). The file picker and
		// share sheet are the exception: ExternalActivityGuard lets the expected trip there and back through (Q11).
		window.Stopped += (_, _) =>
		{
			StopCsvLoop(); // nothing runs in the background (D27)
			if (!_guard.OnStopped()) _appLock.Lock();
		};
		window.Resumed += (_, _) =>
		{
			if (!_guard.OnResumed()) _appLock.Lock();
			StartCsvLoop();
		};
		window.Activated += (_, _) => _guard.OnActivated();

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
