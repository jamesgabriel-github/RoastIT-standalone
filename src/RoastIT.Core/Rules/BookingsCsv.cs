using System.Globalization;
using System.Text;
using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Rules;

/// <summary>
/// The automatic CSV backup file (plan §10.6, D29): every booking with pickup from 24 hours before to 24 hours after the
/// save, one row per booking, as a readable copy for when the app or database breaks. Not a restorable backup.
/// </summary>
public static class BookingsCsv
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string FileName = "roastline-bookings-48h.csv";

    /// <summary>Where the file lives, as the owner sees it in the Files app.</summary>
    public const string Folder = "Documents/Roastline";

    public static readonly TimeSpan HalfWindow = TimeSpan.FromHours(24);

    public static readonly TimeSpan SaveInterval = TimeSpan.FromHours(1);

    public static readonly IReadOnlyList<string> Header =
    [
        "Booking ID", "Order", "Customer", "Phone", "Product", "Weight (kg)", "Status", "Pickup date", "Pickup time", "Cooking time (min)",
        "Started cooking", "Order total", "Order discount", "Order net total", "Order paid", "Order balance", "Notes",
    ];

    /// <summary>The pickups a save at <paramref name="nowUtc"/> covers: both ends included.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Window(DateTime nowUtc) => (nowUtc - HalfWindow, nowUtc + HalfWindow);

    public static bool InWindow(DateTime pickupUtc, DateTime nowUtc)
    {
        var (from, to) = Window(nowUtc);
        return pickupUtc >= from && pickupUtc <= to;
    }

    /// <summary>
    /// Due when it was never saved, or last saved an hour ago or more. A last-save time in the future (the phone's clock
    /// was moved back) also counts as due, so the file can't get stuck.
    /// </summary>
    public static bool IsDue(DateTime? lastSavedUtc, DateTime nowUtc) =>
        lastSavedUtc is not { } last || nowUtc - last >= SaveInterval || last > nowUtc;

    /// <summary>
    /// The whole file: UTF-8 BOM, header, one CRLF-terminated line per booking sorted by pickup then booking ID. Rows must
    /// carry their order info (a fully cancelled order shows a balance of 0.00, D23). Money columns are per order and repeat
    /// on each of its rows. Dates and times are business-local.
    /// </summary>
    public static string Build(IEnumerable<BookingRow> rows, IBusinessCalendar calendar)
    {
        var list = rows.OrderBy(r => r.PickupAt).ThenBy(r => r.BookingId, StringComparer.Ordinal).ToList();
        var firstIdByOrder = list.GroupBy(r => r.OrderKey)
            .ToDictionary(g => g.Key, g => g.Select(r => r.BookingId).Min(StringComparer.Ordinal)!);

        var sb = new StringBuilder();
        sb.Append('﻿');
        Line(sb, Header);
        foreach (var r in list)
        {
            var pickup = calendar.ToLocal(r.PickupAt);
            Line(sb,
            [
                r.BookingId,
                firstIdByOrder[r.OrderKey],
                r.CustomerLabel,
                r.Phone ?? "",
                r.ProductName,
                Weight.ToFieldText(r.WeightGrams),
                ItemStatusRules.Label(r.Status),
                pickup.ToString("yyyy-MM-dd", Inv),
                pickup.ToString("h:mm tt", Inv),
                r.CookingTimeMin.ToString(Inv),
                r.StartedCookingAt is { } started ? calendar.ToLocal(started).ToString("yyyy-MM-dd h:mm tt", Inv) : "",
                Pesos(r.TotalAmount),
                Pesos(OrderMath.Clamp(r.DiscountAmount, r.TotalAmount)),
                Pesos(r.NetTotal),
                Pesos(r.AmountPaid),
                Pesos(r.BalanceDue),
                r.Notes ?? "",
            ]);
        }
        return sb.ToString();
    }

    /// <summary>"4400.00": two decimals, no peso sign or thousands separator, so spreadsheets read a number.</summary>
    public static string Pesos(long centavos) => (centavos / (decimal)Money.CentavosPerPeso).ToString("0.00", Inv);

    /// <summary>
    /// RFC 4180 quoting (a field with a comma, quote or line break is quoted, quotes doubled), and a leading <c>'</c> on a
    /// text cell that starts with = + - or @, so a customer name can never run as a spreadsheet formula.
    /// </summary>
    public static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static void Line(StringBuilder sb, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Cell(cells[i]));
        }
        sb.Append("\r\n");
    }

    /// <summary>"Pickups Nov 4, 10:00 PM – Nov 6, 10:00 PM" on the Settings card.</summary>
    public static string WindowLabel(DateTime savedAtUtc, IBusinessCalendar calendar)
    {
        var (from, to) = Window(savedAtUtc);
        string Fmt(DateTime utc) => calendar.ToLocal(utc).ToString("MMM d, h:mm tt", Inv);
        return $"Pickups {Fmt(from)} – {Fmt(to)}";
    }
}
