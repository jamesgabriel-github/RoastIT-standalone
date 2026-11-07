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

    public async Task<IReadOnlyList<BookingRow>> ListRowsByPickupAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Rows(db.Items.AsNoTracking().Where(i => i.Order!.PickupAt >= fromUtc && i.Order.PickupAt < toUtcExclusive)).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<ItemStatus, int>> CountByStatusAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Items.AsNoTracking()
            .Where(i => i.Order!.PickupAt >= fromUtc && i.Order.PickupAt < toUtcExclusive)
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
    }

    public async Task<IReadOnlyList<DateTime>> ListPickupTimesAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Items.AsNoTracking()
            .Where(i => i.Order!.PickupAt >= fromUtc && i.Order.PickupAt < toUtcExclusive)
            .Select(i => i.Order!.PickupAt)
            .ToListAsync(ct);
    }

    public async Task<DashboardSource> GetDashboardSourceAsync(DateTime? fromUtc, DateTime? toUtcExclusive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var orders = db.Orders.AsNoTracking();
        var items = db.Items.AsNoTracking();
        if (fromUtc is { } from && toUtcExclusive is { } to)
        {
            orders = orders.Where(o => o.PickupAt >= from && o.PickupAt < to);
            items = items.Where(i => i.Order!.PickupAt >= from && i.Order.PickupAt < to);
        }

        // Two flat reads joined here: one GROUP BY over items is much cheaper than a correlated subquery per order.
        var perOrder = await items
            .GroupBy(i => i.OrderKey)
            .Select(g => new { OrderKey = g.Key, Active = g.Count(i => i.Status != ItemStatus.Cancelled), FirstId = g.Min(i => i.BookingId) })
            .ToDictionaryAsync(x => x.OrderKey, ct);
        var money = await orders
            .Select(o => new { o.Key, o.Customer, o.PickupAt, o.TotalAmount, o.DiscountAmount, o.AmountPaid })
            .ToListAsync(ct);
        var totals = money
            .Where(o => perOrder.ContainsKey(o.Key))
            .Select(o =>
            {
                var info = perOrder[o.Key];
                return new OrderTotalsRow(o.Key, o.Customer, o.PickupAt, o.TotalAmount, o.DiscountAmount, o.AmountPaid, info.Active == 0, info.FirstId!);
            })
            .ToList();

        // Grouped on the covering index; names come from the (small) products table afterwards.
        var grouped = await items
            .GroupBy(i => new { i.ProductId, i.Status })
            .Select(g => new { g.Key.ProductId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        var names = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var counts = grouped
            .Select(g => new ProductStatusCount(g.ProductId, names.GetValueOrDefault(g.ProductId, ""), g.Status, g.Count))
            .ToList();

        return new DashboardSource(totals, counts);
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
