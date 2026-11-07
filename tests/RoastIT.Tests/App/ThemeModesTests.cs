using RoastIT.App.Services;

namespace RoastIT.Tests.App;

public class ThemeModesTests
{
    [Theory]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("system", ThemeMode.System)]
    public void Known_keys_round_trip(string key, ThemeMode mode)
    {
        Assert.Equal(mode, ThemeModes.Parse(key));
        Assert.Equal(key, mode.ToKey());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sepia")]
    [InlineData("DARK")]
    public void Anything_unreadable_or_unknown_is_light(string? key)
    {
        Assert.Equal(ThemeMode.Light, ThemeModes.Parse(key));
    }
}
