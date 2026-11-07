namespace RoastIT.App.Services;

/// <summary>
/// Holds the theme choice (Preferences["theme"]) and applies it natively. The page side
/// (data-theme on &lt;html&gt;) is applied by <c>AppHost</c>, which listens to <see cref="Changed"/>.
/// Light/Dark pin <c>UserAppTheme</c>; System sets it to Unspecified and follows the phone live.
/// </summary>
public sealed class ThemeService(PreferencesStore preferences)
{
    private Application? _app;

    public ThemeMode Mode { get; private set; } = preferences.Theme;

    public bool IsDark => Mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => (_app?.RequestedTheme ?? AppInfo.Current.RequestedTheme) == AppTheme.Dark,
    };

    /// <summary>Raised (possibly off the UI thread) when the page needs to re-apply the theme.</summary>
    public event Action? Changed;

    /// <summary>Called once from <c>App</c> so the native theme is right before the first window shows.</summary>
    public void Attach(Application app)
    {
        _app = app;
        app.RequestedThemeChanged += (_, _) =>
        {
            // Only matters in System mode; Light/Dark ignore the phone's setting.
            if (Mode == ThemeMode.System) Changed?.Invoke();
        };
        ApplyNative();
    }

    public void SetMode(ThemeMode mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        preferences.Theme = mode;
        ApplyNative();
        Changed?.Invoke();
    }

    private void ApplyNative()
    {
        var app = _app;
        if (app is null) return;
        var theme = Mode switch
        {
            ThemeMode.Dark => AppTheme.Dark,
            ThemeMode.Light => AppTheme.Light,
            _ => AppTheme.Unspecified,
        };
        MainThread.BeginInvokeOnMainThread(() => app.UserAppTheme = theme);
    }
}
