using System.Text.Json;
using RoastIT.Core.Enums;
using RoastIT.Core.Rules;

namespace RoastIT.Tests.Core;

/// <summary>
/// D21: the Bookings page reads only its scope and the Dashboard totals come from SQL. Every number must match what the
/// Phase 3 in-memory code (<see cref="Dashboard.Build"/> over every booking) gives for the same data.
/// </summary>
public sealed class ScopedQueriesTests : BookingsTestBase
{
    private async Task SeedAsync()
    {
        var lechon = await AddProductAsync("Lechon Belly", 440000, 240);
        var crispy = await AddProductAsync("Crispy Pata", 95000, 90);
        var liempo = await AddProductAsync("Liempo", 65000, 120);

        // Tomorrow: a two-booking order with a discount and a payment, a single booking, and a fully cancelled order with money on it.
        var big = await CreateFullAsync("Diego Santos", Local(2026, 11, 8, 10), 50000, 300000, (lechon, 1), (crispy, 1));
        await CreateFullAsync(null, Local(2026, 11, 8, 15), 0, 0, (liempo, 2));
        var dead = await CreateFullAsync("Gone", Local(2026, 11, 8, 18), 0, 100000, (lechon, 1));
        await Orders.CancelOrderAsync(dead.OrderKey, await BookingsPinAsync());
        await Bookings.AdvanceStatusAsync(big.Units[0].BookingId, ItemStatus.Pending);

        // Today and next week, so "all dates" spans several days; one order fully paid.
        await CreateFullAsync("Grace Villanueva", Local(2026, 11, 7, 18), 0, 440000, (lechon, 1));
        await CreateFullAsync("Ana Reyes", Local(2026, 11, 14, 12), 0, 0, (crispy, 3));
        // Same balance and pickup as another order: the tie-break must be stable.
        await CreateFullAsync("Ben Cruz", Local(2026, 11, 14, 12), 0, 0, (crispy, 3));
    }

    private static string Json(DashboardData d) => JsonSerializer.Serialize(d);

    /// <summary>An old, unpaid booking from a year ago (pickup moved outside today ± 6 months after it was made).</summary>
    private async Task AddOutsideWindowAsync()
    {
        var old = await AddProductAsync("Old roast", 999900);
        await CreateFullAsync("Last year", Local(2026, 6, 1), 0, 0, (old, 2));
        RoastIT.Tests.Sql.Exec(Fx.DbPath, "UPDATE orders SET pickup_at = '2025-11-01 02:00:00' WHERE customer = 'Last year';");
    }

    /// <summary>Every booking whose pickup date is inside today ± 6 months, from the unscoped read.</summary>
    private async Task<List<BookingRow>> InWindowAsync() =>
        (await Bookings.ListAsync()).Where(r => ScheduleWindow.Contains(Today, Calendar.DateOf(r.PickupAt))).ToList();

    [Theory]
    [InlineData(null)]
    [InlineData("2026-11-07")]
    [InlineData("2026-11-08")]
    [InlineData("2026-11-14")]
    [InlineData("2026-11-20")]
    public async Task The_sql_dashboard_matches_the_in_memory_one(string? day)
    {
        await SeedAsync();
        DateOnly? date = day is null ? null : DateOnly.Parse(day);

        await AddOutsideWindowAsync();

        var fromSql = await DashboardSvc.GetAsync(date);
        var reference = Dashboard.Build(await InWindowAsync(), date, Calendar, Fx.Clock.UtcNow);

        Assert.Equal(Json(reference), Json(fromSql));
    }

    [Fact]
    public async Task A_selected_date_lists_only_that_days_bookings_with_full_order_info()
    {
        await SeedAsync();

        var tomorrow = await Bookings.ListAsync(new DateOnly(2026, 11, 8));
        var all = await Bookings.ListAsync(null);

        Assert.Equal(5, tomorrow.Count);
        Assert.All(tomorrow, r => Assert.Equal(new DateOnly(2026, 11, 8), Calendar.DateOf(r.PickupAt)));
        Assert.Equal(12, all.Count);
        // Order info is the same as when computed from every booking.
        var byId = all.ToDictionary(r => r.BookingId);
        Assert.All(tomorrow, r => Assert.Equal(byId[r.BookingId], r));
    }

    [Fact]
    public async Task A_day_boundary_is_manila_midnight()
    {
        var lechon = await AddProductAsync("Lechon Belly", 440000);
        await CreateFullAsync("Late", Local(2026, 11, 8, 23, 59), 0, 0, (lechon, 1));
        await CreateFullAsync("Early", Local(2026, 11, 9, 0, 0), 0, 0, (lechon, 1));

        Assert.Equal("Late", Assert.Single(await Bookings.ListAsync(new DateOnly(2026, 11, 8))).Customer);
        Assert.Equal("Early", Assert.Single(await Bookings.ListAsync(new DateOnly(2026, 11, 9))).Customer);
    }

    [Fact]
    public async Task All_dates_means_today_plus_or_minus_six_months()
    {
        await SeedAsync();
        await AddOutsideWindowAsync();

        var all = await Bookings.ListAsync(null);
        var dashboard = await DashboardSvc.GetAsync(null);

        Assert.Equal(14, (await Bookings.ListAsync()).Count); // the unscoped read still sees it
        Assert.Equal(12, all.Count);
        Assert.DoesNotContain(all, r => r.Customer == "Last year");
        Assert.Equal(12, dashboard.TotalBookings);
        Assert.DoesNotContain(dashboard.BalanceDueTop, b => b.CustomerLabel == "Last year");
        Assert.DoesNotContain(dashboard.ProductMix, m => m.Name == "Old roast");
        Assert.Equal(12, (await Bookings.StatusCountsAsync()).All);
    }

    [Fact]
    public async Task The_window_edges_are_whole_manila_days()
    {
        var lechon = await AddProductAsync("Lechon Belly", 440000);
        await CreateFullAsync("First day", Local(2026, 5, 7, 0, 0), 0, 0, (lechon, 1));    // today − 6 months, 12:00 AM
        await CreateFullAsync("Last day", Local(2027, 5, 7, 23, 59), 0, 0, (lechon, 1));   // today + 6 months, 11:59 PM
        await CreateFullAsync("Edge out", Local(2026, 5, 7, 0, 0), 0, 0, (lechon, 1));
        RoastIT.Tests.Sql.Exec(Fx.DbPath, "UPDATE orders SET pickup_at = '2026-05-06 15:59:00' WHERE customer = 'Edge out';"); // May 6, 11:59 PM Manila

        var names = (await Bookings.ListAsync(null)).Select(r => r.Customer!).Order().ToArray();

        Assert.Equal(["First day", "Last day"], names);
    }

    [Fact]
    public async Task Status_counts_match_the_all_dates_listing()
    {
        await SeedAsync();
        await AddOutsideWindowAsync();

        var counts = await Bookings.StatusCountsAsync();

        Assert.Equal(BookingListing.Counts((await Bookings.ListAsync(null)).ToList()), counts);
        Assert.Equal(12, counts.All);
        Assert.Equal(1, counts.Cooking);
        Assert.Equal(1, counts.Cancelled);
    }

    [Fact]
    public async Task Date_badges_count_bookings_inside_the_schedule_window_only()
    {
        await SeedAsync();
        var lechon = await AddProductAsync("Far", 1000);
        // Inside the window at creation, then pushed outside it.
        await CreateFullAsync("Far away", Local(2027, 5, 1), 0, 0, (lechon, 1));
        RoastIT.Tests.Sql.Exec(Fx.DbPath, "UPDATE orders SET pickup_at = '2028-01-01 02:00:00' WHERE customer = 'Far away';");

        var counts = await Bookings.CountsByDateAsync();

        Assert.Equal(BookingListing.CountsByDate(await Bookings.ListAsync(), Calendar)
            .Where(kv => ScheduleWindow.Contains(Today, kv.Key)).OrderBy(kv => kv.Key), counts.OrderBy(kv => kv.Key));
        Assert.Equal(5, counts[new DateOnly(2026, 11, 8)]);
        Assert.DoesNotContain(new DateOnly(2028, 1, 1), counts.Keys);
    }

    [Fact]
    public async Task The_new_indexes_exist()
    {
        foreach (var (table, index) in new[]
                 {
                     ("orders", "ix_orders_pickup_at"), ("items", "ix_items_status"), ("products", "ix_products_active"),
                     ("items", "ix_items_order_status_id"), ("items", "ix_items_product_status"), ("payments", "IX_payments_order_key"),
                 })
            Assert.Equal("1", RoastIT.Tests.Sql.Scalar(Fx.DbPath, $"SELECT COUNT(*) FROM pragma_index_list('{table}') WHERE name = '{index}';"));
    }
}
