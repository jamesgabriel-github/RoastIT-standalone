namespace RoastIT.Core.Rules;

/// <summary>The one-line summaries shown in the Bookings PIN prompt (shared-ui-spec §7.3).</summary>
public static class BookingPrompts
{
    public static string RecordPayment(long amount, string customerLabel, string bookingId) =>
        $"Record a {Money.Format(amount)} payment from {customerLabel} ({bookingId}).";

    public static string Waive(long balance, string customerLabel, string bookingId) =>
        $"Waive the remaining {Money.Format(balance)} for {customerLabel} ({bookingId}).";

    /// <summary>"Move the pickup for Diego Santos (261008-002) to Nov 9 · 5:00 PM." <paramref name="newPickupLocal"/> is business time.</summary>
    public static string ChangePickup(IReadOnlyList<string> activeIds, string customerLabel, DateTime newPickupLocal) =>
        $"Move the pickup for {customerLabel} ({string.Join(", ", activeIds)}) to {BookingFormat.DateTimeShort(newPickupLocal)}.";

    /// <summary>
    /// Cancel summary: one booking names it, several list every ID. When money was paid it adds the refund note.
    /// <paramref name="activeIds"/> are the order's non-cancelled bookings.
    /// </summary>
    public static string Cancel(IReadOnlyList<string> activeIds, string customerLabel, long amountPaid)
    {
        var text = activeIds.Count == 1
            ? $"Cancel booking {activeIds[0]} for {customerLabel}."
            : $"Cancel the whole order for {customerLabel}: {activeIds.Count} bookings ({string.Join(", ", activeIds)}).";
        return amountPaid > 0 ? $"{text} {Money.Format(amountPaid)} was already paid — handle any refund outside the app." : text;
    }

    /// <summary>The toast after a cancel: "261009-003 cancelled" or "Order cancelled · 2 bookings".</summary>
    public static string Cancelled(IReadOnlyList<string> cancelledIds) =>
        cancelledIds.Count == 1 ? $"{cancelledIds[0]} cancelled" : $"Order cancelled · {cancelledIds.Count} bookings";
}
