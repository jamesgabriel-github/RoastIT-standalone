using System.Text.Json;

namespace RoastIT.Core.Rules;

/// <summary>
/// Which fields the detailed booking card shows (bookings spec §2, plan §10.4). Booking ID and the order tag always show.
/// Stored as JSON in <c>Preferences["bookingColumns"]</c> with the keys below; a missing or broken value means "all on".
/// Settings (Phase 4) edits it.
/// </summary>
public sealed record BookingColumns(
    bool Customer = true,
    bool Product = true,
    bool CookingTime = true,
    bool Pickup = true,
    bool Status = true,
    bool Total = true,
    bool Paid = true)
{
    public static BookingColumns All => new();

    public bool ShowsProductLine => Product || CookingTime;

    public bool ShowsMoneyLine => Total || Paid;

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, bool>
    {
        ["customer"] = Customer,
        ["product"] = Product,
        ["cookingTime"] = CookingTime,
        ["pickup"] = Pickup,
        ["status"] = Status,
        ["total"] = Total,
        ["paid"] = Paid,
    });

    public static BookingColumns Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return All;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
            if (map is null) return All;
            bool Get(string key) => !map.TryGetValue(key, out var on) || on;
            return new BookingColumns(Get("customer"), Get("product"), Get("cookingTime"), Get("pickup"), Get("status"), Get("total"), Get("paid"));
        }
        catch (JsonException)
        {
            return All;
        }
    }
}

/// <summary>One switch in Settings → Columns to show.</summary>
public sealed record BookingColumnField(string Key, string Label, Func<BookingColumns, bool> Get, Func<BookingColumns, bool, BookingColumns> Set);

/// <summary>The Settings rows for the booking card fields, in the prototype's order (settings spec §7).</summary>
public static class BookingColumnFields
{
    public static readonly IReadOnlyList<BookingColumnField> All =
    [
        new("customer", "Customer", c => c.Customer, (c, on) => c with { Customer = on }),
        new("product", "Product", c => c.Product, (c, on) => c with { Product = on }),
        new("cookingTime", "Cooking time", c => c.CookingTime, (c, on) => c with { CookingTime = on }),
        new("pickup", "Pickup", c => c.Pickup, (c, on) => c with { Pickup = on }),
        new("status", "Status", c => c.Status, (c, on) => c with { Status = on }),
        new("total", "Total amount", c => c.Total, (c, on) => c with { Total = on }),
        new("paid", "Amount paid", c => c.Paid, (c, on) => c with { Paid = on }),
    ];

    public static int HiddenCount(BookingColumns columns) => All.Count(f => !f.Get(columns));

    /// <summary>"Every column is shown." / "1 column hidden on the Bookings list." / "3 columns hidden on the Bookings list."</summary>
    public static string HiddenCaption(BookingColumns columns) => HiddenCount(columns) switch
    {
        0 => "Every column is shown.",
        1 => "1 column hidden on the Bookings list.",
        var n => $"{n} columns hidden on the Bookings list.",
    };
}
