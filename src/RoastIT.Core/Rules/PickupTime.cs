using System.Globalization;

namespace RoastIT.Core.Rules;

/// <summary>
/// The New booking pickup is entered as separate parts (date, hour 1–12, minutes typed as text, AM/PM) so date and time
/// can't be mixed up (bookings prototype: "Pickup date &amp; time"). These helpers join and split those parts.
/// All values are business-local wall-clock time.
/// </summary>
public static class PickupTime
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Hour 1–12 and AM/PM to a 24-hour clock hour (12 AM = 0, 12 PM = 12). An hour outside 1–12 counts as 12.</summary>
    public static int Hour24(int hour12, bool pm)
    {
        var h = (hour12 is >= 1 and <= 12 ? hour12 : 12) % 12;
        return pm ? h + 12 : h;
    }

    /// <summary>True when the typed minutes are digits above 59. Empty is fine (it counts as 00).</summary>
    public static bool MinuteOutOfRange(string? raw) =>
        int.TryParse(raw, NumberStyles.None, Inv, out var n) && n > 59;

    /// <summary>What the minutes field holds while typing: digits only, the last two kept, so "00" + 7 → "07" → "75".</summary>
    public static string CleanMinuteInput(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length > 2 ? digits[^2..] : digits;
    }

    /// <summary>Tidies the minutes when the field is left: "7" → "07", "75" → "59", empty → "00".</summary>
    public static string NormalizeMinute(string? raw)
    {
        var n = int.TryParse(raw, NumberStyles.None, Inv, out var parsed) ? parsed : 0;
        return Math.Clamp(n, 0, 59).ToString("00", Inv);
    }

    /// <summary>The parts joined into one local date-time, cut to the minute.</summary>
    public static DateTime Compose(DateOnly date, int hour12, string? minuteText, bool pm)
    {
        var minute = int.Parse(NormalizeMinute(minuteText), Inv);
        return date.ToDateTime(new TimeOnly(Hour24(hour12, pm), minute));
    }

    /// <summary>A local date-time split into the form's parts: date, hour 1–12, minutes "00"–"59", and whether it is PM.</summary>
    public static (DateOnly Date, int Hour12, string Minute, bool Pm) Split(DateTime local)
    {
        var h12 = local.Hour % 12;
        return (DateOnly.FromDateTime(local), h12 == 0 ? 12 : h12, local.Minute.ToString("00", Inv), local.Hour >= 12);
    }

    /// <summary>True when the pickup is before the current business time (it is allowed, but the form warns).</summary>
    public static bool HasPassed(DateTime pickupLocal, DateTime nowLocal) => pickupLocal < nowLocal;
}
