using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoastIT.App.Services;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Services;
using RoastIT.Data;
using RoastIT.Data.Repositories;
using Serilog;

namespace RoastIT.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		VersionTracking.Track();

		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		builder.Services.AddMauiBlazorWebView();

		ConfigureLogging(builder);
		ConfigureData(builder, out var dbPath);

		builder.Services.AddSingleton<IClock, SystemClock>();

		// App lock (Phase 1). Singletons: the locked state and PIN windows live in memory.
		builder.Services.AddSingleton<IAppLockStore, EfAppLockStore>();
		builder.Services.AddSingleton<IAppLock, AppLockService>();
		builder.Services.AddSingleton<IdleMonitor>();
		builder.Services.AddSingleton<IAppBackgrounder, AppBackgrounder>();
		builder.Services.AddSingleton<PinPromptService>();

		// Bookings (Phase 3). Business time is always Asia/Manila, whatever the device's time zone is.
		builder.Services.AddSingleton<IBusinessCalendar>(sp => new BusinessCalendar(sp.GetRequiredService<IClock>()));
		builder.Services.AddSingleton<IBookingStore, EfBookingStore>();
		builder.Services.AddSingleton<BookingScopeState>();
		builder.Services.AddScoped<BookingService>();
		builder.Services.AddScoped<OrderService>();
		builder.Services.AddScoped<DashboardService>();

		// Menu catalog (Phase 2). The notifier is a singleton so every open screen hears the same events.
		builder.Services.AddSingleton<IChangeNotifier, ChangeNotifier>();
		builder.Services.AddSingleton<IProductStore, EfProductStore>();
		builder.Services.AddScoped<ProductService>();

		// Settings → Data (Phase 4): export/import (plan §10.5) and the automatic CSV backup (§10.6, Android only).
		builder.Services.AddSingleton<IBackupStore>(sp => new SqliteBackupStore(
			sp.GetRequiredService<IDbContextFactory<RoastItDbContext>>(),
			dbPath,
			AppInfo.Current.VersionString,
			sp.GetRequiredService<ILogger<SqliteBackupStore>>()));
		builder.Services.AddScoped<BackupService>();
		builder.Services.AddScoped<DataTransferService>();
#if ANDROID
		builder.Services.AddSingleton<IBackupFileSaver, AndroidBackupFileSaver>();
		builder.Services.AddSingleton<AndroidCsvFileStore>();
		builder.Services.AddSingleton<ICsvFileStore>(sp => sp.GetRequiredService<AndroidCsvFileStore>());
		builder.Services.AddSingleton<ICsvFileShare>(sp => sp.GetRequiredService<AndroidCsvFileStore>());
#else
		builder.Services.AddSingleton<IBackupFileSaver, UnsupportedBackupFileSaver>();
		builder.Services.AddSingleton<UnsupportedCsvFileStore>();
		builder.Services.AddSingleton<ICsvFileStore>(sp => sp.GetRequiredService<UnsupportedCsvFileStore>());
		builder.Services.AddSingleton<ICsvFileShare>(sp => sp.GetRequiredService<UnsupportedCsvFileStore>());
#endif
		builder.Services.AddSingleton<ICsvBackupLog, PreferencesCsvBackupLog>();
		builder.Services.AddSingleton<CsvBackupService>();

		builder.Services.AddSingleton<IPreferences>(_ => Preferences.Default);
		builder.Services.AddSingleton<PreferencesStore>();
		builder.Services.AddSingleton<ThemeService>();
		builder.Services.AddSingleton<BackNavigationService>();
		builder.Services.AddSingleton<ToastService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
#endif

		var app = builder.Build();

		// Before the first page loads: back up (if migrating), migrate, WAL (plan §6.3).
		// A failed migration must stop the app rather than run on a half-migrated database.
		var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
		Task.Run(() => initializer.InitializeAsync(
			AppInfo.Current.VersionString,
			VersionTracking.Default.PreviousVersion)).GetAwaiter().GetResult();

		return app;
	}

	private static void ConfigureLogging(MauiAppBuilder builder)
	{
		// Debug aid only. audit_logs in SQLite is the business history. Never log the PIN,
		// its hash or the recovery code.
		var logPath = Path.Combine(FileSystem.AppDataDirectory, "logs", "roastit-.log");
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
			.CreateLogger();

		builder.Logging.AddSerilog(Log.Logger, dispose: true);
#if DEBUG
		builder.Logging.AddDebug();
#endif

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating={Terminating})", e.IsTerminating);
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			Log.Error(e.Exception, "Unobserved task exception");
			e.SetObserved();
		};
	}

	private static void ConfigureData(MauiAppBuilder builder, out string dbPath)
	{
		var path = dbPath = Path.Combine(FileSystem.AppDataDirectory, "roastit.db3");

		builder.Services.AddDbContextFactory<RoastItDbContext>(o =>
			o.UseSqlite($"Data Source={path};Foreign Keys=True"));

		builder.Services.AddSingleton(sp => new DatabaseInitializer(
			sp.GetRequiredService<IDbContextFactory<RoastItDbContext>>(),
			path,
			sp.GetRequiredService<ILogger<DatabaseInitializer>>()));
	}
}
