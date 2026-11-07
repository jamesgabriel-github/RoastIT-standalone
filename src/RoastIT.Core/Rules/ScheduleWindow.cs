using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Rules;

/// <summary>
/// Only dates from today − 6 months to today + 6 months can be selected or booked (plan §7.4). Since D32 the window is also
/// what "all dates" means on the Bookings page (list, chips and Dashboard).
/// </summary>
public static class ScheduleWindow
{
    public const int Months = 6;

    public static DateOnly Min(DateOnly today) => today.AddMonths(-Months);

    public static DateOnly Max(DateOnly today) => today.AddMonths(Months);

    public static bool Contains(DateOnly today, DateOnly date) => date >= Min(today) && date <= Max(today);

    /// <summary>The window as UTC instants: the start of its first business day (inclusive) to the end of its last (exclusive).</summary>
    public static (DateTime StartUtc, DateTime EndUtc) RangeUtc(IBusinessCalendar calendar)
    {
        var today = calendar.Today();
        var (start, _) = calendar.DayRangeUtc(Min(today));
        var (_, end) = calendar.DayRangeUtc(Max(today));
        return (start, end);
    }
}
