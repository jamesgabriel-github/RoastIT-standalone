using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Services;

/// <summary>Business time from <see cref="IClock"/> and a named time zone (<c>Asia/Manila</c> in the app).</summary>
public sealed class BusinessCalendar(IClock clock, string timeZoneId = BusinessCalendar.DefaultTimeZoneId) : IBusinessCalendar
{
    public const string DefaultTimeZoneId = "Asia/Manila";

    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    public DateOnly Today() => DateOf(clock.UtcNow);

    public DateOnly DateOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly date)
    {
        var start = ToUtc(date.ToDateTime(TimeOnly.MinValue));
        var end = ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return (start, end);
    }

    public DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), _zone), DateTimeKind.Unspecified);

    public DateTime ToUtc(DateTime local) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone), DateTimeKind.Utc);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
