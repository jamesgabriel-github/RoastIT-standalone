using RoastIT.Core.Enums;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;

namespace RoastIT.Tests.Core;

public class StartCueTests
{
    private static readonly BusinessCalendar Cal = new(new FakeClock());

    // Lechon Belly: 2 hr cooking, pickup 5:00 PM Manila on Thu 2026-11-12 (09:00 UTC) → ideal start 3:00 PM (07:00 UTC).
    private static readonly DateTime Pickup = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime IdealStart = Pickup.AddHours(-2);

    private static BookingRow Row(ItemStatus status = ItemStatus.Pending, int cookingMin = 120, DateTime? pickup = null) =>
        new("261112-001", Guid.NewGuid(), status, null, Guid.NewGuid(), "Lechon Belly", cookingMin, "Ana", null,
            pickup ?? Pickup, 100000, 0, 0, null);

    [Fact]
    public void Late_exactly_at_the_ideal_start()
    {
        var cue = StartCue.For(Row(), IdealStart, Cal);

        Assert.NotNull(cue);
        Assert.False(cue.PickupPassed);
        Assert.Equal("Late to start", cue.Title);
        Assert.Equal("was due 3:00 PM (now)", cue.Detail);
        Assert.Equal("Late", cue.ShortFlag);
    }

    [Fact]
    public void No_warning_one_minute_before()
    {
        Assert.Null(StartCue.For(Row(), IdealStart.AddMinutes(-1), Cal));
        Assert.Null(StartCue.For(Row(), IdealStart.AddSeconds(-1), Cal));
    }

    [Fact]
    public void Says_how_long_ago_it_was_due()
    {
        var cue = StartCue.For(Row(), IdealStart.AddMinutes(20), Cal);

        Assert.Equal("was due 3:00 PM (20 min ago)", cue!.Detail);
        Assert.Equal("late to start, was due 3:00 PM (20 min ago)", cue.Aria);
    }

    [Fact]
    public void Seconds_do_not_round_up_to_the_next_minute()
    {
        var cue = StartCue.For(Row(), IdealStart.AddSeconds(59), Cal);

        Assert.Equal("was due 3:00 PM (now)", cue!.Detail);
    }

    [Fact]
    public void Not_started_once_the_pickup_has_passed()
    {
        var atPickup = StartCue.For(Row(), Pickup, Cal)!;
        Assert.True(atPickup.PickupPassed);
        Assert.Equal("Not started", atPickup.Title);
        Assert.Equal("pickup was 5:00 PM (now)", atPickup.Detail);

        var later = StartCue.For(Row(), Pickup.AddMinutes(148), Cal)!;
        Assert.Equal("pickup was 5:00 PM (2 hr 28 min ago)", later.Detail);
    }

    [Theory]
    [InlineData(ItemStatus.Cooking)]
    [InlineData(ItemStatus.Cooked)]
    [InlineData(ItemStatus.Completed)]
    [InlineData(ItemStatus.Cancelled)]
    public void Only_pending_bookings_are_flagged(ItemStatus status)
    {
        Assert.Null(StartCue.For(Row(status), Pickup.AddHours(3), Cal));
    }

    [Fact]
    public void A_time_on_another_day_includes_the_date()
    {
        // Pickup was yesterday 10:00 AM Manila (02:00 UTC on the 11th); now is Thu 5:00 PM Manila.
        var yesterday = new DateTime(2026, 11, 11, 2, 0, 0, DateTimeKind.Utc);

        var cue = StartCue.For(Row(pickup: yesterday), Pickup, Cal)!;

        Assert.Equal("pickup was Nov 11 · 10:00 AM (1 day 7 hr ago)", cue.Detail);
    }

    [Fact]
    public void Is_correct_across_Manila_midnight()
    {
        // Pickup 1:00 AM Manila on Fri 13th (17:00 UTC on the 12th), 3 hr cooking → ideal start 10:00 PM Manila on Thu 12th.
        var pickup = new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc);
        var row = Row(cookingMin: 180, pickup: pickup);
        var idealStart = new DateTime(2026, 11, 12, 14, 0, 0, DateTimeKind.Utc); // 10:00 PM Manila

        Assert.Null(StartCue.For(row, idealStart.AddMinutes(-1), Cal)); // 9:59 PM Manila
        var onTime = StartCue.For(row, idealStart.AddHours(1), Cal)!;   // 11:00 PM Manila, still the 12th
        Assert.Equal("was due 10:00 PM (1 hr ago)", onTime.Detail);

        var afterMidnight = StartCue.For(row, idealStart.AddHours(3), Cal)!; // 1:00 AM Manila on the 13th = the pickup
        Assert.True(afterMidnight.PickupPassed);
        Assert.Equal("pickup was 1:00 AM (now)", afterMidnight.Detail);
    }

    [Fact]
    public void The_detail_sheet_shows_the_ideal_start_before_it_is_late_and_only_for_pending()
    {
        Assert.Equal("3:00 PM", StartCue.StartByLabel(Row(), IdealStart.AddHours(-5), Cal));
        Assert.Equal("3:00 PM", StartCue.StartByLabel(Row(), IdealStart.AddHours(1), Cal));
        Assert.Null(StartCue.StartByLabel(Row(ItemStatus.Cooking), IdealStart, Cal));
        Assert.Null(StartCue.StartByLabel(Row(ItemStatus.Cancelled), IdealStart, Cal));
    }

    [Fact]
    public void The_detail_sheet_start_time_includes_the_date_on_another_day()
    {
        // Ideal start 3:00 PM on the 12th, viewed on the 14th.
        Assert.Equal("Nov 12 · 3:00 PM", StartCue.StartByLabel(Row(), Pickup.AddDays(2), Cal));
    }

    [Fact]
    public void The_detail_sheet_tag_reads_late_due_now_or_pickup_passed()
    {
        Assert.Equal("Due now", StartCue.For(Row(), IdealStart, Cal)!.Flag);
        Assert.Equal("Late · 20 min", StartCue.For(Row(), IdealStart.AddMinutes(20), Cal)!.Flag);
        Assert.Equal("Late · 1 hr 5 min", StartCue.For(Row(), IdealStart.AddMinutes(65), Cal)!.Flag);
        Assert.Equal("Pickup passed", StartCue.For(Row(), Pickup.AddMinutes(5), Cal)!.Flag);
    }

    [Fact]
    public void Uses_Manila_time_not_the_device_time_zone()
    {
        // 23:30 UTC on the 11th is already 7:30 AM Manila on the 12th: the ideal start (6:00 AM Manila) is "today", so no date.
        var pickup = new DateTime(2026, 11, 11, 23, 0, 0, DateTimeKind.Utc); // 7:00 AM Manila on the 12th
        var row = Row(cookingMin: 60, pickup: pickup);

        var cue = StartCue.For(row, new DateTime(2026, 11, 11, 22, 30, 0, DateTimeKind.Utc), Cal)!;

        Assert.Equal("was due 6:00 AM (30 min ago)", cue.Detail);
    }
}

public class DurationSpanTests
{
    [Theory]
    [InlineData(0, "1 min")]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 hr")]
    [InlineData(148, "2 hr 28 min")]
    [InlineData(1439, "23 hr 59 min")]
    [InlineData(1440, "1 day")]
    [InlineData(1620, "1 day 3 hr")]
    [InlineData(2 * 1440 + 59, "2 days")]
    public void Reads_as_minutes_hours_or_days(int minutes, string expected)
    {
        Assert.Equal(expected, DurationFormat.Span(minutes));
    }
}
