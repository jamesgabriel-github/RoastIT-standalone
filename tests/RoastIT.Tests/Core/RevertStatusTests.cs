using System.Text.Json;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Tests.Core;

/// <summary>Reverting a booking one status step (D39).</summary>
public class RevertStatusTests : BookingsTestBase
{
    private async Task<(Guid Key, string[] Ids)> NewOrderAsync(int bookings = 1)
    {
        var p = await AddProductAsync("Lechon Chicken", 100000);
        var created = await CreateAsync(Enumerable.Repeat((p, 1.0), bookings).ToArray());
        Events.Clear();
        return (created.OrderKey, created.Units.Select(u => u.BookingId).ToArray());
    }

    private async Task AdvanceToAsync(string id, ItemStatus target)
    {
        var current = ItemStatus.Pending;
        while (current != target)
        {
            current = await Bookings.AdvanceStatusAsync(id, current);
            Fx.Clock.Advance(TimeSpan.FromMinutes(5));
        }
    }

    private async Task<Item> LoadItemAsync(Guid key, string id) => (await LoadOrderAsync(key)).Items.Single(i => i.BookingId == id);

    [Theory]
    [InlineData(ItemStatus.Cooking, ItemStatus.Pending)]
    [InlineData(ItemStatus.Cooked, ItemStatus.Cooking)]
    [InlineData(ItemStatus.Completed, ItemStatus.Cooked)]
    public void Previous_is_one_step_back_from_any_status_but_pending_and_cancelled(ItemStatus from, ItemStatus to)
    {
        Assert.Equal(to, ItemStatusRules.Previous(from));
        Assert.Equal(from, ItemStatusRules.Next(to)); // the exact inverse of Next
    }

    [Theory]
    [InlineData(ItemStatus.Pending)]
    [InlineData(ItemStatus.Cancelled)]
    public void Pending_and_cancelled_have_no_step_back(ItemStatus status) => Assert.Null(ItemStatusRules.Previous(status));

    [Fact]
    public async Task Revert_is_refused_without_a_PIN_confirmation_and_changes_nothing()
    {
        var (key, ids) = await NewOrderAsync();
        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        Events.Clear();

        await Assert.ThrowsAsync<PinRequiredException>(() => Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, null));

        var item = await LoadItemAsync(key, ids[0]);
        Assert.Equal(ItemStatus.Cooking, item.Status);
        Assert.NotNull(item.StartedCookingAt);
        Assert.Empty(await AuditAsync(AuditActions.StatusReverted));
        Assert.Empty(Events);
    }

    [Fact]
    public async Task Cooking_back_to_Pending_clears_the_cooking_start_audits_it_and_announces_the_change()
    {
        var (key, ids) = await NewOrderAsync();
        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        var started = (await LoadItemAsync(key, ids[0])).StartedCookingAt;
        Assert.NotNull(started);
        Events.Clear();

        var status = await Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, await BookingsPinAsync());

        Assert.Equal(ItemStatus.Pending, status);
        var item = await LoadItemAsync(key, ids[0]);
        Assert.Equal(ItemStatus.Pending, item.Status);
        Assert.Null(item.StartedCookingAt);

        var audit = Assert.Single(await AuditAsync(AuditActions.StatusReverted));
        Assert.Equal(ids[0], audit.TargetId);
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.Equal("Cooking", details.RootElement.GetProperty("from").GetString());
        Assert.Equal("Pending", details.RootElement.GetProperty("to").GetString());
        Assert.Equal(started, details.RootElement.GetProperty("clearedStartedCookingAt").GetDateTime());
        Assert.Equal(["order", "bookings:2026-11-08"], Events);
    }

    [Fact]
    public async Task A_reverted_booking_can_be_started_again_and_gets_a_fresh_start_time()
    {
        var (key, ids) = await NewOrderAsync();
        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        var first = (await LoadItemAsync(key, ids[0])).StartedCookingAt;
        await Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, await BookingsPinAsync());
        Fx.Clock.Advance(TimeSpan.FromMinutes(20));

        await Bookings.AdvanceStatusAsync(ids[0], ItemStatus.Pending);

        var second = (await LoadItemAsync(key, ids[0])).StartedCookingAt;
        Assert.NotNull(second);
        Assert.True(second > first);
    }

    [Theory]
    [InlineData(ItemStatus.Cooked, ItemStatus.Cooking)]
    [InlineData(ItemStatus.Completed, ItemStatus.Cooked)]
    public async Task Later_steps_go_one_back_and_keep_the_cooking_start(ItemStatus from, ItemStatus to)
    {
        var (key, ids) = await NewOrderAsync();
        await AdvanceToAsync(ids[0], from);
        var started = (await LoadItemAsync(key, ids[0])).StartedCookingAt;

        var status = await Orders.RevertStatusAsync(ids[0], from, await BookingsPinAsync());

        Assert.Equal(to, status);
        var item = await LoadItemAsync(key, ids[0]);
        Assert.Equal(to, item.Status);
        Assert.Equal(started, item.StartedCookingAt);
        Assert.Single(await AuditAsync(AuditActions.StatusReverted));
    }

    [Theory]
    [InlineData(ItemStatus.Pending)]
    [InlineData(ItemStatus.Cancelled)]
    public async Task Pending_and_cancelled_bookings_cannot_be_reverted(ItemStatus status)
    {
        var (key, ids) = await NewOrderAsync();
        await SetStatusAsync(ids[0], status);
        var pin = await BookingsPinAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Orders.RevertStatusAsync(ids[0], status, pin));

        Assert.Equal(BookingMessages.CantRevert, ex.Message);
        Assert.Equal(status, (await LoadItemAsync(key, ids[0])).Status);
        Assert.Empty(await AuditAsync(AuditActions.StatusReverted));
        Assert.Empty(Events);
    }

    [Fact]
    public async Task A_stale_screen_reverts_nothing()
    {
        var (key, ids) = await NewOrderAsync();
        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        var pin = await BookingsPinAsync();   // the sheet showed Cooking and the PIN was entered...
        await Bookings.AdvanceStatusAsync(ids[0], ItemStatus.Cooking);   // ...meanwhile it was marked Cooked
        Events.Clear();

        await Assert.ThrowsAsync<StaleStateException>(() => Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, pin));

        Assert.Equal(ItemStatus.Cooked, (await LoadItemAsync(key, ids[0])).Status);
        Assert.Empty(await AuditAsync(AuditActions.StatusReverted));
        Assert.Empty(Events);
    }

    [Fact]
    public async Task An_unknown_booking_is_a_stale_state_error()
    {
        var pin = await BookingsPinAsync();
        await Assert.ThrowsAsync<StaleStateException>(() => Orders.RevertStatusAsync("261108-999", ItemStatus.Cooking, pin));
    }

    [Fact]
    public async Task Revert_is_refused_while_locked()
    {
        var (key, ids) = await NewOrderAsync();
        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        var pin = await BookingsPinAsync();
        Lock.Lock();

        await Assert.ThrowsAsync<AccessDeniedException>(() => Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, pin));
    }

    [Fact]
    public async Task The_PIN_is_remembered_for_the_Bookings_window_and_expires_with_it()
    {
        var (_, ids) = await NewOrderAsync(bookings: 2);
        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        await AdvanceToAsync(ids[1], ItemStatus.Cooking);

        await Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, await BookingsPinAsync());
        await Orders.RevertStatusAsync(ids[1], ItemStatus.Cooking, null); // inside the 3 minutes: no new PIN

        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        Fx.Clock.Advance(TimeSpan.FromMinutes(4));
        await Assert.ThrowsAsync<PinRequiredException>(() => Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, null));
    }

    [Fact]
    public async Task Reverting_a_cooked_booking_makes_the_order_cancellable_again()
    {
        var (key, ids) = await NewOrderAsync(bookings: 2);
        await AdvanceToAsync(ids[0], ItemStatus.Cooked);
        var pin = await BookingsPinAsync();
        await Assert.ThrowsAsync<ValidationException>(() => Orders.CancelOrderAsync(key, pin));
        await Assert.ThrowsAsync<ValidationException>(() => Orders.SetPickupAsync(key, Local(2026, 11, 9), pin));

        await Orders.RevertStatusAsync(ids[0], ItemStatus.Cooked, pin);

        Assert.True(ItemStatusRules.CanCancelOrder((await LoadOrderAsync(key)).Items.Select(i => i.Status)));
        Assert.True(await Orders.SetPickupAsync(key, Local(2026, 11, 9), pin));
        Assert.Equal(ids, await Orders.CancelOrderAsync(key, pin));
    }

    [Fact]
    public async Task Reverting_does_not_touch_money_or_the_other_bookings()
    {
        var (key, ids) = await NewOrderAsync(bookings: 2);
        await AdvanceToAsync(ids[0], ItemStatus.Cooking);
        await AdvanceToAsync(ids[1], ItemStatus.Cooking);
        await Orders.RecordPaymentAsync(key, 50000, await BookingsPinAsync());
        var before = await LoadOrderAsync(key);

        await Orders.RevertStatusAsync(ids[0], ItemStatus.Cooking, null);

        var after = await LoadOrderAsync(key);
        Assert.Equal(before.TotalAmount, after.TotalAmount);
        Assert.Equal(before.AmountPaid, after.AmountPaid);
        Assert.Equal(before.PickupAt, after.PickupAt);
        Assert.Single(after.Payments);
        Assert.Equal(ItemStatus.Cooking, after.Items.Single(i => i.BookingId == ids[1]).Status);
    }

    [Fact]
    public void The_PIN_prompt_names_the_booking_the_steps_and_the_cleared_start_time()
    {
        var start = new DateTime(2026, 10, 8, 17, 30, 0);

        Assert.Equal(
            "Move 261008-001 (Lechon Chicken) back from Cooking to Pending. Its cooking start time (Oct 8 · 5:30 PM) will be cleared.",
            BookingPrompts.RevertStatus("261008-001", "Lechon Chicken", ItemStatus.Cooking, ItemStatus.Pending, start));
        Assert.Equal(
            "Move 261008-001 (Lechon Chicken) back from Cooked to Cooking.",
            BookingPrompts.RevertStatus("261008-001", "Lechon Chicken", ItemStatus.Cooked, ItemStatus.Cooking, start));
        Assert.Equal("261008-001 reverted to Pending", BookingPrompts.Reverted("261008-001", ItemStatus.Pending));
    }
}
