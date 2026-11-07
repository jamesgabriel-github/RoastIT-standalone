using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;

namespace RoastIT.Tests.Core;

public class OrderMathTests
{
    [Theory]
    [InlineData(100000, 0, 100000)]
    [InlineData(100000, 25000, 75000)]
    [InlineData(100000, 100000, 0)]
    [InlineData(100000, 999999, 0)]   // clamped to the total
    [InlineData(100000, -500, 100000)] // negative counts as none
    public void NetTotal_clamps_the_discount(long total, long discount, long expected) =>
        Assert.Equal(expected, OrderMath.NetTotal(total, discount));

    [Theory]
    [InlineData(100000, 0, 0, 100000)]
    [InlineData(100000, 10000, 40000, 50000)]
    [InlineData(100000, 0, 150000, 0)]       // overpaid: never negative
    [InlineData(100000, 100000, 0, 0)]       // fully discounted
    public void Balance_is_net_total_minus_paid_and_never_negative(long total, long discount, long paid, long expected) =>
        Assert.Equal(expected, OrderMath.Balance(total, discount, paid));

    [Fact]
    public void The_Order_overloads_use_the_same_calculation()
    {
        var o = new Order { TotalAmount = 101500, DiscountAmount = 1500, AmountPaid = 40000 };
        Assert.Equal(100000, OrderMath.NetTotal(o));
        Assert.Equal(60000, OrderMath.Balance(o));
    }

    [Theory]
    [InlineData(200000, 0, 190000, true)]     // 5%
    [InlineData(200000, 0, 180001, true)]     // just under 10%
    [InlineData(200000, 0, 180000, false)]    // exactly 10%
    [InlineData(200000, 0, 200000, false)]    // nothing left
    [InlineData(200000, 0, 0, false)]
    [InlineData(200000, 100000, 90000, true)] // 10,000 left is 5% of the GROSS total, although it is 10% of the net
    public void Write_off_window_is_under_ten_percent_of_the_gross_total(long total, long discount, long paid, bool expected) =>
        Assert.Equal(expected, OrderMath.IsWriteOffBalance(total, discount, paid));

    [Fact]
    public void Balance_percentage_rounds_to_one_decimal_of_the_gross_total()
    {
        Assert.Equal(1.5m, OrderMath.BalancePercentOfTotal(100000, 1500));
        Assert.Equal(0.1m, OrderMath.BalancePercentOfTotal(1000000, 1049)); // 0.1049 -> 0.1
        Assert.Equal(0m, OrderMath.BalancePercentOfTotal(0, 100));
    }
}

public class ItemStatusRulesTests
{
    [Theory]
    [InlineData(ItemStatus.Pending, ItemStatus.Cooking, "Start cooking")]
    [InlineData(ItemStatus.Cooking, ItemStatus.Cooked, "Mark cooked")]
    [InlineData(ItemStatus.Cooked, ItemStatus.Completed, "Mark completed")]
    public void The_pipeline_goes_one_step_forward(ItemStatus from, ItemStatus to, string label)
    {
        Assert.Equal(to, ItemStatusRules.Next(from));
        Assert.Equal(label, ItemStatusRules.ActionLabel(from));
    }

    [Theory]
    [InlineData(ItemStatus.Completed)]
    [InlineData(ItemStatus.Cancelled)]
    public void Completed_and_cancelled_have_no_next_step(ItemStatus status)
    {
        Assert.Null(ItemStatusRules.Next(status));
        Assert.Null(ItemStatusRules.ActionLabel(status));
    }

    [Fact]
    public void Status_numbers_are_the_pipeline_order()
    {
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<ItemStatus>().Select(s => (int)s));
    }

    [Theory]
    [InlineData(new[] { ItemStatus.Pending }, true)]
    [InlineData(new[] { ItemStatus.Cooking, ItemStatus.Pending }, true)]
    [InlineData(new[] { ItemStatus.Pending, ItemStatus.Cancelled }, true)]
    [InlineData(new[] { ItemStatus.Pending, ItemStatus.Cooked }, false)]
    [InlineData(new[] { ItemStatus.Cooking, ItemStatus.Completed }, false)]
    [InlineData(new[] { ItemStatus.Cancelled, ItemStatus.Cancelled }, false)]
    [InlineData(new ItemStatus[0], false)]
    public void An_order_can_be_cancelled_only_while_nothing_is_cooked_or_completed(ItemStatus[] statuses, bool expected) =>
        Assert.Equal(expected, ItemStatusRules.CanCancelOrder(statuses));

    [Fact]
    public void Fully_cancelled_needs_at_least_one_item_and_all_cancelled()
    {
        Assert.True(ItemStatusRules.IsFullyCancelled([ItemStatus.Cancelled, ItemStatus.Cancelled]));
        Assert.False(ItemStatusRules.IsFullyCancelled([ItemStatus.Cancelled, ItemStatus.Pending]));
        Assert.False(ItemStatusRules.IsFullyCancelled([]));
    }
}

public class BusinessCalendarTests
{
    private static BusinessCalendar At(DateTime utc) => new(new FakeClock(utc));

    [Fact]
    public void Today_is_the_Manila_date_whatever_the_UTC_date_is()
    {
        Assert.Equal(new DateOnly(2026, 11, 7), At(new DateTime(2026, 11, 7, 15, 59, 59, DateTimeKind.Utc)).Today());
        Assert.Equal(new DateOnly(2026, 11, 8), At(new DateTime(2026, 11, 7, 16, 0, 0, DateTimeKind.Utc)).Today());
    }

    [Fact]
    public void A_day_range_is_Manila_midnight_to_midnight_in_UTC()
    {
        var (start, end) = At(DateTime.UtcNow).DayRangeUtc(new DateOnly(2026, 11, 8));

        Assert.Equal(new DateTime(2026, 11, 7, 16, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 11, 8, 16, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void Local_and_UTC_convert_both_ways()
    {
        var cal = At(DateTime.UtcNow);
        var utc = cal.ToUtc(new DateTime(2026, 11, 8, 10, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc), utc);
        Assert.Equal(new DateTime(2026, 11, 8, 10, 0, 0), cal.ToLocal(utc));
        Assert.Equal(new DateOnly(2026, 11, 8), cal.DateOf(utc));
    }
}

public class ScheduleWindowTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    [Fact]
    public void The_window_is_six_months_either_side_of_today()
    {
        Assert.Equal(new DateOnly(2026, 5, 7), ScheduleWindow.Min(Today));
        Assert.Equal(new DateOnly(2027, 5, 7), ScheduleWindow.Max(Today));
        Assert.True(ScheduleWindow.Contains(Today, new DateOnly(2026, 5, 7)));
        Assert.True(ScheduleWindow.Contains(Today, new DateOnly(2027, 5, 7)));
        Assert.False(ScheduleWindow.Contains(Today, new DateOnly(2026, 5, 6)));
        Assert.False(ScheduleWindow.Contains(Today, new DateOnly(2027, 5, 8)));
    }
}

public class BookingScopeStateTests
{
    private static BookingScopeState NewState() => new(new BusinessCalendar(new FakeClock())); // today = 2026-11-07

    [Fact]
    public void Starts_on_today_the_list_tab_and_a_strip_two_days_before_today()
    {
        var s = NewState();
        Assert.Equal(new DateOnly(2026, 11, 7), s.SelectedDate); // D32
        Assert.Equal("Today", BookingFormat.ScopeLabel(s.SelectedDate, s.Today));
        Assert.Equal(BookingTab.List, s.ActiveTab);
        Assert.Equal(new DateOnly(2026, 11, 5), s.StripStart);
        Assert.Equal(new DateOnly(2026, 11, 1), s.CalendarMonth);
    }

    [Fact]
    public void Dates_outside_the_window_are_rejected_whatever_asks()
    {
        var s = NewState();
        Assert.False(s.SelectDate(new DateOnly(2027, 5, 8)));
        Assert.False(s.ToggleDate(new DateOnly(2026, 5, 6)));
        Assert.Equal(new DateOnly(2026, 11, 7), s.SelectedDate); // unchanged
        Assert.True(s.SelectDate(new DateOnly(2027, 5, 7)));
        Assert.Equal(new DateOnly(2027, 5, 7), s.SelectedDate);
    }

    [Fact]
    public void Tapping_the_selected_date_clears_it()
    {
        var s = NewState();
        s.ToggleDate(new DateOnly(2026, 11, 7)); // today, selected at start
        Assert.Null(s.SelectedDate);
        s.ToggleDate(new DateOnly(2026, 11, 8));
        Assert.Equal(new DateOnly(2026, 11, 8), s.SelectedDate);
        s.ToggleDate(new DateOnly(2026, 11, 8));
        Assert.Null(s.SelectedDate);
    }

    [Fact]
    public void A_date_inside_the_visible_strip_keeps_the_strip_where_it_is()
    {
        var s = NewState();
        s.SelectDate(new DateOnly(2026, 11, 14)); // strip is Nov 5 .. Nov 14
        Assert.Equal(new DateOnly(2026, 11, 5), s.StripStart);
    }

    [Fact]
    public void A_date_outside_the_strip_moves_it_to_date_minus_two_and_the_calendar_to_its_month()
    {
        var s = NewState();
        s.SelectDate(new DateOnly(2026, 12, 25));
        Assert.Equal(new DateOnly(2026, 12, 23), s.StripStart);
        Assert.Equal(new DateOnly(2026, 12, 1), s.CalendarMonth);
    }

    [Fact]
    public void The_strip_is_clamped_at_both_window_edges()
    {
        var s = NewState();
        s.SelectDate(new DateOnly(2026, 5, 7));   // first selectable day: strip can't start before it
        Assert.Equal(new DateOnly(2026, 5, 7), s.StripStart);
        Assert.False(s.CanShiftStrip(-7));

        s.SelectDate(new DateOnly(2027, 5, 7));
        Assert.Equal(new DateOnly(2027, 4, 28), s.StripStart); // max − 9
        Assert.False(s.CanShiftStrip(7));
        s.ShiftStrip(7);
        Assert.Equal(new DateOnly(2027, 4, 28), s.StripStart);
    }

    [Fact]
    public void The_arrows_shift_the_strip_by_a_week()
    {
        var s = NewState();
        s.ShiftStrip(7);
        Assert.Equal(new DateOnly(2026, 11, 12), s.StripStart);
        s.ShiftStrip(-7);
        s.ShiftStrip(-7);
        Assert.Equal(new DateOnly(2026, 10, 29), s.StripStart);
    }

    [Fact]
    public void Today_selects_today_and_resets_the_strip()
    {
        var s = NewState();
        s.SelectDate(new DateOnly(2027, 3, 1));
        s.SelectToday();
        Assert.Equal(new DateOnly(2026, 11, 7), s.SelectedDate);
        Assert.Equal(new DateOnly(2026, 11, 5), s.StripStart);
        Assert.Equal(new DateOnly(2026, 11, 1), s.CalendarMonth);
    }

    [Fact]
    public void The_calendar_month_stops_at_the_first_and_last_month_of_the_window()
    {
        var s = NewState();
        for (var i = 0; i < 20; i++) s.ShiftMonth(-1);
        Assert.Equal(new DateOnly(2026, 5, 1), s.CalendarMonth);
        Assert.False(s.CanShiftMonth(-1));
        for (var i = 0; i < 20; i++) s.ShiftMonth(1);
        Assert.Equal(new DateOnly(2027, 5, 1), s.CalendarMonth);
        Assert.False(s.CanShiftMonth(1));
    }

    [Fact]
    public void Opening_the_calendar_shows_the_selected_dates_month_or_todays()
    {
        var s = NewState();
        s.SelectDate(new DateOnly(2026, 11, 8));
        s.ShiftMonth(2);
        s.OpenCalendar();
        Assert.Equal(new DateOnly(2026, 11, 1), s.CalendarMonth);

        s.SelectDate(null);
        s.ShiftMonth(1);
        s.OpenCalendar();
        Assert.Equal(new DateOnly(2026, 11, 1), s.CalendarMonth);
    }

    [Fact]
    public void Changed_is_raised_only_when_something_changes()
    {
        var s = NewState();
        var raised = 0;
        s.Changed += () => raised++;
        s.SetTab(BookingTab.List);     // already the list
        Assert.Equal(0, raised);
        s.SelectDate(null);            // today → all dates
        s.SelectDate(null);            // already all dates
        Assert.Equal(1, raised);
        s.SetTab(BookingTab.Dashboard);
        s.SelectDate(new DateOnly(2026, 11, 8));
        Assert.Equal(3, raised);
        Assert.Equal(BookingTab.Dashboard, s.ActiveTab);
    }
}

public class BookingFormatTests
{
    [Fact]
    public void Scope_label_names_today_yesterday_and_tomorrow()
    {
        var today = new DateOnly(2026, 10, 9);
        Assert.Equal("all dates", BookingFormat.ScopeLabel(null, today));
        Assert.Equal("Today", BookingFormat.ScopeLabel(today, today));
        Assert.Equal("Yesterday", BookingFormat.ScopeLabel(today.AddDays(-1), today));
        Assert.Equal("Tomorrow", BookingFormat.ScopeLabel(today.AddDays(1), today));
        Assert.Equal("Wed, Sep 30, 2026", BookingFormat.ScopeLabel(new DateOnly(2026, 9, 30), today));
    }

    [Fact]
    public void Dates_and_times_read_like_the_prototype()
    {
        Assert.Equal("Fri, Oct 9, 2026", BookingFormat.LongDate(new DateOnly(2026, 10, 9)));
        Assert.Equal("Oct 9", BookingFormat.ShortDate(new DateOnly(2026, 10, 9)));
        Assert.Equal("October 2026", BookingFormat.MonthYear(new DateOnly(2026, 10, 9)));
        Assert.Equal("5:00 PM", BookingFormat.Time(new DateTime(2026, 10, 9, 17, 0, 0)));
        Assert.Equal("12:05 AM", BookingFormat.Time(new DateTime(2026, 10, 9, 0, 5, 0)));
        Assert.Equal("12 AM", BookingFormat.Hour(new DateTime(2026, 10, 9, 0, 0, 0)));
        Assert.Equal("11 PM", BookingFormat.Hour(new DateTime(2026, 10, 9, 23, 0, 0)));
        Assert.Equal("Oct 9 · 5:00 PM", BookingFormat.DateTimeShort(new DateTime(2026, 10, 9, 17, 0, 0)));
    }

    [Fact]
    public void Booking_ids_are_two_digit_year_month_day_and_a_three_digit_sequence()
    {
        Assert.Equal("261009-001", BookingIdFormat.Format(new DateOnly(2026, 10, 9), 1));
        Assert.Equal("261009-1000", BookingIdFormat.Format(new DateOnly(2026, 10, 9), 1000));
        Assert.Equal(["261009-004", "261009-005", "261009-006"], BookingIdFormat.Block(new DateOnly(2026, 10, 9), 6, 3));
    }

    [Fact]
    public void Counts_read_singular_and_plural()
    {
        Assert.Equal("1 booking", BookingFormat.BookingCount(1));
        Assert.Equal("0 bookings", BookingFormat.BookingCount(0));
        Assert.Equal("Walk-in customer", BookingFormat.CustomerLabel("  "));
        Assert.Equal("Anna", BookingFormat.CustomerLabel(" Anna "));
    }
}
