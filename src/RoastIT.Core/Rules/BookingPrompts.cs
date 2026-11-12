using RoastIT.Core.Enums;

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

    /// <summary>
    /// "Move 261008-001 (Lechon Chicken) back from Cooking to Pending. Its cooking start time (Oct 8 · 5:30 PM) will be
    /// cleared." The second sentence only when the start time is cleared (Cooking → Pending);
    /// <paramref name="cookingStartLocal"/> is business time.
    /// </summary>
    public static string RevertStatus(string bookingId, string product, ItemStatus from, ItemStatus to, DateTime? cookingStartLocal)
    {
        var text = $"Move {bookingId} ({product}) back from {ItemStatusRules.Label(from)} to {ItemStatusRules.Label(to)}.";
        return to == ItemStatus.Pending && cookingStartLocal is { } start
            ? $"{text} Its cooking start time ({BookingFormat.DateTimeShort(start)}) will be cleared."
            : text;
    }

    /// <summary>The toast after a revert: "261008-001 reverted to Pending".</summary>
    public static string Reverted(string bookingId, ItemStatus to) => $"{bookingId} reverted to {ItemStatusRules.Label(to)}";
}
