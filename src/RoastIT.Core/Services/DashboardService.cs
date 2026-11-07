using RoastIT.Core.Abstractions;
using RoastIT.Core.Rules;

namespace RoastIT.Core.Services;

/// <summary>The Dashboard tab's numbers for one pickup date or all dates (plan §7.6). Read-only.</summary>
public sealed class DashboardService(IBookingStore store, IAppLock appLock, IClock clock, IBusinessCalendar calendar)
{
    public async Task<DashboardData> GetAsync(DateOnly? date, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        var rows = BookingRows.WithOrderInfo(await store.ListRowsAsync(ct));
        return Dashboard.Build(rows, date, calendar, clock.UtcNow);
    }
}
