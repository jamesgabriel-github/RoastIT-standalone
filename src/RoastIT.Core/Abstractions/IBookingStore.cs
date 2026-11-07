using RoastIT.Core.Entities;
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
