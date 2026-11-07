namespace RoastIT.Core.Abstractions;

/// <summary>
/// The only place that knows what "today" is (plan §5 rule 6, §7.4). Business time is Asia/Manila whatever the device's
/// time zone is. Instants elsewhere in the app are UTC <see cref="DateTime"/>.
/// </summary>
public interface IBusinessCalendar
{
    /// <summary>Today's date in business time.</summary>
    DateOnly Today();

    /// <summary>The business date an instant falls on.</summary>
    DateOnly DateOf(DateTime utc);

    /// <summary>The day's start (inclusive) and the next day's start (exclusive) as UTC instants.</summary>
    (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly date);

    /// <summary>For display: a UTC instant as business-local wall-clock time (Kind Unspecified).</summary>
    DateTime ToLocal(DateTime utc);

    /// <summary>A business-local wall-clock time (what a date-time field holds) as a UTC instant.</summary>
    DateTime ToUtc(DateTime local);
}
