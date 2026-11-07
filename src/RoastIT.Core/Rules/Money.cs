using System.Globalization;

namespace RoastIT.Core.Rules;

/// <summary>
/// Money is <c>long</c> centavos everywhere (₱1,250.50 = 125050). This is the only place that turns it into
/// text or back, for display and form fields. Culture-invariant, so the output is the same on every phone.
/// </summary>
public static class Money
{
    public const long CentavosPerPeso = 100;

    /// <summary>"₱2,200" for whole pesos, "₱1,250.50" otherwise. Negative values keep the sign after the peso sign.</summary>
    public static string Format(long centavos)
    {
        var abs = centavos < 0 ? -(decimal)centavos : centavos;
        var pesos = abs / CentavosPerPeso;
        var text = centavos % CentavosPerPeso == 0
            ? pesos.ToString("N0", CultureInfo.InvariantCulture)
            : pesos.ToString("N2", CultureInfo.InvariantCulture);
        return (centavos < 0 ? "-₱" : "₱") + text;
    }

    /// <summary>Value for a price field: "1200" or "1250.50" (no thousands separators, so the field can read it back).</summary>
    public static string ToFieldText(long centavos)
    {
        var pesos = (decimal)centavos / CentavosPerPeso;
        return centavos % CentavosPerPeso == 0
            ? pesos.ToString("0", CultureInfo.InvariantCulture)
            : pesos.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads pesos typed in a field ("1200", "1250.5", "0.75") into centavos. Plain digits with an optional decimal
    /// point and at most two decimals: no signs, thousands separators or exponents. Returns false otherwise.
    /// </summary>
    public static bool TryParsePesos(string? text, out long centavos)
    {
        centavos = 0;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) return false;
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pesos)) return false;

        var scaled = pesos * CentavosPerPeso;
        if (scaled != decimal.Truncate(scaled)) return false; // more than two decimals
        if (scaled > long.MaxValue) return false;
        centavos = (long)scaled;
        return true;
    }
}

/// <summary>"45 min", "2 hr", "2 hr 30 min" (menu prototype).</summary>
public static class DurationFormat
{
    public static string Format(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} hr" : $"{hours} hr {rest} min";
    }
}
