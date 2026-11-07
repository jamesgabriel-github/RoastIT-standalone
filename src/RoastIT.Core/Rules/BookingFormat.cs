using System.Globalization;

namespace RoastIT.Core.Rules;

/// <summary>Display text for business-local dates and times (bookings spec). Culture-invariant.</summary>
public static class BookingFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"Fri, Oct 9, 2026".</summary>
    public static string LongDate(DateOnly date) => date.ToString("ddd, MMM d, yyyy", Inv);

    /// <summary>"Oct 9".</summary>
    public static string ShortDate(DateOnly date) => date.ToString("MMM d", Inv);

    /// <summary>"Nov 7, 2026".</summary>
    public static string MediumDate(DateOnly date) => date.ToString("MMM d, yyyy", Inv);

    /// <summary>"October 2026".</summary>
    public static string MonthYear(DateOnly date) => date.ToString("MMMM yyyy", Inv);

    /// <summary>"5:00 PM".</summary>
    public static string Time(DateTime local) => local.ToString("h:mm tt", Inv);

    /// <summary>"5 PM" (the kitchen chart's hour ticks).</summary>
    public static string Hour(DateTime local) => local.ToString("h tt", Inv);

    /// <summary>"Oct 9 · 5:00 PM".</summary>
    public static string DateTimeShort(DateTime local) => $"{local.ToString("MMM d", Inv)} · {Time(local)}";

    /// <summary>The "Showing …" value of the scope line (bookings spec §5).</summary>
    public static string ScopeLabel(DateOnly? selected, DateOnly today)
    {
        if (selected is not { } date) return "all dates";
        if (date == today) return "Today";
        if (date == today.AddDays(-1)) return "Yesterday";
        if (date == today.AddDays(1)) return "Tomorrow";
        return LongDate(date);
    }

    public static string BookingCount(int n) => n == 1 ? "1 booking" : $"{n} bookings";

    /// <summary>"Walk-in customer" when the name is blank.</summary>
    public static string CustomerLabel(string? customer) =>
        string.IsNullOrWhiteSpace(customer) ? "Walk-in customer" : customer.Trim();
}
