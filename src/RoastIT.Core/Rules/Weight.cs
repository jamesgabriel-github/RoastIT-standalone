using System.Globalization;

namespace RoastIT.Core.Rules;

/// <summary>
/// A booking's weight (D34). The form takes kilos ("3.2"), the database keeps whole grams (3200), so nothing is ever a
/// floating-point number. Up to two decimals are accepted (10 g steps). Culture-invariant, like <see cref="Money"/>.
/// </summary>
public static class Weight
{
    public const int GramsPerKilo = 1000;

    /// <summary>The smallest weight a booking can have: 0.01 kg.</summary>
    public const int MinGrams = 10;

    /// <summary>The largest weight a booking can have: 500 kg. Keeps a mistyped "3200" from becoming a booking.</summary>
    public const int MaxGrams = 500 * GramsPerKilo;

    /// <summary>The +/- buttons on the form move by this much: 0.5 kg.</summary>
    public const int StepGrams = 500;

    /// <summary>What an existing booking from before D34 weighs: 1 kg, since it used to be one unit at the product's price.</summary>
    public const int LegacyGrams = GramsPerKilo;

    /// <summary>
    /// Reads kilos typed in a field ("3", "3.2", "0.75") into grams. Plain digits with an optional decimal point and at
    /// most two decimals: no signs, thousands separators or exponents. Returns false for anything else, and for a weight
    /// outside <see cref="MinGrams"/>–<see cref="MaxGrams"/>.
    /// </summary>
    public static bool TryParseKilos(string? text, out int grams)
    {
        grams = 0;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) return false;
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kilos)) return false;

        var scaled = kilos * GramsPerKilo;
        if (scaled != decimal.Truncate(scaled)) return false;              // more than three decimals
        if (scaled % MinGrams != 0) return false;                          // more than two decimals
        if (scaled < MinGrams || scaled > MaxGrams) return false;
        grams = (int)scaled;
        return true;
    }

    /// <summary>Value for the weight field: "3", "3.2" or "3.25" (no unit, no trailing zeros).</summary>
    public static string ToFieldText(int grams) =>
        ((decimal)grams / GramsPerKilo).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>"3.2 kg", "3 kg", "0.75 kg".</summary>
    public static string Format(int grams) => ToFieldText(grams) + " kg";

    /// <summary>
    /// The weight after pressing + (<paramref name="direction"/> 1) or − (−1) once. + stops at <see cref="MaxGrams"/>; −
    /// stops at one step (0.5 kg) and never lowers a weight that is already below it.
    /// </summary>
    public static int Step(int grams, int direction)
    {
        var next = grams + direction * StepGrams;
        return direction < 0 ? Math.Max(next, Math.Min(grams, StepGrams)) : Math.Min(next, MaxGrams);
    }
}
