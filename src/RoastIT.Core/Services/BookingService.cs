using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Core.Services;

/// <summary>
/// One line of the New booking form: a product and how much of it, in grams (D34). Each line becomes one booking, however
/// heavy it is; the weight is not a count of bookings.
/// </summary>
public sealed record NewBookingLine(Guid ProductId, int WeightGrams);

/// <summary>
/// The New booking form (bookings spec §9). <see cref="PickupAtUtc"/> is a UTC instant (the form converts the business-local
/// picker value with <see cref="IBusinessCalendar.ToUtc"/>). Money is centavos. The form's own total is never sent.
/// </summary>
public sealed record NewBookingInput(
    string? Customer,
    string? Phone,
    DateTime PickupAtUtc,
    IReadOnlyList<NewBookingLine> Lines,
    long Discount = 0,
    long PaidNow = 0);

public sealed record CreatedUnit(string BookingId, string ProductName, int WeightGrams);

/// <summary>What the "Booking created" dialog shows (bookings spec §9.1).</summary>
public sealed record CreatedBooking(Guid OrderKey, string CustomerLabel, DateTime PickupAtUtc, IReadOnlyList<CreatedUnit> Units, long NetTotal);

/// <summary>A booking and the rest of its order, for the detail sheet (bookings spec §8).</summary>
public sealed record BookingDetail(BookingRow Item, IReadOnlyList<BookingRow> OrderItems)
{
    /// <summary>The other bookings in the order, by ID.</summary>
    public IEnumerable<BookingRow> Siblings => OrderItems.Where(r => r.BookingId != Item.BookingId);

    public bool CanCancel => ItemStatusRules.CanCancelOrder(OrderItems.Select(r => r.Status));

    /// <summary>The pickup can still be moved (D35).</summary>
    public bool CanChangePickup => ItemStatusRules.CanChangePickup(OrderItems.Select(r => r.Status));

    /// <summary>Non-cancelled bookings: "Cancel order" when more than one, else "Cancel booking" (plan §7.3).</summary>
    public int ActiveCount => OrderItems.Count(r => r.Status != ItemStatus.Cancelled);

    public string CancelLabel => ActiveCount > 1 ? "Cancel order" : "Cancel booking";

    /// <summary>The viewed booking is Pending or Cooking but the order can't be cancelled (bookings spec §8.5).</summary>
    public bool ShowCantCancelCaption => Item.Status is ItemStatus.Pending or ItemStatus.Cooking && !CanCancel;
}

/// <summary>
/// Creating bookings, moving them through the kitchen, and reading them (plan §7.1–§7.3). Every write starts with
/// <c>EnsureUnlocked</c>. Neither creating nor advancing asks for the PIN. Change events fire only after the save.
/// </summary>
public sealed class BookingService(
    IBookingStore store,
    IProductStore products,
    IAppLock appLock,
    IChangeNotifier changes,
    IClock clock,
    IBusinessCalendar calendar)
{
    /// <summary>Every booking, with its order position, count and cancelled flag filled in. Reading needs no PIN.</summary>
    public async Task<IReadOnlyList<BookingRow>> ListAsync(CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        return BookingRows.WithOrderInfo(await store.ListRowsAsync(ct));
    }

    /// <summary>
    /// The bookings for one pickup date (only that day is read, D21), or for "all dates" when <paramref name="date"/> is
    /// null, which means the schedulable window, today ± 6 months (D32). Order info is complete either way: an order's
    /// bookings share its pickup.
    /// </summary>
    public async Task<IReadOnlyList<BookingRow>> ListAsync(DateOnly? date, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        var (start, end) = date is { } d ? calendar.DayRangeUtc(d) : ScheduleWindow.RangeUtc(calendar);
        return BookingRows.WithOrderInfo(await store.ListRowsByPickupAsync(start, end, ct));
    }

    /// <summary>
    /// The status chip counts: they ignore the selected date and the search (bookings spec §6.1), and cover what "all dates"
    /// covers, today ± 6 months (D32), so "All · N" matches the all-dates list.
    /// </summary>
    public async Task<BookingStatusCounts> StatusCountsAsync(CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        var (from, to) = ScheduleWindow.RangeUtc(calendar);
        var by = await store.CountByStatusAsync(from, to, ct);
        int Of(ItemStatus s) => by.GetValueOrDefault(s);
        return new BookingStatusCounts(by.Values.Sum(), Of(ItemStatus.Pending), Of(ItemStatus.Cooking), Of(ItemStatus.Cooked),
            Of(ItemStatus.Completed), Of(ItemStatus.Cancelled));
    }

    /// <summary>Bookings per pickup date inside the schedulable window (today ± 6 months): the strip badges and calendar dots.</summary>
    public async Task<IReadOnlyDictionary<DateOnly, int>> CountsByDateAsync(CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        var (from, to) = ScheduleWindow.RangeUtc(calendar);
        var times = await store.ListPickupTimesAsync(from, to, ct);
        return times.GroupBy(calendar.DateOf).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>The booking and its order, or null when it no longer exists.</summary>
    public async Task<BookingDetail?> GetDetailAsync(string bookingId, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        var rows = BookingRows.WithOrderInfo(await store.ListOrderRowsAsync(bookingId, ct))
            .OrderBy(r => r.BookingId, StringComparer.Ordinal).ToList();
        var item = rows.FirstOrDefault(r => r.BookingId == bookingId);
        return item is null ? null : new BookingDetail(item, rows);
    }

    /// <summary>
    /// Creates one order with one booking per line (plan §7.2, D34). The total is recomputed from current catalog prices (per
    /// kilo) × each line's weight, inactive
    /// products are refused, the discount is clamped to [0, total] and the paid amount to ≥ 0 (a payment row is written when
    /// it is above 0). IDs are claimed as one block in the same transaction as the insert.
    /// </summary>
    public async Task<CreatedBooking> CreateAsync(NewBookingInput input, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();

        var customer = Clean(input.Customer);
        var phone = Clean(input.Phone);
        if (customer is { Length: > BookingLimits.CustomerMax }) throw new ValidationException(BookingMessages.CustomerTooLong);
        if (phone is { Length: > BookingLimits.PhoneMax }) throw new ValidationException(BookingMessages.PhoneTooLong);

        var lines = input.Lines;
        if (lines.Count == 0) throw new ValidationException(BookingMessages.NoItems);
        if (lines.Count > BookingLimits.MaxUnitsPerOrder) throw new ValidationException(BookingMessages.TooManyUnits);
        if (lines.Any(l => l.WeightGrams is < Weight.MinGrams or > Weight.MaxGrams)) throw new ValidationException(BookingMessages.WeightInvalid);

        var today = calendar.Today();
        var pickupDate = calendar.DateOf(input.PickupAtUtc);
        if (!ScheduleWindow.Contains(today, pickupDate)) throw new ValidationException(BookingMessages.PickupOutsideWindow);

        var catalog = (await products.ListAsync(ct)).ToDictionary(p => p.Id);
        long total = 0;
        var drafts = new List<NewItemDraft>();
        var created = new List<(string Name, int WeightGrams)>();
        foreach (var line in lines)
        {
            if (!catalog.TryGetValue(line.ProductId, out var product)) throw new StaleStateException(BookingMessages.ProductGone);
            if (!product.Active) throw new ValidationException(BookingMessages.NotAvailable(product.Name));
            total = checked(total + OrderMath.LineTotal(product.Price, line.WeightGrams));
            drafts.Add(new NewItemDraft(product.Id, line.WeightGrams));
            created.Add((product.Name, line.WeightGrams));
        }

        var discount = OrderMath.Clamp(input.Discount, total);
        var paid = Math.Max(0, input.PaidNow);
        var now = clock.UtcNow;

        var order = new Order
        {
            Key = Guid.NewGuid(),
            Customer = customer,
            Phone = phone,
            OrderDate = today,
            PickupAt = input.PickupAtUtc,
            TotalAmount = total,
            DiscountAmount = discount,
            AmountPaid = paid,
        };
        var audit = new List<AuditLog>();
        if (discount > 0)
            audit.Add(AuditLog.Create(AuditActions.DiscountSet, "order", order.Key.ToString(), now,
                new { before = 0L, after = discount, atCreation = true }));
        if (paid > 0)
        {
            order.Payments.Add(new Payment { Id = Guid.NewGuid(), OrderKey = order.Key, Amount = paid, RecordedAt = now });
            audit.Add(AuditLog.Create(AuditActions.PaymentRecorded, "order", order.Key.ToString(), now,
                new { amount = paid, paidBefore = 0L, paidAfter = paid, atCreation = true }));
        }

        var ids = await store.AddOrderAsync(new NewOrderDraft(order, drafts, today, audit), ct);

        changes.NotifyBookingsChanged(pickupDate);
        return new CreatedBooking(
            order.Key,
            BookingFormat.CustomerLabel(customer),
            order.PickupAt,
            ids.Select((id, i) => new CreatedUnit(id, created[i].Name, created[i].WeightGrams)).ToList(),
            OrderMath.NetTotal(total, discount));
    }

    /// <summary>
    /// Moves a booking one step forward (plan §7.3). Refuses with <see cref="StaleStateException"/> when the booking is no
    /// longer in <paramref name="expectedStatus"/> (a double tap or a stale screen). Moving to Cooking stamps the start time.
    /// No PIN. Returns the new status.
    /// </summary>
    public async Task<ItemStatus> AdvanceStatusAsync(string bookingId, ItemStatus expectedStatus, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        var next = ItemStatusRules.Next(expectedStatus) ?? throw new ValidationException(BookingMessages.CantAdvance);
        var key = await store.FindOrderKeyAsync(bookingId, ct) ?? throw new StaleStateException(BookingMessages.OrderGone);

        var pickup = await store.MutateOrderAsync(key, order =>
        {
            var item = order.Items.SingleOrDefault(i => i.BookingId == bookingId)
                ?? throw new StaleStateException(BookingMessages.OrderGone);
            if (item.Status != expectedStatus)
                throw new StaleStateException(BookingMessages.AlreadyAtStatus(bookingId, ItemStatusRules.Label(item.Status)));
            item.Status = next;
            if (next == ItemStatus.Cooking) item.StartedCookingAt = clock.UtcNow;
            return new OrderMutation<DateTime>(order.PickupAt, []);
        }, ct);

        changes.NotifyOrderChanged(key);
        changes.NotifyBookingsChanged(calendar.DateOf(pickup));
        return next;
    }

    private static string? Clean(string? text)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
