using RoastIT.Core.Abstractions;
using RoastIT.Core.Enums;

namespace RoastIT.Core.Rules;

/// <summary>
/// The "late to start" warning on a Pending booking (D38). It is there from the booking's ideal start
/// (pickup − the product's cooking time, the rule the Kitchen load chart uses) and not a minute before.
/// </summary>
public sealed record StartCue(DateTime IdealStartUtc, bool PickupPassed, int MinutesOver, string Title, string Detail, string ShortFlag)
{
    /// <summary>The cue as one phrase for screen readers: "late to start, was due 3:00 PM (20 min ago)".</summary>
    public string Aria => $"{Title.ToLowerInvariant()}, {Detail}";

    /// <summary>The detail sheet's tag: "Late · 20 min", "Due now" or "Pickup passed".</summary>
    public string Flag => PickupPassed ? "Pickup passed" : MinutesOver > 0 ? $"Late · {DurationFormat.Span(MinutesOver)}" : "Due now";

    /// <summary>
    /// The detail sheet's "Start cooking by" value: the ideal start as "3:00 PM" (or "Oct 8 · 2:15 PM" on another day), for a
    /// Pending booking whether or not it is late yet. Null for any other status.
    /// </summary>
    public static string? StartByLabel(BookingRow row, DateTime nowUtc, IBusinessCalendar calendar) =>
        row.Status == ItemStatus.Pending
            ? Label(row.PickupAt - TimeSpan.FromMinutes(row.CookingTimeMin), calendar.DateOf(nowUtc), calendar)
            : null;

    /// <summary>The warning for <paramref name="row"/> at <paramref name="nowUtc"/>, or null when there is nothing to warn about.</summary>
    public static StartCue? For(BookingRow row, DateTime nowUtc, IBusinessCalendar calendar)
    {
        if (row.Status != ItemStatus.Pending) return null;

        var idealStart = row.PickupAt - TimeSpan.FromMinutes(row.CookingTimeMin);
        if (nowUtc < idealStart) return null;

        var today = calendar.DateOf(nowUtc);
        if (nowUtc >= row.PickupAt)
        {
            var since = WholeMinutes(nowUtc - row.PickupAt);
            return new StartCue(idealStart, true, since, "Not started",
                $"pickup was {Label(row.PickupAt, today, calendar)} ({Ago(since)})", "Late");
        }

        var late = WholeMinutes(nowUtc - idealStart);
        return new StartCue(idealStart, false, late, "Late to start",
            $"was due {Label(idealStart, today, calendar)} ({Ago(late)})", "Late");
    }

    private static int WholeMinutes(TimeSpan span) => (int)Math.Floor(span.TotalMinutes);

    private static string Ago(int minutes) => minutes > 0 ? $"{DurationFormat.Span(minutes)} ago" : "now";

    /// <summary>"3:00 PM" today, "Oct 8 · 2:15 PM" on another day.</summary>
    private static string Label(DateTime utc, DateOnly today, IBusinessCalendar calendar)
    {
        var local = calendar.ToLocal(utc);
        return DateOnly.FromDateTime(local) == today ? BookingFormat.Time(local) : BookingFormat.DateTimeShort(local);
    }
}
