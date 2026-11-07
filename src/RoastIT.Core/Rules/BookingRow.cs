using RoastIT.Core.Enums;

namespace RoastIT.Core.Rules;

/// <summary>
/// One booking (item) with its order and product, flattened for lists, the detail sheet and the Dashboard.
/// Order-level fields (<see cref="OrderPosition"/>, <see cref="OrderCount"/>, <see cref="OrderCancelled"/>) are filled in by
/// <see cref="BookingRows.WithOrderInfo"/> from all of an order's bookings, never from a filtered list.
/// </summary>
public sealed record BookingRow(
    string BookingId,
    Guid OrderKey,
    ItemStatus Status,
    DateTime? StartedCookingAt,
    Guid ProductId,
    string ProductName,
    int CookingTimeMin,
    string? Customer,
    string? Phone,
    DateTime PickupAt,
    long TotalAmount,
    long DiscountAmount,
    long AmountPaid,
    string? Notes)
{
    /// <summary>1-based rank of this booking among the order's bookings sorted by ID (cancelled included).</summary>
    public int OrderPosition { get; init; } = 1;

    /// <summary>All of the order's bookings, any status.</summary>
    public int OrderCount { get; init; } = 1;

    /// <summary>Every booking of the order is cancelled (D23).</summary>
    public bool OrderCancelled { get; init; }

    public string CustomerLabel => BookingFormat.CustomerLabel(Customer);

    public long NetTotal => OrderMath.NetTotal(TotalAmount, DiscountAmount);

    /// <summary>What the order still owes, ignoring cancellation.</summary>
    public long Balance => OrderMath.Balance(TotalAmount, DiscountAmount, AmountPaid);

    /// <summary>The balance as the Dashboard counts it: ₱0 for a fully cancelled order (D23).</summary>
    public long BalanceDue => OrderCancelled ? 0 : Balance;

    /// <summary>"1 of 2", or null for a single-booking order.</summary>
    public string? OrderTag => OrderCount > 1 ? $"{OrderPosition} of {OrderCount}" : null;
}

public static class BookingRows
{
    /// <summary>Fills in each row's order position, order size and cancelled flag from the full set of rows.</summary>
    public static List<BookingRow> WithOrderInfo(IEnumerable<BookingRow> rows)
    {
        var all = rows.ToList();
        var info = new Dictionary<Guid, (List<string> Ids, bool Cancelled)>();
        foreach (var group in all.GroupBy(r => r.OrderKey))
        {
            var ids = group.Select(r => r.BookingId).OrderBy(id => id, StringComparer.Ordinal).ToList();
            info[group.Key] = (ids, ItemStatusRules.IsFullyCancelled(group.Select(r => r.Status)));
        }
        return all.Select(r =>
        {
            var (ids, cancelled) = info[r.OrderKey];
            return r with { OrderPosition = ids.IndexOf(r.BookingId) + 1, OrderCount = ids.Count, OrderCancelled = cancelled };
        }).ToList();
    }
}
