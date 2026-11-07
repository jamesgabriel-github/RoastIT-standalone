using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Rules;

namespace RoastIT.Core.Abstractions;

/// <summary>
/// A new order ready to save: the order (no items yet; it may carry one initial payment), one product id per unit,
/// the business date the booking IDs are numbered for, and the audit rows to write with it.
/// </summary>
public sealed record NewOrderDraft(Order Order, IReadOnlyList<Guid> UnitProductIds, DateOnly BookingDate, IReadOnlyList<AuditLog> Audit);

/// <summary>What a mutation of an order returns: a result for the caller, and audit rows to save with the change.</summary>
public sealed record OrderMutation<T>(T Result, IReadOnlyList<AuditLog> Audit);

/// <summary>
/// Persistence for orders, items and payments (plan §6.2, §7). Rules live in the services; the store only runs
/// transactions. Money and times are as the entities define them.
/// </summary>
public interface IBookingStore
{
    /// <summary>
    /// Every booking with its order and product, flat and in no particular order (order info is not filled in).
    /// </summary>
    Task<IReadOnlyList<BookingRow>> ListRowsAsync(CancellationToken ct = default);

    /// <summary>
    /// Bookings whose order's pickup is in [<paramref name="fromUtc"/>, <paramref name="toUtcExclusive"/>). Every booking of an
    /// order shares its pickup, so each order comes back whole and <c>BookingRows.WithOrderInfo</c> is correct on the result.
    /// </summary>
    Task<IReadOnlyList<BookingRow>> ListRowsByPickupAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default);

    /// <summary>How many bookings with pickup in [from, to) have each status (the list's status chips). Missing statuses are 0.</summary>
    Task<IReadOnlyDictionary<ItemStatus, int>> CountByStatusAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default);

    /// <summary>One pickup instant per booking whose pickup is in [from, to): the date strip and calendar badges.</summary>
    Task<IReadOnlyList<DateTime>> ListPickupTimesAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default);

    /// <summary>
    /// The Dashboard's inputs computed by SQL instead of loading every booking (D21): one money row per order and booking
    /// counts per product and status, for pickups in [from, to), or for everything when both are null. The rules
    /// (what counts as revenue, D23) are applied by <c>Dashboard.Compose</c> in Core.
    /// </summary>
    Task<DashboardSource> GetDashboardSourceAsync(DateTime? fromUtc, DateTime? toUtcExclusive, CancellationToken ct = default);

    /// <summary>All bookings of the order that contains <paramref name="bookingId"/>; empty when there is no such booking.</summary>
    Task<IReadOnlyList<BookingRow>> ListOrderRowsAsync(string bookingId, CancellationToken ct = default);

    Task<Guid?> FindOrderKeyAsync(string bookingId, CancellationToken ct = default);

    /// <summary>
    /// In one transaction: claims a block of booking IDs for <c>BookingDate</c> from <c>daily_counters</c>
    /// (plan §7.1), inserts the order, its items and payment, and the audit rows. Returns the new IDs in unit order.
    /// A rollback leaves a gap in the numbering, never a duplicate.
    /// </summary>
    Task<IReadOnlyList<string>> AddOrderAsync(NewOrderDraft draft, CancellationToken ct = default);

    /// <summary>
    /// In one transaction: loads the order with its items as it is <i>now</i>, runs <paramref name="mutate"/> on it, and
    /// saves what it changed together with its audit rows. New payments are added to <see cref="Order.Payments"/>.
    /// If <paramref name="mutate"/> throws, nothing is saved. Throws <c>StaleStateException</c> when the order is gone.
    /// </summary>
    Task<T> MutateOrderAsync<T>(Guid orderKey, Func<Order, OrderMutation<T>> mutate, CancellationToken ct = default);
}
