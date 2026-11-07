using Microsoft.Extensions.Logging;

namespace RoastIT.App.Services;

/// <summary>
/// Typed, failure-tolerant wrapper over MAUI <see cref="IPreferences"/>. A read that fails returns
/// the default, so a broken preference can never stop the app from starting.
/// </summary>
public sealed class PreferencesStore(IPreferences preferences, ILogger<PreferencesStore> logger)
{
    public string? GetString(string key)
    {
        try
        {
            var value = preferences.Get(key, string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read preference {Key}", key);
            return null;
        }
    }

    public void SetString(string key, string value)
    {
        try
        {
            preferences.Set(key, value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save preference {Key}", key);
        }
    }

    public void Remove(string key)
    {
        try
        {
            preferences.Remove(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove preference {Key}", key);
        }
    }

    public ThemeMode Theme
    {
        get => ThemeModes.Parse(GetString(PreferenceKeys.Theme));
        set => SetString(PreferenceKeys.Theme, value.ToKey());
    }
}
