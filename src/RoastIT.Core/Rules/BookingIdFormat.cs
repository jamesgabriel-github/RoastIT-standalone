using System.Globalization;

namespace RoastIT.Core.Rules;

/// <summary>Booking IDs are <c>YYMMDD-NNN</c>: the business date the booking was made plus a daily sequence (plan §7.1).</summary>
public static class BookingIdFormat
{
    public static string Format(DateOnly date, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{date:yyMMdd}-{sequence:000}");

    /// <summary>The IDs for a freshly claimed block: <c>lastSeq − count + 1 … lastSeq</c>, in order.</summary>
    public static IReadOnlyList<string> Block(DateOnly date, int lastSeq, int count) =>
        Enumerable.Range(lastSeq - count + 1, count).Select(n => Format(date, n)).ToList();
}
