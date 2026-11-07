using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Tests.Core;

public class OrderServiceTests : BookingsTestBase
{
    private async Task<(Guid Key, string[] Ids)> NewOrderAsync(long price = 100000, int bookings = 1, long discount = 0, long paid = 0)
    {
        var p = await AddProductAsync("Lechon Belly", price);
        var created = await CreateFullAsync("Diego Santos", null, discount, paid, Enumerable.Repeat((p, 1.0), bookings).ToArray());
        Events.Clear();
        return (created.OrderKey, created.Units.Select(u => u.BookingId).ToArray());
    }

    // ---- Discount ----------------------------------------------------------------

    [Fact]
    public async Task Discount_is_clamped_to_the_total_and_zero_removes_it_with_no_PIN()
    {
        var (key, _) = await NewOrderAsync(price: 100000);

        Assert.Equal(100000, await Orders.SetDiscountAsync(key, 999999));
        Assert.Equal(100000, (await LoadOrderAsync(key)).DiscountAmount);
        Assert.Equal(0, await Orders.SetDiscountAsync(key, -50));
        Assert.Equal(1500, await Orders.SetDiscountAsync(key, 1500));
        Assert.Equal(0, await Orders.SetDiscountAsync(key, 0));
        Assert.Equal(0, (await LoadOrderAsync(key)).DiscountAmount);
        Assert.Equal(4, (await AuditAsync(AuditActions.DiscountSet)).Count);
    }

    [Fact]
    public async Task Setting_the_same_discount_writes_nothing_and_announces_nothing()
    {
        var (key, _) = await NewOrderAsync(discount: 2000);

        await Orders.SetDiscountAsync(key, 2000);
        await Orders.SetDiscountAsync(key, 5000);
        Assert.Equal(["order"], Events);
        Assert.Equal(2, (await AuditAsync(AuditActions.DiscountSet)).Count); // creation + the change to 5000
    }

    [Fact]
    public async Task Discount_never_changes_the_total_and_is_audited_with_before_and_after()
    {
        var (key, _) = await NewOrderAsync(price: 101500);

        await Orders.SetDiscountAsync(key, 1500);

        var order = await LoadOrderAsync(key);
        Assert.Equal(101500, order.TotalAmount);
        Assert.Equal(100000, OrderMath.NetTotal(order));
        var audit = Assert.Single(await AuditAsync(AuditActions.DiscountSet));
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.Equal(0, details.RootElement.GetProperty("before").GetInt64());
        Assert.Equal(1500, details.RootElement.GetProperty("after").GetInt64());
    }

    [Fact]
    public async Task Discount_is_refused_while_locked()
    {
        var (key, _) = await NewOrderAsync();
        Lock.Lock();
        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.SetDiscountAsync(key, 100));
    }

    // ---- Payments ----------------------------------------------------------------

    [Fact]
    public async Task Payment_is_refused_without_a_Bookings_PIN_confirmation_and_writes_nothing()
    {
        var (key, _) = await NewOrderAsync();

        await Assert.ThrowsAsync<PinRequiredException>(() => Orders.RecordPaymentAsync(key, 50000, null));

        var order = await LoadOrderAsync(key);
        Assert.Equal(0, order.AmountPaid);
        Assert.Empty(order.Payments);
        Assert.Empty(await AuditAsync(AuditActions.PaymentRecorded));
        Assert.Empty(Events);
    }

    [Fact]
    public async Task Payments_are_additive_write_a_row_each_and_are_audited()
    {
        var (key, ids) = await NewOrderAsync(price: 200000);
        var pin = await BookingsPinAsync();

        await Orders.RecordPaymentAsync(key, 50000, pin);
        await Orders.RecordPaymentAsync(key, 70000, null); // inside the 3-minute window

        var order = await LoadOrderAsync(key);
        Assert.Equal(120000, order.AmountPaid);
        Assert.Equal([50000L, 70000L], order.Payments.Select(p => p.Amount).OrderBy(a => a));
        Assert.Equal(80000, OrderMath.Balance(order));

        var audits = await AuditAsync(AuditActions.PaymentRecorded);
        Assert.Equal(2, audits.Count);
        using var details = JsonDocument.Parse(audits.Single(a => a.Details!.Contains("\"amount\":70000")).Details!);
        Assert.Equal(50000, details.RootElement.GetProperty("paidBefore").GetInt64());
        Assert.Equal(120000, details.RootElement.GetProperty("paidAfter").GetInt64());
        Assert.Equal(ids[0], details.RootElement.GetProperty("bookingIds")[0].GetString());
        Assert.Equal(["order", "order"], Events);
    }

    [Fact]
    public async Task A_second_payment_asks_for_the_PIN_again_after_three_minutes_or_a_lock()
    {
        var (key, _) = await NewOrderAsync(price: 200000);
        await Orders.RecordPaymentAsync(key, 10000, await BookingsPinAsync());

        Fx.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(59));
        await Orders.RecordPaymentAsync(key, 10000, null); // still inside the window

        Fx.Clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<PinRequiredException>(() => Orders.RecordPaymentAsync(key, 10000, null));

        await Orders.RecordPaymentAsync(key, 10000, await BookingsPinAsync());
        Lock.Lock();
        await Lock.UnlockAsync(Pin);
        await Assert.ThrowsAsync<PinRequiredException>(() => Orders.RecordPaymentAsync(key, 10000, null));
        Assert.Equal(30000, (await LoadOrderAsync(key)).AmountPaid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Payment_must_be_above_zero(long amount)
    {
        var (key, _) = await NewOrderAsync();
        var pin = await BookingsPinAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Orders.RecordPaymentAsync(key, amount, pin));

        Assert.Equal(BookingMessages.PaymentTooSmall, ex.Message);
        Assert.Empty((await LoadOrderAsync(key)).Payments);
    }

    [Fact]
    public async Task A_payment_may_exceed_the_balance_and_the_balance_stops_at_zero()
    {
        var (key, _) = await NewOrderAsync(price: 100000);

        await Orders.RecordPaymentAsync(key, 150000, await BookingsPinAsync());

        var order = await LoadOrderAsync(key);
        Assert.Equal(150000, order.AmountPaid);
        Assert.Equal(0, OrderMath.Balance(order));
    }

    [Fact]
    public async Task Payment_is_refused_while_locked_and_for_a_missing_order()
    {
        var (key, _) = await NewOrderAsync();
        var pin = await BookingsPinAsync();

        await Assert.ThrowsAsync<StaleStateException>(() => Orders.RecordPaymentAsync(Guid.NewGuid(), 100, pin));
        Lock.Lock();
        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.RecordPaymentAsync(key, 100, pin));
    }

    // ---- Write-off ---------------------------------------------------------------

    [Fact]
    public async Task Write_off_is_refused_without_a_PIN_confirmation()
    {
        var (key, _) = await NewOrderAsync(price: 200000, paid: 190000);
        await Assert.ThrowsAsync<PinRequiredException>(() => Orders.WriteOffBalanceAsync(key, null));
        Assert.Equal(0, (await LoadOrderAsync(key)).DiscountAmount);
    }

    [Fact]
    public async Task Write_off_zeroes_the_balance_using_the_current_values()
    {
        var (key, _) = await NewOrderAsync(price: 200000, paid: 190000); // balance 10,000 = 5%
        var pin = await BookingsPinAsync();

        // The balance changes after the sheet was drawn: another payment came in.
        await Orders.RecordPaymentAsync(key, 4000, pin);
        var waived = await Orders.WriteOffBalanceAsync(key, null);

        Assert.Equal(6000, waived); // the balance as it is now, not as it was when drawn
        var order = await LoadOrderAsync(key);
        Assert.Equal(6000, order.DiscountAmount);
        Assert.Equal(0, OrderMath.Balance(order));
        Assert.Equal(200000, order.TotalAmount);
        Assert.Single(await AuditAsync(AuditActions.BalanceWaived));
    }

    [Fact]
    public async Task Write_off_adds_to_an_existing_discount()
    {
        var (key, _) = await NewOrderAsync(price: 200000, discount: 10000, paid: 185000); // net 190,000, balance 5,000

        await Orders.WriteOffBalanceAsync(key, await BookingsPinAsync());

        var order = await LoadOrderAsync(key);
        Assert.Equal(15000, order.DiscountAmount);
        Assert.Equal(0, OrderMath.Balance(order));
    }

    [Theory]
    [InlineData(200000, 0)]        // nothing paid: balance is 100%
    [InlineData(200000, 180000)]   // balance exactly 10% of the gross total is not "small"
    [InlineData(200000, 200000)]   // no balance
    public async Task Write_off_is_refused_unless_the_balance_is_under_ten_percent(long price, long paid)
    {
        var (key, _) = await NewOrderAsync(price: price, paid: paid);

        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
            await Orders.WriteOffBalanceAsync(key, await BookingsPinAsync()));

        Assert.Equal(BookingMessages.NothingToWaive, ex.Message);
        Assert.Equal(0, (await LoadOrderAsync(key)).DiscountAmount);
    }

    [Fact]
    public async Task Write_off_is_allowed_just_under_ten_percent()
    {
        var (key, _) = await NewOrderAsync(price: 200000, paid: 180001); // balance 19,999 < 20,000
        await Orders.WriteOffBalanceAsync(key, await BookingsPinAsync());
        Assert.Equal(0, OrderMath.Balance(await LoadOrderAsync(key)));
    }

    // ---- Cancel ------------------------------------------------------------------

    [Fact]
    public async Task Cancel_is_refused_without_a_PIN_confirmation_and_changes_nothing()
    {
        var (key, _) = await NewOrderAsync(bookings: 2);

        await Assert.ThrowsAsync<PinRequiredException>(() => Orders.CancelOrderAsync(key, null));

        Assert.All((await LoadOrderAsync(key)).Items, i => Assert.Equal(ItemStatus.Pending, i.Status));
        Assert.Empty(Events);
    }

    [Fact]
    public async Task Cancel_cancels_every_item_of_the_order_and_keeps_money_and_payments()
    {
        var (key, ids) = await NewOrderAsync(price: 220000, bookings: 2, paid: 200000);
        await SetStatusAsync(ids[1], ItemStatus.Cooking);

        var cancelled = await Orders.CancelOrderAsync(key, await BookingsPinAsync());

        Assert.Equal(ids, cancelled);
        var order = await LoadOrderAsync(key);
        Assert.All(order.Items, i => Assert.Equal(ItemStatus.Cancelled, i.Status));
        Assert.Equal(440000, order.TotalAmount);
        Assert.Equal(200000, order.AmountPaid);
        Assert.Single(order.Payments);

        var audit = Assert.Single(await AuditAsync(AuditActions.OrderCancelled));
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.Equal(ids, details.RootElement.GetProperty("bookingIds").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["order", "bookings:2026-11-08"], Events);
    }

    [Theory]
    [InlineData(ItemStatus.Cooked)]
    [InlineData(ItemStatus.Completed)]
    public async Task Cancel_is_refused_once_any_item_is_cooked_or_completed_and_nothing_changes(ItemStatus blocking)
    {
        var (key, ids) = await NewOrderAsync(bookings: 2);
        await SetStatusAsync(ids[0], blocking);
        var pin = await BookingsPinAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Orders.CancelOrderAsync(key, pin));

        Assert.Equal(BookingMessages.CantCancelToast, ex.Message);
        var items = (await LoadOrderAsync(key)).Items.OrderBy(i => i.BookingId).ToList();
        Assert.Equal([blocking, ItemStatus.Pending], items.Select(i => i.Status));
        Assert.Empty(await AuditAsync(AuditActions.OrderCancelled));
        Assert.Empty(Events);
    }

    [Fact]
    public async Task Cancel_rechecks_at_save_time_when_the_order_moved_on_after_the_PIN()
    {
        var (key, ids) = await NewOrderAsync(bookings: 2);
        var pin = await BookingsPinAsync();   // the sheet was open and the PIN entered...
        await Bookings.AdvanceStatusAsync(ids[0], ItemStatus.Pending);
        await Bookings.AdvanceStatusAsync(ids[0], ItemStatus.Cooking);   // ...meanwhile one item got cooked

        await Assert.ThrowsAsync<ValidationException>(() => Orders.CancelOrderAsync(key, pin));
        Assert.DoesNotContain((await LoadOrderAsync(key)).Items, i => i.Status == ItemStatus.Cancelled);
    }

    [Fact]
    public async Task Cancelling_an_already_cancelled_order_is_a_stale_state_error()
    {
        var (key, _) = await NewOrderAsync();
        var pin = await BookingsPinAsync();
        await Orders.CancelOrderAsync(key, pin);

        await Assert.ThrowsAsync<StaleStateException>(() => Orders.CancelOrderAsync(key, null));
    }

    [Fact]
    public async Task Already_cancelled_items_stay_as_they_are_when_the_rest_is_cancelled()
    {
        var (key, ids) = await NewOrderAsync(bookings: 3);
        await SetStatusAsync(ids[1], ItemStatus.Cancelled);

        var cancelled = await Orders.CancelOrderAsync(key, await BookingsPinAsync());

        Assert.Equal([ids[0], ids[2]], cancelled);
        Assert.All((await LoadOrderAsync(key)).Items, i => Assert.Equal(ItemStatus.Cancelled, i.Status));
    }

    [Fact]
    public async Task Cancel_is_refused_while_locked()
    {
        var (key, _) = await NewOrderAsync();
        var pin = await BookingsPinAsync();
        Lock.Lock();
        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.CancelOrderAsync(key, pin));
    }

    [Fact]
    public async Task A_cancelled_order_takes_no_discount_payment_or_write_off()
    {
        var (key, _) = await NewOrderAsync(price: 200000, paid: 190000);
        var pin = await BookingsPinAsync();
        await Orders.CancelOrderAsync(key, pin);

        await Assert.ThrowsAsync<ValidationException>(() => Orders.SetDiscountAsync(key, 100));
        await Assert.ThrowsAsync<ValidationException>(() => Orders.RecordPaymentAsync(key, 100, null));
        await Assert.ThrowsAsync<ValidationException>(() => Orders.WriteOffBalanceAsync(key, null));

        var order = await LoadOrderAsync(key);
        Assert.Equal(0, order.DiscountAmount);
        Assert.Equal(190000, order.AmountPaid);
    }

    [Fact]
    public async Task A_fully_cancelled_order_has_no_balance_due_in_the_list_rows()
    {
        var (key, _) = await NewOrderAsync(price: 200000, paid: 50000);
        var before = (await Bookings.ListAsync()).Single();
        Assert.Equal(150000, before.BalanceDue);

        await Orders.CancelOrderAsync(key, await BookingsPinAsync());

        var after = (await Bookings.ListAsync()).Single();
        Assert.True(after.OrderCancelled);
        Assert.Equal(0, after.BalanceDue);
        Assert.Equal(150000, after.Balance); // the raw figure is still there for history
    }
}

