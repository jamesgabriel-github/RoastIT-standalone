using RoastIT.Core.Abstractions;
using RoastIT.Core.Enums;

namespace RoastIT.Core.Rules;

public sealed record StatusSlice(ItemStatus Status, int Count);

public sealed record ProductMixRow(Guid ProductId, string Name, int Count);

public sealed record BalanceDueRow(Guid OrderKey, string CustomerLabel, DateTime PickupAt, long Balance);

/// <summary>One bar of the kitchen chart: how many items are cooking between two UTC instants.</summary>
public sealed record KitchenBar(DateTime StartUtc, DateTime EndUtc, int Count);

/// <summary>
/// Everything the Dashboard tab shows for one scope (bookings spec §10).
/// <see cref="StatusBreakdown"/> is null unless a date is selected (statuses with a count of 0 are left out).
/// <see cref="KitchenLoad"/> is null unless the selected date is today or later; an empty list means nothing is scheduled.
/// <see cref="BalanceDueTop"/> holds the five largest balances, largest first.
/// </summary>
public sealed record DashboardData(
    int TotalBookings,
    long Revenue,
    long Collected,
    long BalanceDue,
    long AverageOrderValue,
    IReadOnlyList<StatusSlice>? StatusBreakdown,
    IReadOnlyList<KitchenBar>? KitchenLoad,
    IReadOnlyList<ProductMixRow> ProductMix,
    IReadOnlyList<BalanceDueRow> BalanceDueTop,
    int BalanceDueMore);

/// <summary>One order's money as stored, with whether every one of its bookings is cancelled (D23).</summary>
public sealed record OrderTotalsRow(
    Guid OrderKey,
    string? Customer,
    DateTime PickupAt,
    long TotalAmount,
    long DiscountAmount,
    long AmountPaid,
    bool FullyCancelled,
    string FirstBookingId)
{
    public long NetTotal => OrderMath.NetTotal(TotalAmount, DiscountAmount);

    public long Balance => OrderMath.Balance(TotalAmount, DiscountAmount, AmountPaid);
}

/// <summary>How many bookings of one product have one status.</summary>
public sealed record ProductStatusCount(Guid ProductId, string ProductName, ItemStatus Status, int Count);

/// <summary>What SQL hands the Dashboard for one scope (D21).</summary>
public sealed record DashboardSource(IReadOnlyList<OrderTotalsRow> Orders, IReadOnlyList<ProductStatusCount> Counts);

public static class Dashboard
{
    public const int BalanceDueRows = 5;

    /// <summary>
    /// The Dashboard from per-order totals and per-product counts (D21), the same numbers <see cref="Build"/> gives from
    /// every booking. <paramref name="dayRows"/> are the selected date's bookings with order info (for the status breakdown
    /// and the kitchen load); null for all dates.
    /// </summary>
    public static DashboardData Compose(DashboardSource source, IReadOnlyList<BookingRow>? dayRows, DateOnly? date, IBusinessCalendar calendar, DateTime nowUtc)
    {
        var orders = source.Orders.Where(o => !o.FullyCancelled).ToList();
        var revenue = orders.Sum(o => o.NetTotal);
        var collected = orders.Sum(o => o.AmountPaid);
        var balanceDue = orders.Sum(o => o.Balance);
        var average = orders.Count == 0 ? 0 : (revenue + orders.Count / 2) / orders.Count;

        IReadOnlyList<StatusSlice>? breakdown = null;
        IReadOnlyList<KitchenBar>? kitchen = null;
        if (date is { } d && dayRows is not null)
        {
            breakdown = StatusBreakdown(dayRows);
            if (d >= calendar.DateOf(nowUtc)) kitchen = KitchenLoad.Compute(dayRows, nowUtc);
        }

        var mix = source.Counts.Where(c => c.Status != ItemStatus.Cancelled)
            .GroupBy(c => (c.ProductId, c.ProductName))
            .Select(g => new ProductMixRow(g.Key.ProductId, g.Key.ProductName, g.Sum(c => c.Count)))
            .OrderByDescending(m => m.Count).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var owing = orders.Where(o => o.Balance > 0)
            .OrderByDescending(o => o.Balance).ThenBy(o => o.PickupAt).ThenBy(o => o.FirstBookingId, StringComparer.Ordinal)
            .Select(o => new BalanceDueRow(o.OrderKey, BookingFormat.CustomerLabel(o.Customer), o.PickupAt, o.Balance))
            .ToList();

        return new DashboardData(source.Counts.Sum(c => c.Count), revenue, collected, balanceDue, average, breakdown, kitchen, mix,
            owing.Take(BalanceDueRows).ToList(), Math.Max(0, owing.Count - BalanceDueRows));
    }

    private static List<StatusSlice> StatusBreakdown(IEnumerable<BookingRow> rows)
    {
        var list = rows.ToList();
        return Enum.GetValues<ItemStatus>()
            .Select(s => new StatusSlice(s, list.Count(r => r.Status == s)))
            .Where(s => s.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Builds the Dashboard for a pickup date, or for all dates when <paramref name="date"/> is null. Money figures use
    /// <see cref="OrderMath"/> and skip orders whose bookings are all cancelled (D23). Rows must already carry their order info.
    /// </summary>
    public static DashboardData Build(IEnumerable<BookingRow> allRows, DateOnly? date, IBusinessCalendar calendar, DateTime nowUtc)
    {
        var rows = allRows.Where(r => date is null || calendar.DateOf(r.PickupAt) == date).ToList();

        // Money is per order, so count each order once (its first booking by ID stands for it).
        var orders = rows.Where(r => !r.OrderCancelled).GroupBy(r => r.OrderKey)
            .Select(g => g.OrderBy(r => r.BookingId, StringComparer.Ordinal).First()).ToList();
        var revenue = orders.Sum(o => o.NetTotal);
        var collected = orders.Sum(o => o.AmountPaid);
        var balanceDue = orders.Sum(o => o.Balance);
        var average = orders.Count == 0 ? 0 : (revenue + orders.Count / 2) / orders.Count; // rounded to the centavo

        IReadOnlyList<StatusSlice>? breakdown = date is not null ? StatusBreakdown(rows) : null;

        IReadOnlyList<KitchenBar>? kitchen = null;
        if (date is { } d && d >= calendar.DateOf(nowUtc))
            kitchen = KitchenLoad.Compute(rows, nowUtc);

        // Ties in the product mix fall back to name A → Z (the catalog has no manual order).
        var mix = rows.Where(r => r.Status != ItemStatus.Cancelled)
            .GroupBy(r => (r.ProductId, r.ProductName))
            .Select(g => new ProductMixRow(g.Key.ProductId, g.Key.ProductName, g.Count()))
            .OrderByDescending(m => m.Count).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var owing = orders.Where(o => o.Balance > 0)
            .OrderByDescending(o => o.Balance).ThenBy(o => o.PickupAt).ThenBy(o => o.BookingId, StringComparer.Ordinal)
            .Select(o => new BalanceDueRow(o.OrderKey, o.CustomerLabel, o.PickupAt, o.Balance))
            .ToList();

        return new DashboardData(rows.Count, revenue, collected, balanceDue, average, breakdown, kitchen, mix,
            owing.Take(BalanceDueRows).ToList(), Math.Max(0, owing.Count - BalanceDueRows));
    }
}

/// <summary>The kitchen load chart's data (plan §7.6, bookings spec §11.1).</summary>
public static class KitchenLoad
{
    /// <summary>
    /// Pending and Cooking items only. A cooking item occupies [started, started + cooking time). A pending item occupies
    /// [pickup − cooking time, pickup), or [now, now + cooking time) when that start has already passed (it is running late).
    /// Every item occupies at least one minute. Each gap between two event times is one bar whose height is the running total.
    /// </summary>
    public static IReadOnlyList<KitchenBar> Compute(IEnumerable<BookingRow> rows, DateTime nowUtc)
    {
        var events = new SortedDictionary<DateTime, int>();
        void Add(DateTime at, int delta) => events[at] = events.GetValueOrDefault(at) + delta;

        foreach (var r in rows)
        {
            DateTime start, end;
            var cooking = TimeSpan.FromMinutes(r.CookingTimeMin);
            if (r.Status == ItemStatus.Cooking)
            {
                start = r.StartedCookingAt ?? nowUtc;
                end = start + cooking;
            }
            else if (r.Status == ItemStatus.Pending)
            {
                start = r.PickupAt - cooking;
                end = r.PickupAt;
                if (start < nowUtc) // running late
                {
                    start = nowUtc;
                    end = nowUtc + cooking;
                }
            }
            else continue;

            if (end < start + TimeSpan.FromMinutes(1)) end = start + TimeSpan.FromMinutes(1);
            Add(start, +1);
            Add(end, -1);
        }

        var times = events.Keys.ToList();
        var bars = new List<KitchenBar>();
        var running = 0;
        for (var i = 0; i < times.Count - 1; i++)
        {
            running += events[times[i]];
            if (times[i + 1] > times[i]) bars.Add(new KitchenBar(times[i], times[i + 1], running));
        }
        return bars;
    }
}
