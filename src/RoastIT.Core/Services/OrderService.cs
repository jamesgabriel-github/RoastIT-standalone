using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Core.Services;

/// <summary>
/// Money and cancellation on an order (plan §7.2, §7.3). Every write starts with <c>EnsureUnlocked</c>. Payments,
/// write-offs and cancellations then need a Bookings PIN confirmation (remembered for 3 minutes), checked here and not
/// only in the UI. Each service reads the order as it is now inside the transaction and never trusts an amount it didn't
/// compute. Change events fire only after the save. A fully cancelled order takes no money actions (Q21).
/// </summary>
public sealed class OrderService(IBookingStore store, IAppLock appLock, IChangeNotifier changes, IClock clock, IBusinessCalendar calendar)
{
    /// <summary>
    /// Sets the flat discount, clamped to [0, total]; 0 removes it. No PIN. Nothing is written when the discount is
    /// unchanged. Returns the discount as saved.
    /// </summary>
    public async Task<long> SetDiscountAsync(Guid orderKey, long amount, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();

        var saved = await store.MutateOrderAsync(orderKey, order =>
        {
            EnsureNotCancelled(order);
            var discount = OrderMath.Clamp(amount, order.TotalAmount);
            if (discount == order.DiscountAmount) return new OrderMutation<(long, bool, DateTime)>((discount, false, order.PickupAt), []);

            var before = order.DiscountAmount;
            order.DiscountAmount = discount;
            var audit = AuditLog.Create(AuditActions.DiscountSet, "order", order.Key.ToString(), clock.UtcNow,
                new { bookingIds = Ids(order), before, after = discount });
            return new OrderMutation<(long, bool, DateTime)>((discount, true, order.PickupAt), [audit]);
        }, ct);

        if (saved.Item2) changes.NotifyOrderChanged(orderKey);
        return saved.Item1;
    }

    /// <summary>
    /// Adds a payment: a <c>payments</c> row and the same amount on <c>amount_paid</c>, in one transaction. The amount
    /// must be above 0. It is additive and may exceed the balance (Q22).
    /// </summary>
    public async Task RecordPaymentAsync(Guid orderKey, long amount, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Bookings);
        if (amount <= 0) throw new ValidationException(BookingMessages.PaymentTooSmall);

        await store.MutateOrderAsync(orderKey, order =>
        {
            EnsureNotCancelled(order);
            var now = clock.UtcNow;
            var paidBefore = order.AmountPaid;
            order.Payments.Add(new Payment { Id = Guid.NewGuid(), OrderKey = order.Key, Amount = amount, RecordedAt = now });
            order.AmountPaid = checked(order.AmountPaid + amount);
            var audit = AuditLog.Create(AuditActions.PaymentRecorded, "order", order.Key.ToString(), now,
                new { bookingIds = Ids(order), amount, paidBefore, paidAfter = order.AmountPaid });
            return new OrderMutation<bool>(true, [audit]);
        }, ct);

        changes.NotifyOrderChanged(orderKey);
    }

    /// <summary>
    /// "Discount remaining balance": reads the order as it is now and sets
    /// <c>discount = min(discount + balance, total)</c>. Offered, and accepted here, only while 0 &lt; balance &lt; 10% of the
    /// gross total. Returns the amount waived.
    /// </summary>
    public async Task<long> WriteOffBalanceAsync(Guid orderKey, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Bookings);

        var waived = await store.MutateOrderAsync(orderKey, order =>
        {
            EnsureNotCancelled(order);
            if (!OrderMath.IsWriteOffBalance(order.TotalAmount, order.DiscountAmount, order.AmountPaid))
                throw new ValidationException(BookingMessages.NothingToWaive);

            var balance = OrderMath.Balance(order);
            var before = order.DiscountAmount;
            order.DiscountAmount = Math.Min(before + balance, order.TotalAmount);
            var audit = AuditLog.Create(AuditActions.BalanceWaived, "order", order.Key.ToString(), clock.UtcNow,
                new { bookingIds = Ids(order), waived = balance, discountBefore = before, discountAfter = order.DiscountAmount });
            return new OrderMutation<long>(balance, [audit]);
        }, ct);

        changes.NotifyOrderChanged(orderKey);
        return waived;
    }

    /// <summary>
    /// Cancels the whole order: every item that isn't already cancelled becomes Cancelled (D8). Refused when any item is
    /// Cooked or Completed (D22), checked again here inside the transaction. <c>total_amount</c> and payments are untouched.
    /// Returns the booking IDs that were cancelled.
    /// </summary>
    public async Task<IReadOnlyList<string>> CancelOrderAsync(Guid orderKey, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Bookings);

        var result = await store.MutateOrderAsync(orderKey, order =>
        {
            var statuses = order.Items.Select(i => i.Status).ToList();
            if (ItemStatusRules.IsFullyCancelled(statuses)) throw new StaleStateException(BookingMessages.OrderCancelled);
            if (!ItemStatusRules.CanCancelOrder(statuses)) throw new ValidationException(BookingMessages.CantCancelToast);

            var cancelled = new List<string>();
            foreach (var item in order.Items.Where(i => i.Status != ItemStatus.Cancelled).OrderBy(i => i.BookingId, StringComparer.Ordinal))
            {
                item.Status = ItemStatus.Cancelled;
                cancelled.Add(item.BookingId);
            }
            var audit = AuditLog.Create(AuditActions.OrderCancelled, "order", order.Key.ToString(), clock.UtcNow,
                new { bookingIds = cancelled, totalAmount = order.TotalAmount, amountPaid = order.AmountPaid });
            return new OrderMutation<(IReadOnlyList<string>, DateTime)>((cancelled, order.PickupAt), [audit]);
        }, ct);

        changes.NotifyOrderChanged(orderKey);
        changes.NotifyBookingsChanged(calendar.DateOf(result.Item2));
        return result.Item1;
    }

    /// <summary>
    /// Moves the pickup of the whole order (D35). Needs a Bookings PIN confirmation. The new time must fall within the
    /// schedulable window (D32), and the order must still have a Pending or Cooking item and none that is Cooked or Completed.
    /// A time in the past is allowed (the form warns). Booking IDs don't change. Nothing is written when the time is unchanged.
    /// Returns true when the pickup was changed.
    /// </summary>
    public async Task<bool> SetPickupAsync(Guid orderKey, DateTime pickupAtUtc, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Bookings);

        if (!ScheduleWindow.Contains(calendar.Today(), calendar.DateOf(pickupAtUtc)))
            throw new ValidationException(BookingMessages.PickupOutsideWindow);

        var result = await store.MutateOrderAsync(orderKey, order =>
        {
            var statuses = order.Items.Select(i => i.Status).ToList();
            if (ItemStatusRules.IsFullyCancelled(statuses)) throw new ValidationException(BookingMessages.OrderCancelled);
            if (!ItemStatusRules.CanChangePickup(statuses)) throw new ValidationException(BookingMessages.CantChangePickup);

            var before = order.PickupAt;
            if (before == pickupAtUtc) return new OrderMutation<(bool, DateTime)>((false, before), []);

            order.PickupAt = pickupAtUtc;
            var audit = AuditLog.Create(AuditActions.PickupChanged, "order", order.Key.ToString(), clock.UtcNow,
                new { bookingIds = Ids(order), before, after = pickupAtUtc });
            return new OrderMutation<(bool, DateTime)>((true, before), [audit]);
        }, ct);

        if (!result.Item1) return false;

        changes.NotifyOrderChanged(orderKey);
        var oldDate = calendar.DateOf(result.Item2);
        var newDate = calendar.DateOf(pickupAtUtc);
        changes.NotifyBookingsChanged(oldDate);
        if (newDate != oldDate) changes.NotifyBookingsChanged(newDate);
        return true;
    }

    /// <summary>
    /// Moves one booking one step back (D39): Cooking → Pending, Cooked → Cooking, Completed → Cooked. Needs a Bookings PIN
    /// confirmation. Refuses with <see cref="StaleStateException"/> when the booking is no longer in
    /// <paramref name="expectedStatus"/>; Pending and Cancelled have no step back. Going back to Pending clears
    /// <c>started_cooking_at</c>; the other steps leave it alone. Cancel and pickup change follow the new status (D22, D35).
    /// Returns the new status.
    /// </summary>
    public async Task<ItemStatus> RevertStatusAsync(string bookingId, ItemStatus expectedStatus, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Bookings);
        var previous = ItemStatusRules.Previous(expectedStatus) ?? throw new ValidationException(BookingMessages.CantRevert);
        var key = await store.FindOrderKeyAsync(bookingId, ct) ?? throw new StaleStateException(BookingMessages.OrderGone);

        var pickup = await store.MutateOrderAsync(key, order =>
        {
            var item = order.Items.SingleOrDefault(i => i.BookingId == bookingId)
                ?? throw new StaleStateException(BookingMessages.OrderGone);
            if (item.Status != expectedStatus)
                throw new StaleStateException(BookingMessages.AlreadyAtStatus(bookingId, ItemStatusRules.Label(item.Status)));

            var clearedStart = previous == ItemStatus.Pending ? item.StartedCookingAt : null;
            item.Status = previous;
            if (previous == ItemStatus.Pending) item.StartedCookingAt = null;
            var audit = AuditLog.Create(AuditActions.StatusReverted, "item", bookingId, clock.UtcNow,
                new { orderKey = order.Key, from = ItemStatusRules.Label(expectedStatus), to = ItemStatusRules.Label(previous), clearedStartedCookingAt = clearedStart });
            return new OrderMutation<DateTime>(order.PickupAt, [audit]);
        }, ct);

        changes.NotifyOrderChanged(key);
        changes.NotifyBookingsChanged(calendar.DateOf(pickup));
        return previous;
    }

    private static void EnsureNotCancelled(Order order)
    {
        if (ItemStatusRules.IsFullyCancelled(order.Items.Select(i => i.Status)))
            throw new ValidationException(BookingMessages.OrderCancelled);
    }

    private static List<string> Ids(Order order) =>
        order.Items.Select(i => i.BookingId).OrderBy(id => id, StringComparer.Ordinal).ToList();
}
