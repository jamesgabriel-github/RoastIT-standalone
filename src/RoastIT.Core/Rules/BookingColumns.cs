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
