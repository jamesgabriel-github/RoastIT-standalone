namespace RoastIT.App.Services;

public enum ThemeMode { Light, Dark, System }

public static class ThemeModes
{
    public static string ToKey(this ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => "dark",
        ThemeMode.System => "system",
        _ => "light",
    };

    /// <summary>Anything unreadable or unknown is Light (shared-ui-spec §2).</summary>
    public static ThemeMode Parse(string? key) => key switch
    {
        "dark" => ThemeMode.Dark,
        "system" => ThemeMode.System,
        _ => ThemeMode.Light,
    };
}
