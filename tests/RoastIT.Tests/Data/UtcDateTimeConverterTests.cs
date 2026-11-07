using RoastIT.Data;

namespace RoastIT.Tests.Data;

public class UtcDateTimeConverterTests
{
    private readonly UtcDateTimeConverter _converter = new();

    [Fact]
    public void Local_values_are_written_as_utc()
    {
        var local = new DateTime(2026, 10, 9, 17, 0, 0, DateTimeKind.Local);

        var stored = (DateTime)_converter.ConvertToProvider(local)!;

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(local.ToUniversalTime(), stored);
    }

    [Fact]
    public void Utc_values_are_written_unchanged()
    {
        var utc = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);

        var stored = (DateTime)_converter.ConvertToProvider(utc)!;

        Assert.Equal(utc, stored);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
    }

    [Fact]
    public void Values_read_back_are_marked_utc()
    {
        var unspecified = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Unspecified);

        var read = (DateTime)_converter.ConvertFromProvider(unspecified)!;

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(unspecified.Ticks, read.Ticks);
    }
}
