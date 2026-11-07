namespace RoastIT.Core.Rules;

/// <summary>Only dates from today − 6 months to today + 6 months can be selected or booked (plan §7.4).</summary>
public static class ScheduleWindow
{
    public const int Months = 6;

    public static DateOnly Min(DateOnly today) => today.AddMonths(-Months);

    public static DateOnly Max(DateOnly today) => today.AddMonths(Months);

    public static bool Contains(DateOnly today, DateOnly date) => date >= Min(today) && date <= Max(today);
}
