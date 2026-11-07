using RoastIT.Core.Abstractions;
using RoastIT.Core.Rules;

namespace RoastIT.Core.Services;

/// <summary>
/// The Dashboard tab's numbers for one pickup date or all dates (plan §7.6). "All dates" is the schedulable window, today ± 6
/// months (D32). Read-only. Money and product counts come from SQL over the scope (D21) instead of loading every booking; only
/// a selected date's own bookings are loaded, for the status breakdown and the kitchen load.
/// </summary>
public sealed class DashboardService(IBookingStore store, IAppLock appLock, IClock clock, IBusinessCalendar calendar)
{
    public async Task<DashboardData> GetAsync(DateOnly? date, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        if (date is not { } d)
        {
            var (from, to) = ScheduleWindow.RangeUtc(calendar);
            return Dashboard.Compose(await store.GetDashboardSourceAsync(from, to, ct), null, null, calendar, clock.UtcNow);
        }

        var (start, end) = calendar.DayRangeUtc(d);
        var source = await store.GetDashboardSourceAsync(start, end, ct);
        var dayRows = BookingRows.WithOrderInfo(await store.ListRowsByPickupAsync(start, end, ct));
        return Dashboard.Compose(source, dayRows, d, calendar, clock.UtcNow);
    }
}
