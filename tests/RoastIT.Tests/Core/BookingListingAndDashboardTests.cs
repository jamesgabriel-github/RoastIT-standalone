using RoastIT.Core.Enums;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;

namespace RoastIT.Tests.Core;

/// <summary>Search, filter, sort, grouping, Dashboard and kitchen load over in-memory rows. Today = Sat 2026-11-07, 11:00 Manila.</summary>
public class BookingListingAndDashboardTests
{
    private static readonly BusinessCalendar Cal = new(new FakeClock());
    private static readonly DateTime Now = new FakeClock().UtcNow;
    private static readonly Guid BellyId = Guid.NewGuid();
    private static readonly Guid LiempoId = Guid.NewGuid();
    private static readonly Guid ChickenId = Guid.NewGuid();

    private static DateTime Local(int month, int day, int hour, int minute = 0) =>
        Cal.ToUtc(new DateTime(2026, month, day, hour, minute, 0));

    private static BookingRow Row(
        string id, Guid? order = null, ItemStatus status = ItemStatus.Pending, string product = "Lechon Belly",
        int cooking = 120, string? customer = "Grace", DateTime? pickup = null,
        long total = 220000, long discount = 0, long paid = 0, DateTime? started = null, Guid? productId = null) =>
        new(id, order ?? Guid.NewGuid(), status, started, productId ?? (product == "Liempo" ? LiempoId : product == "Lechon Chicken" ? ChickenId : BellyId),
            product, cooking, customer, null, pickup ?? Local(11, 8, 10), total, discount, paid, null);

    private static List<BookingRow> Enrich(params BookingRow[] rows) => BookingRows.WithOrderInfo(rows);

    private static List<string> Ids(IEnumerable<BookingGroup> groups) => groups.SelectMany(g => g.Rows).Select(r => r.BookingId).ToList();

    // ---- Search and filters -----------------------------------------------------

    [Fact]
    public void Search_finds_booking_ids_products_customers_and_walk_ins_case_insensitively()
    {
        var rows = Enrich(
            Row("261107-001", customer: "Diego Santos"),
            Row("261107-002", customer: null, product: "Liempo"),
            Row("261107-003", customer: "  "));

        Assert.Equal(["261107-002", "261107-003"], Ids(BookingListing.Apply(rows, new BookingListOptions(Search: "WALK-in"), Cal)));
        Assert.Equal(["261107-001"], Ids(BookingListing.Apply(rows, new BookingListOptions(Search: "diego"), Cal)));
        Assert.Equal(["261107-002"], Ids(BookingListing.Apply(rows, new BookingListOptions(Search: "liempo"), Cal)));
        Assert.Equal(["261107-003"], Ids(BookingListing.Apply(rows, new BookingListOptions(Search: " 107-003 "), Cal)));
        Assert.Empty(BookingListing.Apply(rows, new BookingListOptions(Search: "nobody"), Cal));
    }

    [Fact]
    public void Search_status_and_date_combine()
    {
        var rows = Enrich(
            Row("a-1", status: ItemStatus.Pending, pickup: Local(11, 8, 10)),
            Row("a-2", status: ItemStatus.Cooking, pickup: Local(11, 8, 12)),
            Row("a-3", status: ItemStatus.Cooking, pickup: Local(11, 9, 12)));

        var o = new BookingListOptions(Date: new DateOnly(2026, 11, 8), Status: ItemStatus.Cooking);
        Assert.Equal(["a-2"], Ids(BookingListing.Apply(rows, o, Cal)));
        Assert.Equal(["a-3"], Ids(BookingListing.Apply(rows, o with { Date = new DateOnly(2026, 11, 9) }, Cal)));
    }

    [Fact]
    public void A_pickup_just_before_Manila_midnight_belongs_to_that_day_and_not_the_next()
    {
        var late = Row("late", pickup: Cal.ToUtc(new DateTime(2026, 11, 8, 23, 30, 0)));
        var after = Row("after", pickup: Cal.ToUtc(new DateTime(2026, 11, 9, 0, 15, 0)));
        var rows = Enrich(late, after);

        Assert.Equal(["late"], Ids(BookingListing.Apply(rows, new BookingListOptions(Date: new DateOnly(2026, 11, 8)), Cal)));
        Assert.Equal(["after"], Ids(BookingListing.Apply(rows, new BookingListOptions(Date: new DateOnly(2026, 11, 9)), Cal)));
    }

    [Fact]
    public void Status_counts_are_global_and_ignore_date_and_search()
    {
        var rows = Enrich(
            Row("1", status: ItemStatus.Pending), Row("2", status: ItemStatus.Pending),
            Row("3", status: ItemStatus.Cooking), Row("4", status: ItemStatus.Completed),
            Row("5", status: ItemStatus.Cancelled));

        var c = BookingListing.Counts(rows);

        Assert.Equal(new BookingStatusCounts(5, 2, 1, 0, 1, 1), c);
        Assert.Equal(2, c.For(ItemStatus.Pending));
        Assert.Equal(5, c.For(null));
    }

    [Fact]
    public void Date_counts_group_every_booking_of_any_status_by_pickup_date()
    {
        var rows = Enrich(
            Row("1", pickup: Local(11, 8, 9)), Row("2", pickup: Local(11, 8, 20), status: ItemStatus.Cancelled),
            Row("3", pickup: Local(11, 9, 9)));

        var counts = BookingListing.CountsByDate(rows, Cal);

        Assert.Equal(2, counts[new DateOnly(2026, 11, 8)]);
        Assert.Equal(1, counts[new DateOnly(2026, 11, 9)]);
    }

    // ---- Grouping and sorting ---------------------------------------------------

    [Fact]
    public void All_dates_sorted_by_pickup_is_grouped_by_day()
    {
        var rows = Enrich(
            Row("b", pickup: Local(11, 9, 8)), Row("a", pickup: Local(11, 8, 15)), Row("c", pickup: Local(11, 8, 9)));

        var groups = BookingListing.Apply(rows, BookingListOptions.Default, Cal);

        Assert.Equal([new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 9)], groups.Select(g => g.Date));
        Assert.Equal(["c", "a"], groups[0].Rows.Select(r => r.BookingId));
    }

    [Fact]
    public void Latest_first_reverses_the_day_groups_but_keeps_each_day_earliest_first()
    {
        var rows = Enrich(
            Row("b", pickup: Local(11, 9, 8)), Row("a", pickup: Local(11, 8, 15)), Row("c", pickup: Local(11, 8, 9)));

        var groups = BookingListing.Apply(rows, new BookingListOptions(Direction: SortDirection.Desc), Cal);

        Assert.Equal([new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 8)], groups.Select(g => g.Date));
        Assert.Equal(["c", "a"], groups[1].Rows.Select(r => r.BookingId));
    }

    [Fact]
    public void A_selected_date_or_another_sort_field_gives_a_flat_list()
    {
        var rows = Enrich(Row("b", pickup: Local(11, 9, 8)), Row("a", pickup: Local(11, 8, 15)));

        var byDate = BookingListing.Apply(rows, new BookingListOptions(Date: new DateOnly(2026, 11, 8)), Cal);
        var byId = BookingListing.Apply(rows, new BookingListOptions(Field: BookingSortField.BookingId), Cal);

        Assert.False(BookingListing.IsGrouped(new BookingListOptions(Date: new DateOnly(2026, 11, 8))));
        Assert.Null(Assert.Single(byDate).Date);
        Assert.Null(Assert.Single(byId).Date);
        Assert.Equal(["a", "b"], Ids(byId));
    }

    [Fact]
    public void Every_sort_field_and_direction_orders_correctly_with_booking_id_as_the_tiebreak()
    {
        var rows = Enrich(
            Row("3", status: ItemStatus.Cooked, product: "Liempo", cooking: 45, customer: "Zed", pickup: Local(11, 8, 9)),
            Row("1", status: ItemStatus.Pending, product: "Lechon Belly", cooking: 120, customer: null, pickup: Local(11, 8, 11)),
            Row("2", status: ItemStatus.Cooking, product: "Lechon Chicken", cooking: 30, customer: "Anna", pickup: Local(11, 8, 10)),
            Row("4", status: ItemStatus.Pending, product: "Liempo", cooking: 45, customer: "Zed", pickup: Local(11, 8, 12)));

        List<string> Sort(BookingSortField f, SortDirection d) =>
            Ids(BookingListing.Apply(rows, new BookingListOptions(Date: new DateOnly(2026, 11, 8), Field: f, Direction: d), Cal));

        Assert.Equal(["3", "2", "1", "4"], Sort(BookingSortField.Pickup, SortDirection.Asc));
        Assert.Equal(["4", "1", "2", "3"], Sort(BookingSortField.Pickup, SortDirection.Desc));
        Assert.Equal(["1", "4", "2", "3"], Sort(BookingSortField.Status, SortDirection.Asc));
        Assert.Equal(["3", "2", "1", "4"], Sort(BookingSortField.Status, SortDirection.Desc));
        Assert.Equal(["1", "2", "3", "4"], Sort(BookingSortField.BookingId, SortDirection.Asc));
        Assert.Equal(["4", "3", "2", "1"], Sort(BookingSortField.BookingId, SortDirection.Desc));
        Assert.Equal(["2", "1", "3", "4"], Sort(BookingSortField.Customer, SortDirection.Asc)); // Anna, Walk-in, Zed, Zed
        Assert.Equal(["3", "4", "1", "2"], Sort(BookingSortField.Customer, SortDirection.Desc)); // ties stay A → Z by booking ID
        Assert.Equal(["1", "2", "3", "4"], Sort(BookingSortField.Product, SortDirection.Asc));
        Assert.Equal(["3", "4", "2", "1"], Sort(BookingSortField.Product, SortDirection.Desc));
        Assert.Equal(["2", "3", "4", "1"], Sort(BookingSortField.CookingTime, SortDirection.Asc));
        Assert.Equal(["1", "3", "4", "2"], Sort(BookingSortField.CookingTime, SortDirection.Desc));
    }

    [Fact]
    public void Walk_in_customers_sort_under_W()
    {
        var rows = Enrich(Row("1", customer: null), Row("2", customer: "Anna"), Row("3", customer: "Xavier"));
        var o = new BookingListOptions(Date: new DateOnly(2026, 11, 8), Field: BookingSortField.Customer);
        Assert.Equal(["2", "1", "3"], Ids(BookingListing.Apply(rows, o, Cal)));
    }

    [Fact]
    public void Labels_match_the_sort_sheet()
    {
        Assert.Equal("Soonest first", BookingListing.DirectionLabel(BookingSortField.Pickup, SortDirection.Asc));
        Assert.Equal("Latest first", BookingListing.DirectionLabel(BookingSortField.Pickup, SortDirection.Desc));
        Assert.Equal("Pending → Completed", BookingListing.DirectionLabel(BookingSortField.Status, SortDirection.Asc));
        Assert.Equal("Completed → Pending", BookingListing.DirectionLabel(BookingSortField.Status, SortDirection.Desc));
        Assert.Equal("A → Z", BookingListing.DirectionLabel(BookingSortField.BookingId, SortDirection.Asc));
        Assert.Equal("Z → A", BookingListing.DirectionLabel(BookingSortField.Customer, SortDirection.Desc));
        Assert.Equal("Shortest first", BookingListing.DirectionLabel(BookingSortField.CookingTime, SortDirection.Asc));
        Assert.Equal("Longest first", BookingListing.DirectionLabel(BookingSortField.CookingTime, SortDirection.Desc));
        Assert.Equal("Pickup time", BookingListing.FieldLabel(BookingSortField.Pickup));
    }

    [Fact]
    public void Empty_messages_name_the_date_when_one_is_selected()
    {
        Assert.Equal("No bookings scheduled for Oct 10.", BookingListing.EmptyMessage(new DateOnly(2026, 10, 10)));
        Assert.Equal("No bookings match your filters.", BookingListing.EmptyMessage(null));
    }

    [Fact]
    public void Order_tags_come_from_all_bookings_so_filtering_never_renumbers()
    {
        var order = Guid.NewGuid();
        var rows = Enrich(
            Row("261107-001", order, ItemStatus.Cancelled), Row("261107-002", order), Row("261107-003"));

        var visible = Ids(BookingListing.Apply(rows, new BookingListOptions(Status: ItemStatus.Pending), Cal));
        var tags = rows.Where(r => visible.Contains(r.BookingId)).Select(r => r.OrderTag).ToList();

        Assert.Equal(["261107-002", "261107-003"], visible);
        Assert.Equal(["2 of 2", null], tags);
    }

    // ---- Dashboard --------------------------------------------------------------

    [Fact]
    public void Kpis_count_each_order_once_and_use_net_totals()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = Enrich(
            Row("1", a, total: 440000, discount: 40000, paid: 100000), Row("2", a, total: 440000, discount: 40000, paid: 100000),
            Row("3", b, total: 65000, paid: 65000, product: "Liempo"));

        var d = Dashboard.Build(rows, null, Cal, Now);

        Assert.Equal(3, d.TotalBookings);
        Assert.Equal(400000 + 65000, d.Revenue);
        Assert.Equal(165000, d.Collected);
        Assert.Equal(300000, d.BalanceDue);
        Assert.Equal((465000 + 1) / 2, d.AverageOrderValue);
    }

    [Fact]
    public void Fully_cancelled_orders_are_left_out_of_money_but_still_counted_as_bookings()
    {
        var dead = Guid.NewGuid();
        var live = Guid.NewGuid();
        var rows = Enrich(
            Row("1", dead, ItemStatus.Cancelled, total: 450000, paid: 100000),
            Row("2", live, total: 65000, paid: 20000, product: "Liempo"));

        var d = Dashboard.Build(rows, null, Cal, Now);

        Assert.Equal(2, d.TotalBookings);
        Assert.Equal(65000, d.Revenue);
        Assert.Equal(20000, d.Collected);
        Assert.Equal(45000, d.BalanceDue);
        Assert.Equal(65000, d.AverageOrderValue);
    }

    [Fact]
    public void A_partly_cancelled_order_is_not_fully_cancelled_so_its_money_counts()
    {
        var o = Guid.NewGuid();
        var rows = Enrich(Row("1", o, ItemStatus.Cancelled, total: 440000), Row("2", o, ItemStatus.Pending, total: 440000));

        Assert.Equal(440000, Dashboard.Build(rows, null, Cal, Now).Revenue);
    }

    [Fact]
    public void An_empty_scope_gives_zeros()
    {
        var d = Dashboard.Build([], null, Cal, Now);

        Assert.Equal(0, d.TotalBookings);
        Assert.Equal(0, d.Revenue);
        Assert.Equal(0, d.AverageOrderValue);
        Assert.Empty(d.ProductMix);
        Assert.Empty(d.BalanceDueTop);
    }

    [Fact]
    public void The_scope_is_the_pickup_date()
    {
        var rows = Enrich(
            Row("1", pickup: Local(11, 8, 10), total: 100000), Row("2", pickup: Local(11, 9, 10), total: 200000));

        var d = Dashboard.Build(rows, new DateOnly(2026, 11, 9), Cal, Now);

        Assert.Equal(1, d.TotalBookings);
        Assert.Equal(200000, d.Revenue);
    }

    [Fact]
    public void Status_breakdown_shows_only_with_a_date_in_pipeline_order_without_empty_statuses()
    {
        var rows = Enrich(
            Row("1", status: ItemStatus.Cancelled), Row("2", status: ItemStatus.Pending), Row("3", status: ItemStatus.Pending),
            Row("4", status: ItemStatus.Completed));

        Assert.Null(Dashboard.Build(rows, null, Cal, Now).StatusBreakdown);
        var slices = Dashboard.Build(rows, new DateOnly(2026, 11, 8), Cal, Now).StatusBreakdown!;

        Assert.Equal([(ItemStatus.Pending, 2), (ItemStatus.Completed, 1), (ItemStatus.Cancelled, 1)], slices.Select(s => (s.Status, s.Count)));
    }

    [Fact]
    public void Product_mix_counts_non_cancelled_items_most_first_with_name_as_the_tiebreak()
    {
        var rows = Enrich(
            Row("1", product: "Liempo"), Row("2", product: "Lechon Belly"), Row("3", product: "Lechon Belly"),
            Row("4", product: "Lechon Chicken"), Row("5", product: "Lechon Chicken", status: ItemStatus.Cancelled),
            Row("6", product: "Lechon Chicken", status: ItemStatus.Cancelled));

        var mix = Dashboard.Build(rows, null, Cal, Now).ProductMix;

        Assert.Equal([("Lechon Belly", 2), ("Lechon Chicken", 1), ("Liempo", 1)], mix.Select(m => (m.Name, m.Count)));
    }

    [Fact]
    public void Balance_due_lists_the_five_largest_orders_and_counts_the_rest()
    {
        var rows = Enrich(Enumerable.Range(1, 7).Select(i => Row($"{i}", total: i * 10000, customer: $"C{i}")).ToArray());

        var d = Dashboard.Build(rows, null, Cal, Now);

        Assert.Equal(["C7", "C6", "C5", "C4", "C3"], d.BalanceDueTop.Select(r => r.CustomerLabel));
        Assert.Equal(70000, d.BalanceDueTop[0].Balance);
        Assert.Equal(2, d.BalanceDueMore);
    }

    [Fact]
    public void Paid_up_and_fully_cancelled_orders_are_not_in_balance_due()
    {
        var rows = Enrich(
            Row("1", total: 100000, paid: 100000),
            Row("2", total: 100000, discount: 100000),
            Row("3", status: ItemStatus.Cancelled, total: 100000),
            Row("4", total: 100000, paid: 99999, customer: "Owes"));

        var d = Dashboard.Build(rows, null, Cal, Now);

        var only = Assert.Single(d.BalanceDueTop);
        Assert.Equal("Owes", only.CustomerLabel);
        Assert.Equal(1, only.Balance);
        Assert.Equal(0, d.BalanceDueMore);
    }

    [Fact]
    public void Kitchen_load_shows_only_for_a_date_that_is_today_or_later()
    {
        var rows = Enrich(Row("1", pickup: Local(11, 7, 18)), Row("2", pickup: Local(11, 6, 18)));

        Assert.Null(Dashboard.Build(rows, null, Cal, Now).KitchenLoad);
        Assert.Null(Dashboard.Build(rows, new DateOnly(2026, 11, 6), Cal, Now).KitchenLoad);   // yesterday
        Assert.NotNull(Dashboard.Build(rows, new DateOnly(2026, 11, 7), Cal, Now).KitchenLoad); // today
        Assert.NotNull(Dashboard.Build(rows, new DateOnly(2026, 11, 8), Cal, Now).KitchenLoad); // tomorrow, empty list
    }

    // ---- Kitchen load -----------------------------------------------------------

    [Fact]
    public void A_cooking_item_occupies_started_to_started_plus_cooking_time()
    {
        var started = Now.AddMinutes(-30);
        var bars = KitchenLoad.Compute([Row("1", status: ItemStatus.Cooking, cooking: 120, started: started)], Now);

        var bar = Assert.Single(bars);
        Assert.Equal(started, bar.StartUtc);
        Assert.Equal(started.AddMinutes(120), bar.EndUtc);
        Assert.Equal(1, bar.Count);
    }

    [Fact]
    public void An_on_time_pending_item_occupies_pickup_minus_cooking_time_to_pickup()
    {
        var pickup = Local(11, 7, 18);
        var bars = KitchenLoad.Compute([Row("1", pickup: pickup, cooking: 120)], Now);

        var bar = Assert.Single(bars);
        Assert.Equal(pickup.AddMinutes(-120), bar.StartUtc);
        Assert.Equal(pickup, bar.EndUtc);
    }

    [Fact]
    public void A_late_pending_item_runs_from_now_for_its_cooking_time()
    {
        var pickup = Now.AddMinutes(60); // would have had to start 60 minutes ago
        var bars = KitchenLoad.Compute([Row("1", pickup: pickup, cooking: 120)], Now);

        var bar = Assert.Single(bars);
        Assert.Equal(Now, bar.StartUtc);
        Assert.Equal(Now.AddMinutes(120), bar.EndUtc);
    }

    [Fact]
    public void Overlapping_items_stack_and_each_step_is_one_bar()
    {
        var t = Now.AddHours(1);
        var rows = new[]
        {
            Row("1", status: ItemStatus.Cooking, cooking: 120, started: t),                 // t .. t+120
            Row("2", status: ItemStatus.Cooking, cooking: 60, started: t.AddMinutes(30)),   // t+30 .. t+90
        };

        var bars = KitchenLoad.Compute(rows, Now);

        Assert.Equal(
            [(t, t.AddMinutes(30), 1), (t.AddMinutes(30), t.AddMinutes(90), 2), (t.AddMinutes(90), t.AddMinutes(120), 1)],
            bars.Select(b => (b.StartUtc, b.EndUtc, b.Count)));
    }

    [Fact]
    public void A_gap_is_a_zero_height_bar_and_every_distinct_time_splits_a_bar()
    {
        var t = Now.AddHours(1);
        var rows = new[]
        {
            Row("1", status: ItemStatus.Cooking, cooking: 60, started: t),
            Row("2", status: ItemStatus.Cooking, cooking: 60, started: t.AddMinutes(60)),   // starts exactly when 1 ends
            Row("3", status: ItemStatus.Cooking, cooking: 30, started: t.AddMinutes(150)),
        };

        var bars = KitchenLoad.Compute(rows, Now);

        Assert.Equal(
            [(t, t.AddMinutes(60), 1), (t.AddMinutes(60), t.AddMinutes(120), 1), (t.AddMinutes(120), t.AddMinutes(150), 0), (t.AddMinutes(150), t.AddMinutes(180), 1)],
            bars.Select(b => (b.StartUtc, b.EndUtc, b.Count)));
    }

    [Fact]
    public void Cooked_completed_and_cancelled_items_do_not_load_the_kitchen()
    {
        var rows = new[]
        {
            Row("1", status: ItemStatus.Cooked), Row("2", status: ItemStatus.Completed), Row("3", status: ItemStatus.Cancelled),
        };

        Assert.Empty(KitchenLoad.Compute(rows, Now));
    }
}
