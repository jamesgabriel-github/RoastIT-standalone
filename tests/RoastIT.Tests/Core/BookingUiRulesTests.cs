using RoastIT.Core.Enums;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;

namespace RoastIT.Tests.Core;

public class BookingColumnsTests
{
    [Fact]
    public void Nothing_saved_or_broken_json_means_every_column_is_on()
    {
        Assert.Equal(BookingColumns.All, BookingColumns.Parse(null));
        Assert.Equal(BookingColumns.All, BookingColumns.Parse(""));
        Assert.Equal(BookingColumns.All, BookingColumns.Parse("{not json"));
        Assert.Equal(BookingColumns.All, BookingColumns.Parse("null"));
    }

    [Fact]
    public void Round_trips_through_json_with_the_preference_keys()
    {
        var columns = new BookingColumns(Customer: false, Product: true, CookingTime: false, Pickup: true, Status: false, Total: true, Paid: false);

        var json = columns.ToJson();

        Assert.Contains("\"cookingTime\":false", json);
        Assert.Contains("\"total\":true", json);
        Assert.Equal(columns, BookingColumns.Parse(json));
    }

    [Fact]
    public void A_missing_key_stays_on()
    {
        var columns = BookingColumns.Parse("{\"customer\":false}");
        Assert.False(columns.Customer);
        Assert.True(columns.Paid);
    }

    [Fact]
    public void The_product_and_money_lines_hide_only_when_both_of_their_columns_are_off()
    {
        Assert.True(new BookingColumns(Product: false, CookingTime: true).ShowsProductLine);
        Assert.False(new BookingColumns(Product: false, CookingTime: false).ShowsProductLine);
        Assert.True(new BookingColumns(Total: false, Paid: true).ShowsMoneyLine);
        Assert.False(new BookingColumns(Total: false, Paid: false).ShowsMoneyLine);
    }
}

public class BookingPromptsTests
{
    [Fact]
    public void Payment_and_waive_summaries_name_the_customer_and_booking()
    {
        Assert.Equal("Record a ₱500 payment from Diego Santos (261008-002).", BookingPrompts.RecordPayment(50000, "Diego Santos", "261008-002"));
        Assert.Equal("Waive the remaining ₱150 for Walk-in customer (261008-003).", BookingPrompts.Waive(15000, "Walk-in customer", "261008-003"));
    }

    [Fact]
    public void Cancel_summary_for_one_booking_and_for_several()
    {
        Assert.Equal("Cancel booking 261009-003 for Mark Villanueva.", BookingPrompts.Cancel(["261009-003"], "Mark Villanueva", 0));
        Assert.Equal(
            "Cancel the whole order for The Garden Events Co.: 2 bookings (261009-001, 261009-002).",
            BookingPrompts.Cancel(["261009-001", "261009-002"], "The Garden Events Co.", 0));
    }

    [Fact]
    public void Cancel_summary_adds_the_refund_note_when_money_was_paid()
    {
        Assert.Equal(
            "Cancel the whole order for The Garden Events Co.: 2 bookings (261009-001, 261009-002). ₱2,000 was already paid — handle any refund outside the app.",
            BookingPrompts.Cancel(["261009-001", "261009-002"], "The Garden Events Co.", 200000));
    }

    [Fact]
    public void Cancel_toast_names_one_booking_or_the_order()
    {
        Assert.Equal("261009-003 cancelled", BookingPrompts.Cancelled(["261009-003"]));
        Assert.Equal("Order cancelled · 2 bookings", BookingPrompts.Cancelled(["261009-001", "261009-002"]));
    }
}

public class KitchenChartLayoutTests
{
    private static readonly BusinessCalendar Cal = new(new FakeClock());

    private static readonly DateOnly Day = new(2026, 11, 8);   // the chart's date (tomorrow for the fake clock)
    private static readonly DateTime Now = new FakeClock().UtcNow; // Sat 2026-11-07 11:00 Manila
    private const double H = KitchenChartLayout.HourWidth;
    private const int Visible = 313 - (int)KitchenChartLayout.AxisWidth; // the scrolling part of a 375-px screen's chart

    private static DateTime Local(int hour, int minute = 0, int day = 8) => Cal.ToUtc(new DateTime(2026, 11, day, hour, minute, 0));

    private static KitchenChart Build(IReadOnlyList<KitchenBar> bars, DateOnly? day = null, DateTime? now = null) =>
        KitchenChartLayout.Build(bars, 313, Cal, day ?? Day, now ?? Now)!;

    [Theory]
    [InlineData(375, 313)]
    [InlineData(1000, 698)]  // the page column is capped at 760
    [InlineData(200, 260)]   // never narrower than 260
    public void Width_follows_the_screen_within_limits(int screen, int expected) =>
        Assert.Equal(expected, KitchenChartLayout.WidthFor(screen));

    [Fact]
    public void No_bars_means_no_chart() => Assert.Null(KitchenChartLayout.Build([], 313, Cal, Day, Now));

    [Fact]
    public void The_axis_is_the_whole_day_at_a_fixed_width_per_hour_whatever_the_screen()
    {
        var bars = new List<KitchenBar> { new(Local(14), Local(16), 1) };

        var narrow = Build(bars);
        var wide = KitchenChartLayout.Build(bars, 698, Cal, Day, Now)!;

        Assert.Equal(24 * H, narrow.PlotRight, 3);
        Assert.Equal((int)(24 * H + KitchenChartLayout.PadRight), narrow.PlotWidth);
        Assert.Equal(narrow.PlotWidth, wide.PlotWidth);
        Assert.Equal(313, narrow.ViewportWidth);
    }

    [Fact]
    public void One_tick_per_hour_from_12_AM_to_11_PM_with_the_first_left_anchored()
    {
        var chart = Build([new(Local(14), Local(16), 1)]);

        Assert.Equal(24, chart.XTicks.Count);
        Assert.Equal("12 AM", chart.XTicks[0].Label);
        Assert.Equal("1 AM", chart.XTicks[1].Label);
        Assert.Equal("12 PM", chart.XTicks[12].Label);
        Assert.Equal("11 PM", chart.XTicks[23].Label);
        Assert.Equal(0, chart.XTicks[0].X, 3);
        Assert.Equal(23 * H, chart.XTicks[23].X, 3);
        Assert.Equal("start", chart.XTicks[0].Anchor);
        Assert.All(chart.XTicks.Skip(1), t => Assert.Equal("middle", t.Anchor));
    }

    [Fact]
    public void Bars_sit_at_their_business_time_and_scale_to_the_busiest_step()
    {
        var bars = new List<KitchenBar>
        {
            new(Local(14), Local(14, 30), 1),
            new(Local(14, 30), Local(15, 30), 2),
            new(Local(15, 30), Local(16), 1),
        };

        var chart = Build(bars);

        Assert.Equal(3, chart.Bars.Count);
        Assert.Equal(14 * H, chart.Bars[0].X, 3);
        Assert.Equal(H / 2, chart.Bars[0].Width, 3);
        Assert.Equal(16 * H, chart.Bars[2].X + chart.Bars[2].Width, 3);
        Assert.Equal(chart.PlotTop, chart.Bars[1].Y, 3);                      // the tallest bar reaches the top
        Assert.Equal(chart.BaseY - chart.PlotTop, chart.Bars[1].Height, 3);
        Assert.Equal((chart.BaseY - chart.PlotTop) / 2, chart.Bars[0].Height, 3);
        Assert.Equal("2:00 PM", chart.Bars[0].StartLabel);
        Assert.Equal("3:30 PM", chart.Bars[1].EndLabel);
        Assert.Equal("2 cooking · 2:30 PM – 3:30 PM", chart.Bars[1].Tooltip);
    }

    [Fact]
    public void Bars_are_cut_at_the_day_edges_but_keep_their_real_times()
    {
        var bars = new List<KitchenBar>
        {
            new(Local(20, day: 7), Local(22, day: 7), 1),   // the day before: dropped
            new(Local(22, day: 7), Local(1), 2),            // crosses 12 AM
            new(Local(23), Local(2, day: 9), 1),            // crosses midnight at the end
        };

        var chart = Build(bars);

        Assert.Equal(2, chart.Bars.Count);
        Assert.Equal([0, 1], chart.Bars.Select(b => b.Index));
        Assert.Equal(0, chart.Bars[0].X, 3);
        Assert.Equal(H, chart.Bars[0].Width, 3);
        Assert.Equal("2 cooking · 10:00 PM – 1:00 AM", chart.Bars[0].Tooltip);
        Assert.Equal(23 * H, chart.Bars[1].X, 3);
        Assert.Equal(24 * H, chart.Bars[1].X + chart.Bars[1].Width, 3);
        Assert.Equal("2:00 AM", chart.Bars[1].EndLabel);
    }

    [Fact]
    public void Today_shows_a_now_line_and_opens_centred_on_it()
    {
        var today = new DateOnly(2026, 11, 7);
        var chart = Build([new(Local(14, day: 7), Local(16, day: 7), 1)], day: today);

        Assert.Equal(11 * H, chart.NowX!.Value, 3);
        Assert.Equal(11 * H - Visible / 2.0, chart.ScrollLeft, 3);
    }

    [Fact]
    public void Another_day_has_no_now_line_and_opens_an_hour_before_the_first_busy_bar()
    {
        var chart = Build([new(Local(10), Local(14), 0), new(Local(14), Local(16), 1)]);

        Assert.Null(chart.NowX);
        Assert.Equal(13 * H, chart.ScrollLeft, 3);
    }

    [Fact]
    public void The_opening_scroll_stays_inside_the_plot()
    {
        Assert.Equal(0, Build([new(Local(0, 30), Local(2), 1)]).ScrollLeft, 3);

        var late = Build([new(Local(23), Local(23, 30), 1)]);
        Assert.Equal(late.PlotWidth - Visible, late.ScrollLeft, 3);
    }

    [Fact]
    public void Y_ticks_step_by_one_and_by_two_above_six()
    {
        Assert.Equal(["0", "1", "2"], Build([new(Local(14), Local(15), 2)]).YTicks.Select(t => t.Label));
        Assert.Equal(["0", "2", "4", "6", "8"], Build([new(Local(14), Local(15), 8)]).YTicks.Select(t => t.Label));
    }

    [Fact]
    public void Count_labels_show_only_on_bars_at_least_18_wide_and_above_zero()
    {
        var bars = new List<KitchenBar>
        {
            new(Local(14), Local(14, 20), 1),     // 13 px wide
            new(Local(14, 20), Local(16), 0),     // wide but empty
            new(Local(16), Local(16, 30), 3),     // 20 px wide
        };

        var chart = Build(bars);

        Assert.Equal([false, false, true], chart.Bars.Select(b => b.ShowLabel));
    }
}

public class BookingScopeListStateTests
{
    private static BookingScopeState NewState() => new(new BusinessCalendar(new FakeClock()));

    [Fact]
    public void List_controls_default_to_all_pickup_soonest_first_and_follow_the_selected_date()
    {
        var s = NewState();
        Assert.Equal(BookingListOptions.Default, s.ListOptions);

        s.SelectDate(new DateOnly(2026, 11, 8));
        s.SetSearch("belly");
        s.SetStatusFilter(ItemStatus.Cooking);
        s.SetSort(BookingSortField.Customer);
        s.SetSortDirection(SortDirection.Desc);

        Assert.Equal(new BookingListOptions(new DateOnly(2026, 11, 8), "belly", ItemStatus.Cooking, BookingSortField.Customer, SortDirection.Desc), s.ListOptions);
    }

    [Fact]
    public void Changing_the_sort_field_keeps_the_direction_and_the_tab_keeps_the_filters()
    {
        var s = NewState();
        s.SetSortDirection(SortDirection.Desc);
        s.SetSort(BookingSortField.Product);
        s.SetSearch("x");
        s.SetTab(BookingTab.Dashboard);
        s.SetTab(BookingTab.List);

        Assert.Equal(SortDirection.Desc, s.SortDirection);
        Assert.Equal("x", s.Search);
    }

    [Fact]
    public void A_null_search_is_an_empty_search()
    {
        var s = NewState();
        s.SetSearch(null);
        Assert.Equal("", s.Search);
    }
}

public class BookingDefaultsTests
{
    private static readonly BusinessCalendar Cal = new(new FakeClock());

    [Fact]
    public void Default_pickup_is_now_plus_five_hours_in_Manila_time_cut_to_the_minute()
    {
        // 03:00:42 UTC = 11:00 Manila; + 5 h = 16:00.
        var now = new DateTime(2026, 11, 7, 3, 0, 42, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 11, 7, 16, 0, 0), BookingDefaults.PickupLocal(now, Cal));
    }

    [Fact]
    public void Default_pickup_rolls_into_the_next_business_day()
    {
        var now = new DateTime(2026, 11, 7, 12, 30, 0, DateTimeKind.Utc); // 20:30 Manila
        Assert.Equal(new DateTime(2026, 11, 8, 1, 30, 0), BookingDefaults.PickupLocal(now, Cal));
    }
}
