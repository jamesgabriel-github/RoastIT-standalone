using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Rules;

public sealed record ChartBar(int Index, double X, double Y, double Width, double Height, int Count, bool ShowLabel, string StartLabel, string EndLabel)
{
    public string Tooltip => $"{Count} cooking · {StartLabel} – {EndLabel}";
}

public sealed record ChartXTick(double X, string Label, string Anchor);

public sealed record ChartYTick(double Y, string Label);

/// <summary>
/// Pixel layout of the kitchen load chart (bookings spec §11.3, D31). The y-axis labels sit in a fixed column
/// <see cref="KitchenChartLayout.AxisWidth"/> wide; everything else is drawn in a plot <see cref="PlotWidth"/> wide that
/// scrolls sideways inside the remaining <see cref="ViewportWidth"/>. X values are plot coordinates (0 = 12 AM).
/// <see cref="NowX"/> is set only when now falls on the chart's day. <see cref="ScrollLeft"/> is where the plot opens.
/// </summary>
public sealed record KitchenChart(
    int ViewportWidth, int PlotWidth, int Height, double PlotRight, double PlotTop, double BaseY,
    IReadOnlyList<ChartBar> Bars, IReadOnlyList<ChartXTick> XTicks, IReadOnlyList<ChartYTick> YTicks,
    double? NowX, double ScrollLeft);

public static class KitchenChartLayout
{
    public const int Height = 184;
    public const double AxisWidth = 30, PadRight = 8, PadTop = 18, PadBottom = 24;

    /// <summary>Plot pixels per hour: wide enough for an "11 PM" label under every hour.</summary>
    public const double HourWidth = 40;

    /// <summary>The chart's visible width for a screen width: the page column (max 760) less gutters and card padding, at least 260.</summary>
    public static int WidthFor(int screenWidth) => Math.Max(260, Math.Min(screenWidth, 760) - 32 - 30);

    /// <summary>
    /// Lays the bars out on a fixed axis for <paramref name="date"/>, 12 AM to the next midnight in business time, with a
    /// tick every hour. Bar parts outside the day are cut off; tooltips keep the bar's real times. Returns null when there are
    /// no bars. <paramref name="width"/> is the visible width from <see cref="WidthFor"/>.
    /// </summary>
    public static KitchenChart? Build(IReadOnlyList<KitchenBar> bars, int width, IBusinessCalendar calendar, DateOnly date, DateTime nowUtc)
    {
        if (bars.Count == 0) return null;

        var (dayStart, dayEnd) = calendar.DayRangeUtc(date);
        var plotRight = (dayEnd - dayStart).TotalHours * HourWidth;
        var plotWidth = (int)Math.Ceiling(plotRight + PadRight);
        var maxCount = bars.Max(b => b.Count);

        var plotH = Height - PadTop - PadBottom;
        var baseY = PadTop + plotH;
        double XScale(DateTime utc) => (utc - dayStart).TotalHours * HourWidth;
        double YScale(int count) => PadTop + (1 - (maxCount > 0 ? (double)count / maxCount : 0)) * plotH;
        string Clock(DateTime utc) => BookingFormat.Time(calendar.ToLocal(utc));

        var chartBars = new List<ChartBar>();
        foreach (var b in bars)
        {
            var start = b.StartUtc < dayStart ? dayStart : b.StartUtc;
            var end = b.EndUtc > dayEnd ? dayEnd : b.EndUtc;
            if (end <= start) continue;

            var x1 = XScale(start);
            var x2 = XScale(end);
            var y = YScale(b.Count);
            chartBars.Add(new ChartBar(chartBars.Count, x1, y, x2 - x1, Math.Max(0, baseY - y), b.Count,
                x2 - x1 >= 18 && b.Count > 0, Clock(b.StartUtc), Clock(b.EndUtc)));
        }

        var xTicks = new List<ChartXTick>();
        for (var at = dayStart; at < dayEnd; at = at.AddHours(1))
            xTicks.Add(new ChartXTick(XScale(at), BookingFormat.Hour(calendar.ToLocal(at)), at == dayStart ? "start" : "middle"));

        var yStep = maxCount > 6 ? 2 : 1;
        var yTicks = new List<ChartYTick>();
        for (var c = 0; c <= maxCount; c += yStep) yTicks.Add(new ChartYTick(YScale(c), c.ToString()));

        double? nowX = nowUtc >= dayStart && nowUtc < dayEnd ? XScale(nowUtc) : null;

        // Open centred on now (today), else an hour before the first busy bar.
        var visible = width - AxisWidth;
        var firstBusy = chartBars.FirstOrDefault(b => b.Count > 0);
        var target = nowX is { } nx ? nx - visible / 2
            : firstBusy is not null ? firstBusy.X - HourWidth
            : 0;
        var scrollLeft = Math.Clamp(target, 0, Math.Max(0, plotWidth - visible));

        return new KitchenChart(width, plotWidth, Height, plotRight, PadTop, baseY, chartBars, xTicks, yTicks, nowX, scrollLeft);
    }
}
