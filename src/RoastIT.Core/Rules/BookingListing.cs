using RoastIT.Core.Abstractions;
using RoastIT.Core.Enums;

namespace RoastIT.Core.Rules;

public enum BookingSortField { Pickup, Status, BookingId, Customer, Product, CookingTime }

/// <summary>The Booking list's date, search, status and sort state (bookings spec §2, §6).</summary>
public sealed record BookingListOptions(
    DateOnly? Date = null,
    string Search = "",
    ItemStatus? Status = null,
    BookingSortField Field = BookingSortField.Pickup,
    SortDirection Direction = SortDirection.Asc)
{
    public static BookingListOptions Default => new();
}

/// <summary>Counts for the status chips. They are global: they ignore the date and the search (bookings spec §6.1).</summary>
public sealed record BookingStatusCounts(int All, int Pending, int Cooking, int Cooked, int Completed, int Cancelled)
{
    public int For(ItemStatus? status) => status switch
    {
        null => All,
        ItemStatus.Pending => Pending,
        ItemStatus.Cooking => Cooking,
        ItemStatus.Cooked => Cooked,
        ItemStatus.Completed => Completed,
        _ => Cancelled,
    };
}

/// <summary>A run of bookings on one pickup date (grouped view). <see cref="Date"/> is null for the flat list.</summary>
public sealed record BookingGroup(DateOnly? Date, IReadOnlyList<BookingRow> Rows);

public static class BookingListing
{
    public static BookingStatusCounts Counts(IReadOnlyCollection<BookingRow> rows)
    {
        int Of(ItemStatus s) => rows.Count(r => r.Status == s);
        return new BookingStatusCounts(rows.Count, Of(ItemStatus.Pending), Of(ItemStatus.Cooking), Of(ItemStatus.Cooked),
            Of(ItemStatus.Completed), Of(ItemStatus.Cancelled));
    }

    /// <summary>Number of bookings per pickup date, for the date strip badges and the calendar dots.</summary>
    public static Dictionary<DateOnly, int> CountsByDate(IEnumerable<BookingRow> rows, IBusinessCalendar calendar) =>
        rows.GroupBy(r => calendar.DateOf(r.PickupAt)).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Grouped only when no date is selected and the sort is by pickup time (bookings spec §6.3).</summary>
    public static bool IsGrouped(BookingListOptions o) => o.Date is null && o.Field == BookingSortField.Pickup;

    /// <summary>
    /// Filters by date, status and search, then sorts. Ties always fall back to the booking ID (A → Z), so the order never
    /// jumps between refreshes. In the grouped view the day groups follow the direction but bookings inside a day stay
    /// earliest-first.
    /// </summary>
    public static List<BookingGroup> Apply(IEnumerable<BookingRow> rows, BookingListOptions o, IBusinessCalendar calendar)
    {
        var q = o.Search.Trim();
        var filtered = rows.Where(r =>
        {
            if (o.Status is { } s && r.Status != s) return false;
            if (o.Date is { } d && calendar.DateOf(r.PickupAt) != d) return false;
            if (q.Length == 0) return true;
            return r.BookingId.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.ProductName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.CustomerLabel.Contains(q, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        var desc = o.Direction == SortDirection.Desc;

        if (IsGrouped(o))
        {
            var groups = filtered
                .GroupBy(r => calendar.DateOf(r.PickupAt))
                .Select(g => new BookingGroup(g.Key, g.OrderBy(r => r.PickupAt).ThenBy(r => r.BookingId, StringComparer.Ordinal).ToList()));
            return (desc ? groups.OrderByDescending(g => g.Date) : groups.OrderBy(g => g.Date)).ToList();
        }

        var byName = StringComparer.OrdinalIgnoreCase;
        var sorted = o.Field switch
        {
            BookingSortField.Status => Order(filtered, r => (int)r.Status, desc),
            BookingSortField.BookingId => Order(filtered, r => r.BookingId, desc, StringComparer.Ordinal),
            BookingSortField.Customer => Order(filtered, r => r.CustomerLabel, desc, byName),
            BookingSortField.Product => Order(filtered, r => r.ProductName, desc, byName),
            BookingSortField.CookingTime => Order(filtered, r => r.CookingTimeMin, desc),
            _ => Order(filtered, r => r.PickupAt, desc),
        };
        var flat = sorted.ThenBy(r => r.BookingId, StringComparer.Ordinal).ToList();
        return flat.Count == 0 ? [] : [new BookingGroup(null, flat)];
    }

    private static IOrderedEnumerable<BookingRow> Order<TKey>(IEnumerable<BookingRow> source, Func<BookingRow, TKey> key, bool desc, IComparer<TKey>? comparer = null) =>
        desc ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);

    public static int ResultCount(IEnumerable<BookingGroup> groups) => groups.Sum(g => g.Rows.Count);

    /// <summary>The empty-state text (bookings spec §6.5).</summary>
    public static string EmptyMessage(DateOnly? date) =>
        date is { } d ? $"No bookings scheduled for {BookingFormat.ShortDate(d)}." : "No bookings match your filters.";

    public static string FieldLabel(BookingSortField field) => field switch
    {
        BookingSortField.Status => "Status",
        BookingSortField.BookingId => "Booking ID",
        BookingSortField.Customer => "Customer",
        BookingSortField.Product => "Product",
        BookingSortField.CookingTime => "Cooking time",
        _ => "Pickup time",
    };

    public static string DirectionLabel(BookingSortField field, SortDirection direction) => (field, direction) switch
    {
        (BookingSortField.Pickup, SortDirection.Asc) => "Soonest first",
        (BookingSortField.Pickup, _) => "Latest first",
        (BookingSortField.Status, SortDirection.Asc) => "Pending → Completed",
        (BookingSortField.Status, _) => "Completed → Pending",
        (BookingSortField.CookingTime, SortDirection.Asc) => "Shortest first",
        (BookingSortField.CookingTime, _) => "Longest first",
        (_, SortDirection.Asc) => "A → Z",
        _ => "Z → A",
    };
}
