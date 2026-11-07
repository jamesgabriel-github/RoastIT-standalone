using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Data.Repositories;

/// <summary>
/// Stores orders, items and payments. One short-lived context per call. Every write runs in one transaction with its
/// audit rows; if anything throws, nothing is saved.
/// </summary>
public sealed class EfBookingStore(IDbContextFactory<RoastItDbContext> dbFactory) : IBookingStore
{
    public async Task<IReadOnlyList<BookingRow>> ListRowsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Rows(db.Items.AsNoTracking()).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BookingRow>> ListOrderRowsAsync(string bookingId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var orderKey = await db.Items.AsNoTracking().Where(i => i.BookingId == bookingId).Select(i => (Guid?)i.OrderKey).SingleOrDefaultAsync(ct);
        if (orderKey is null) return [];
        return await Rows(db.Items.AsNoTracking().Where(i => i.OrderKey == orderKey)).ToListAsync(ct);
    }

    public async Task<Guid?> FindOrderKeyAsync(string bookingId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Items.AsNoTracking().Where(i => i.BookingId == bookingId).Select(i => (Guid?)i.OrderKey).SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<string>> AddOrderAsync(NewOrderDraft draft, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var count = draft.UnitProductIds.Count;
        var date = draft.BookingDate.ToString("yyyy-MM-dd");

        // Claim count numbers at once; RETURNING gives the last one of the block (plan §7.1). The counter row changes in this
        // transaction, so a rollback leaves a gap in the sequence, never a duplicate.
        var lastSeq = (await db.Database.SqlQuery<int>($"""
            INSERT INTO daily_counters (date, last_seq) VALUES ({date}, {count})
            ON CONFLICT(date) DO UPDATE SET last_seq = last_seq + excluded.last_seq
            RETURNING last_seq AS "Value"
            """).ToListAsync(ct)).Single();

        var ids = BookingIdFormat.Block(draft.BookingDate, lastSeq, count);
        var order = draft.Order;
        order.Items = draft.UnitProductIds.Select((productId, i) => new Item
        {
            BookingId = ids[i],
            OrderKey = order.Key,
            ProductId = productId,
            Status = ItemStatus.Pending,
        }).ToList();

        db.Orders.Add(order);
        db.AuditLogs.AddRange(draft.Audit);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ids;
    }

    public async Task<T> MutateOrderAsync<T>(Guid orderKey, Func<Order, OrderMutation<T>> mutate, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var order = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Key == orderKey, ct)
            ?? throw new StaleStateException(BookingMessages.OrderGone);

        var change = mutate(order); // may throw: nothing has been saved yet
        db.AuditLogs.AddRange(change.Audit);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return change.Result;
    }

    /// <summary>Flattens items with their order and product. Order info (position, count, cancelled) is filled in by Core.</summary>
    private static IQueryable<BookingRow> Rows(IQueryable<Item> items) =>
        items.Select(i => new BookingRow(
            i.BookingId,
            i.OrderKey,
            i.Status,
            i.StartedCookingAt,
            i.ProductId,
            i.Product!.Name,
            i.Product.CookingTimeMin,
            i.Order!.Customer,
            i.Order.Phone,
            i.Order.PickupAt,
            i.Order.TotalAmount,
            i.Order.DiscountAmount,
            i.Order.AmountPaid,
            i.Order.Notes));
}
