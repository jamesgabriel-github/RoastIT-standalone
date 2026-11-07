using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;
using RoastIT.Core.Validation;

namespace RoastIT.Tests.Core;

public class BookingServiceTests : BookingsTestBase
{
    // ---- Creating a booking ----------------------------------------------------

    [Fact]
    public async Task Create_makes_one_item_per_line_with_sequential_ids_for_today()
    {
        var belly = await AddProductAsync("Lechon Belly", 220000, 120);
        var chicken = await AddProductAsync("Lechon Chicken", 75000, 30);

        var created = await CreateAsync((belly, 3), (chicken, 1), (belly, 2));

        Assert.Equal(["261107-001", "261107-002", "261107-003"], created.Units.Select(u => u.BookingId));
        Assert.Equal(["Lechon Belly", "Lechon Chicken", "Lechon Belly"], created.Units.Select(u => u.ProductName));
        Assert.Equal([3000, 1000, 2000], created.Units.Select(u => u.WeightGrams));

        var order = await LoadOrderAsync(created.OrderKey);
        Assert.Equal(3, order.Items.Count);
        Assert.All(order.Items, i => Assert.Equal(ItemStatus.Pending, i.Status));
        Assert.All(order.Items, i => Assert.Null(i.StartedCookingAt));
        Assert.Equal(3 * 220000 + 75000 + 2 * 220000, order.TotalAmount);
    }

    [Fact]
    public async Task A_weight_is_one_booking_however_heavy_it_is()
    {
        var belly = await AddProductAsync("Lechon Belly", 220000, 120);

        var created = await CreateAsync((belly, 3.2));

        var unit = Assert.Single(created.Units);
        Assert.Equal("261107-001", unit.BookingId);
        Assert.Equal(3200, unit.WeightGrams);
        var order = await LoadOrderAsync(created.OrderKey);
        Assert.Equal(3200, Assert.Single(order.Items).WeightGrams);
        Assert.Equal(704000, order.TotalAmount); // ₱2,200 a kilo × 3.2 kg = ₱7,040
        Assert.Equal(704000, created.NetTotal);
    }

    [Fact]
    public async Task Line_totals_round_to_the_nearest_centavo()
    {
        var p = await AddProductAsync("Odd price", 33333);

        var created = await CreateAsync((p, 0.01), (p, 2.55));

        // ₱333.33 × 0.01 kg = 333.33 centavos → 333; ₱333.33 × 2.55 kg = 84,999.15 centavos → 84,999.
        Assert.Equal(333 + 84999,(await LoadOrderAsync(created.OrderKey)).TotalAmount);
    }

    [Fact]
    public async Task Weights_outside_0_01_to_500_kg_are_refused_and_nothing_is_saved()
    {
        var p = await AddProductAsync("Liempo", 65000);

        foreach (var kilos in new[] { 0.0, -1, 0.005, 500.01, 3200 })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync((p, 1), (p, kilos)));
            Assert.Equal(BookingMessages.WeightInvalid, ex.Message);
        }
        await CreateAsync((p, 0.01), (p, 500)); // both ends are allowed

        await using var db = Fx.Factory.CreateDbContext();
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(2, await db.Items.CountAsync());
    }

    [Fact]
    public async Task Create_numbers_continue_across_orders_on_the_same_day()
    {
        var p = await AddProductAsync("Liempo", 65000);
        var first = await CreateAsync((p, 2), (p, 1));
        var second = await CreateAsync((p, 1));

        Assert.Equal(["261107-001", "261107-002"], first.Units.Select(u => u.BookingId));
        Assert.Equal(["261107-003"], second.Units.Select(u => u.BookingId));
    }

    [Fact]
    public async Task Booking_ids_roll_over_at_Manila_midnight_not_UTC_midnight()
    {
        var p = await AddProductAsync("Liempo", 65000);

        // 15:59 UTC on Nov 7 = 23:59 in Manila: still Nov 7.
        Fx.Clock.UtcNow = new DateTime(2026, 11, 7, 15, 59, 0, DateTimeKind.Utc);
        var before = await CreateAsync((p, 1));
        // 16:00 UTC = 00:00 Nov 8 in Manila.
        Fx.Clock.UtcNow = new DateTime(2026, 11, 7, 16, 0, 0, DateTimeKind.Utc);
        var after = await CreateAsync((p, 1));

        Assert.Equal("261107-001", before.Units.Single().BookingId);
        Assert.Equal("261108-001", after.Units.Single().BookingId);
        Assert.Equal(new DateOnly(2026, 11, 8), (await LoadOrderAsync(after.OrderKey)).OrderDate);
    }

    [Fact]
    public async Task A_failed_create_leaves_no_duplicate_ids_and_no_half_saved_order()
    {
        var p = await AddProductAsync("Liempo", 65000);
        await CreateAsync((p, 1)); // 261107-001

        // A unit pointing at a product that doesn't exist fails the foreign key at save time, after the IDs were claimed.
        var bad = new Order { Key = Guid.NewGuid(), OrderDate = Today, PickupAt = Tomorrow10, TotalAmount = 100 };
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Store.AddOrderAsync(new NewOrderDraft(bad, [new NewItemDraft(p.Id, 1000), new NewItemDraft(Guid.NewGuid(), 1000)], Today, [])));

        var next = await CreateAsync((p, 1));

        await using var db = Fx.Factory.CreateDbContext();
        Assert.Equal(0, await db.Orders.CountAsync(o => o.Key == bad.Key)); // the bad order was not saved
        var ids = await db.Items.Select(i => i.BookingId).ToListAsync();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal("261107-002", next.Units.Single().BookingId);
    }

    [Fact]
    public async Task Total_is_recomputed_from_current_catalog_prices()
    {
        var belly = await AddProductAsync("Lechon Belly", 220000);
        var created = await CreateAsync((belly, 2));

        // Raise the price afterwards: the stored total must not move.
        await using (var db = Fx.Factory.CreateDbContext())
        {
            var stored = await db.Products.SingleAsync();
            stored.Price = 999999;
            await db.SaveChangesAsync();
        }

        Assert.Equal(440000, (await LoadOrderAsync(created.OrderKey)).TotalAmount);
        var again = await CreateAsync((belly, 1));
        Assert.Equal(999999, (await LoadOrderAsync(again.OrderKey)).TotalAmount);
    }

    [Fact]
    public async Task Inactive_products_are_rejected_and_nothing_is_saved()
    {
        var live = await AddProductAsync("Liempo", 65000);
        var hidden = await AddProductAsync("Old special", 100000, active: false);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync((live, 1), (hidden, 1)));

        Assert.Equal(BookingMessages.NotAvailable("Old special"), ex.Message);
        await using var db = Fx.Factory.CreateDbContext();
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(0, await db.Items.CountAsync());
        Assert.Empty(Events);
    }

    [Fact]
    public async Task Unknown_product_is_a_stale_state_error()
    {
        var ghost = new Product { Id = Guid.NewGuid(), Name = "Ghost", Price = 1 };
        await Assert.ThrowsAsync<StaleStateException>(() => CreateAsync((ghost, 1)));
    }

    [Fact]
    public async Task At_least_one_line_is_required()
    {
        await AddProductAsync("Liempo", 65000);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Bookings.CreateAsync(new NewBookingInput(null, null, Tomorrow10, [], 0, 0)));
        Assert.Equal(BookingMessages.NoItems, ex.Message);
    }

    [Fact]
    public async Task A_line_without_a_weight_is_refused_not_dropped()
    {
        var a = await AddProductAsync("Liempo", 65000);
        var b = await AddProductAsync("Head", 120000);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateAsync((a, 0), (b, 2)));

        Assert.Equal(BookingMessages.WeightInvalid, ex.Message);
        await using var db = Fx.Factory.CreateDbContext();
        Assert.Equal(0, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task An_order_is_capped_at_100_bookings_and_weight_does_not_count_toward_it()
    {
        var p = await AddProductAsync("Liempo", 65000);

        await Assert.ThrowsAsync<ValidationException>(() => CreateAsync(Enumerable.Repeat((p, 1.0), 101).ToArray()));
        var ok = await CreateAsync(Enumerable.Repeat((p, 1.0), 100).ToArray());
        Assert.Equal(100, ok.Units.Count);
        Assert.Equal("261107-100", ok.Units[^1].BookingId);

        var heavy = await CreateAsync((p, 500)); // 500 kg is still one booking
        Assert.Single(heavy.Units);
    }

    [Fact]
    public async Task Discount_is_clamped_to_zero_and_the_total_and_paid_to_at_least_zero()
    {
        var p = await AddProductAsync("Liempo", 65000);

        var tooBig = await CreateFullAsync("A", null, discount: 999999, paid: -500, (p, 1));
        var negative = await CreateFullAsync("B", null, discount: -100, paid: 0, (p, 1));

        var a = await LoadOrderAsync(tooBig.OrderKey);
        Assert.Equal(65000, a.DiscountAmount);
        Assert.Equal(0, a.AmountPaid);
        Assert.Empty(a.Payments);
        Assert.Equal(0, tooBig.NetTotal);
        Assert.Equal(0, (await LoadOrderAsync(negative.OrderKey)).DiscountAmount);
    }

    [Fact]
    public async Task Paid_now_creates_a_payment_row_and_an_audit_entry()
    {
        var p = await AddProductAsync("Liempo", 65000);

        var created = await CreateFullAsync("Anna", null, discount: 5000, paid: 20000, (p, 1));

        var order = await LoadOrderAsync(created.OrderKey);
        Assert.Equal(20000, order.AmountPaid);
        Assert.Equal(20000, Assert.Single(order.Payments).Amount);
        Assert.Single(await AuditAsync(AuditActions.PaymentRecorded));
        Assert.Single(await AuditAsync(AuditActions.DiscountSet));
        Assert.Equal(60000, created.NetTotal);
    }

    [Fact]
    public async Task Customer_and_phone_are_trimmed_and_blank_is_a_walk_in()
    {
        var p = await AddProductAsync("Liempo", 65000);

        var walkIn = await Bookings.CreateAsync(new NewBookingInput("   ", " 0917 000 0000 ", Tomorrow10, [new(p.Id, 1000)]));
        var named = await Bookings.CreateAsync(new NewBookingInput("  Anna Reyes ", null, Tomorrow10, [new(p.Id, 1000)]));

        var a = await LoadOrderAsync(walkIn.OrderKey);
        Assert.Null(a.Customer);
        Assert.Equal("0917 000 0000", a.Phone);
        Assert.Equal("Walk-in customer", walkIn.CustomerLabel);
        Assert.Equal("Anna Reyes", (await LoadOrderAsync(named.OrderKey)).Customer);
    }

    [Fact]
    public async Task Pickup_must_be_within_six_months_of_today_but_may_be_in_the_past()
    {
        var p = await AddProductAsync("Liempo", 65000);

        await Assert.ThrowsAsync<ValidationException>(() => CreateFullAsync("x", Local(2027, 5, 8), 0, 0, (p, 1)));
        await Assert.ThrowsAsync<ValidationException>(() => CreateFullAsync("x", Local(2026, 5, 6), 0, 0, (p, 1)));
        await CreateFullAsync("x", Local(2027, 5, 7, 23, 0), 0, 0, (p, 1)); // last day of the window
        await CreateFullAsync("x", Local(2026, 11, 7, 6, 0), 0, 0, (p, 1)); // earlier today: allowed
    }

    [Fact]
    public async Task Create_is_refused_while_locked_and_writes_nothing()
    {
        var p = await AddProductAsync("Liempo", 65000);
        Lock.Lock();

        await Assert.ThrowsAsync<AccessDeniedException>(() => CreateAsync((p, 1)));

        await using var db = Fx.Factory.CreateDbContext();
        Assert.Equal(0, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Create_announces_the_pickup_date_only_after_the_save()
    {
        var p = await AddProductAsync("Liempo", 65000);

        await Assert.ThrowsAsync<ValidationException>(() => CreateAsync((p, 0)));
        Assert.Empty(Events);

        await CreateAsync((p, 1));
        Assert.Equal(["bookings:2026-11-08"], Events);
    }

    // ---- Status ----------------------------------------------------------------

    [Fact]
    public async Task Status_moves_forward_one_step_at_a_time_and_cooking_stamps_the_start()
    {
        var p = await AddProductAsync("Liempo", 65000);
        var id = (await CreateAsync((p, 1))).Units.Single().BookingId;

        Fx.Clock.UtcNow = new DateTime(2026, 11, 7, 4, 30, 0, DateTimeKind.Utc);
        Assert.Equal(ItemStatus.Cooking, await Bookings.AdvanceStatusAsync(id, ItemStatus.Pending));
        var cooking = (await Bookings.ListAsync()).Single();
        Assert.Equal(ItemStatus.Cooking, cooking.Status);
        Assert.Equal(new DateTime(2026, 11, 7, 4, 30, 0, DateTimeKind.Utc), cooking.StartedCookingAt);

        Fx.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(ItemStatus.Cooked, await Bookings.AdvanceStatusAsync(id, ItemStatus.Cooking));
        Assert.Equal(ItemStatus.Completed, await Bookings.AdvanceStatusAsync(id, ItemStatus.Cooked));

        var done = (await Bookings.ListAsync()).Single();
        Assert.Equal(ItemStatus.Completed, done.Status);
        Assert.Equal(new DateTime(2026, 11, 7, 4, 30, 0, DateTimeKind.Utc), done.StartedCookingAt); // stamped once
    }

    [Fact]
    public async Task Advance_checks_the_expected_status_and_refuses_a_stale_or_double_tap()
    {
        var p = await AddProductAsync("Liempo", 65000);
        var id = (await CreateAsync((p, 1))).Units.Single().BookingId;

        await Bookings.AdvanceStatusAsync(id, ItemStatus.Pending);
        var ex = await Assert.ThrowsAsync<StaleStateException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Pending));

        Assert.Equal("261107-001 is already Cooking.", ex.Message);
        Assert.Equal(ItemStatus.Cooking, (await Bookings.ListAsync()).Single().Status); // not moved twice
    }

    [Fact]
    public async Task Advance_cannot_skip_a_step_or_leave_the_end_of_the_pipeline()
    {
        var p = await AddProductAsync("Liempo", 65000);
        var id = (await CreateAsync((p, 1))).Units.Single().BookingId;

        // "Cooked" was expected but the booking is Pending: stale, not a skip.
        await Assert.ThrowsAsync<StaleStateException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Cooked));
        await Assert.ThrowsAsync<ValidationException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Completed));
        await Assert.ThrowsAsync<ValidationException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Cancelled));
        await Assert.ThrowsAsync<StaleStateException>(() => Bookings.AdvanceStatusAsync("261107-999", ItemStatus.Pending));
        Assert.Equal(ItemStatus.Pending, (await Bookings.ListAsync()).Single().Status);
    }

    [Fact]
    public async Task A_cancelled_booking_cannot_be_advanced()
    {
        var p = await AddProductAsync("Liempo", 65000);
        var id = (await CreateAsync((p, 1))).Units.Single().BookingId;
        await SetStatusAsync(id, ItemStatus.Cancelled);

        await Assert.ThrowsAsync<ValidationException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Cancelled));
        await Assert.ThrowsAsync<StaleStateException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Pending));
    }

    [Fact]
    public async Task Advance_needs_no_PIN_but_is_refused_while_locked_and_events_follow_the_save()
    {
        var p = await AddProductAsync("Liempo", 65000);
        var id = (await CreateAsync((p, 1))).Units.Single().BookingId;
        Events.Clear();

        Lock.Lock();
        await Assert.ThrowsAsync<AccessDeniedException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Pending));
        Assert.Empty(Events);

        await Lock.UnlockAsync(Pin);
        await Bookings.AdvanceStatusAsync(id, ItemStatus.Pending);
        Assert.Equal(["order", "bookings:2026-11-08"], Events);

        await Assert.ThrowsAsync<StaleStateException>(() => Bookings.AdvanceStatusAsync(id, ItemStatus.Pending));
        Assert.Equal(2, Events.Count); // a refused advance announces nothing
    }

    // ---- Reading ---------------------------------------------------------------

    [Fact]
    public async Task List_numbers_each_booking_within_its_order_by_id_including_cancelled_ones()
    {
        var belly = await AddProductAsync("Lechon Belly", 220000);
        var liempo = await AddProductAsync("Liempo", 65000);
        var single = await CreateAsync((liempo, 1));                  // 001
        var multi = await CreateAsync((belly, 2), (belly, 2.5), (liempo, 1)); // 002, 003, 004
        await SetStatusAsync("261107-003", ItemStatus.Cancelled);

        var rows = await Bookings.ListAsync();

        Assert.Null(rows.Single(r => r.BookingId == "261107-001").OrderTag);
        Assert.Equal("1 of 3", rows.Single(r => r.BookingId == "261107-002").OrderTag);
        Assert.Equal("2 of 3", rows.Single(r => r.BookingId == "261107-003").OrderTag);
        Assert.Equal("3 of 3", rows.Single(r => r.BookingId == "261107-004").OrderTag);
        Assert.All(rows.Where(r => r.OrderKey == multi.OrderKey), r => Assert.False(r.OrderCancelled));
        Assert.Equal(single.OrderKey, rows.Single(r => r.OrderCount == 1).OrderKey);
    }

    [Fact]
    public async Task Detail_returns_the_booking_its_siblings_and_cancel_state()
    {
        var belly = await AddProductAsync("Lechon Belly", 220000);
        var liempo = await AddProductAsync("Liempo", 65000);
        await CreateAsync((belly, 1), (liempo, 1));  // 001 belly, 002 liempo

        var detail = await Bookings.GetDetailAsync("261107-002");

        Assert.NotNull(detail);
        Assert.Equal("261107-002", detail.Item.BookingId);
        Assert.Equal(["261107-001"], detail.Siblings.Select(s => s.BookingId));
        Assert.True(detail.CanCancel);
        Assert.Equal("Cancel order", detail.CancelLabel);
        Assert.False(detail.ShowCantCancelCaption);
        Assert.Null(await Bookings.GetDetailAsync("261107-999"));
    }

    [Fact]
    public async Task Detail_shows_the_cant_cancel_caption_when_a_sibling_is_cooked()
    {
        var belly = await AddProductAsync("Lechon Belly", 220000);
        var liempo = await AddProductAsync("Liempo", 65000);
        await CreateAsync((belly, 1), (liempo, 1));
        await SetStatusAsync("261107-001", ItemStatus.Cooked);

        var detail = await Bookings.GetDetailAsync("261107-002");

        Assert.NotNull(detail);
        Assert.False(detail.CanCancel);
        Assert.True(detail.ShowCantCancelCaption);
    }

    [Fact]
    public async Task Detail_says_cancel_booking_when_only_one_active_booking_is_left()
    {
        var liempo = await AddProductAsync("Liempo", 65000);
        await CreateAsync((liempo, 1));
        var detail = await Bookings.GetDetailAsync("261107-001");

        Assert.Equal("Cancel booking", detail!.CancelLabel);
    }

    [Fact]
    public async Task Listing_is_refused_while_locked()
    {
        Lock.Lock();
        await Assert.ThrowsAsync<AccessDeniedException>(() => Bookings.ListAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => Bookings.GetDetailAsync("261107-001"));
    }
}
