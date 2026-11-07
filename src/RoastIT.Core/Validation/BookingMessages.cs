namespace RoastIT.Core.Validation;

/// <summary>Limits on booking input (open question Q27).</summary>
public static class BookingLimits
{
    public const int CustomerMax = 80;
    public const int PhoneMax = 30;

    /// <summary>Bookings (product lines) in one order. A line is one booking whatever its weight (D34).</summary>
    public const int MaxUnitsPerOrder = 100;
}

/// <summary>User-facing texts for booking rules (bookings spec §8, §9). Shown inline or as toasts.</summary>
public static class BookingMessages
{
    public const string NoItems = "Add at least one item.";
    public const string PickupOutsideWindow = "Pickup must be within 6 months of today.";
    public const string CustomerTooLong = "Customer name can't be longer than 80 characters.";
    public const string PhoneTooLong = "Phone number can't be longer than 30 characters.";
    public const string TooManyUnits = "An order can have at most 100 bookings.";
    public const string WeightInvalid = "Enter a weight between 0.01 and 500 kg for every item.";
    public const string ProductGone = "That product isn't on the menu any more.";
    public const string OrderGone = "That booking isn't there any more.";
    public const string OrderCancelled = "This order is cancelled.";
    public const string PaymentTooSmall = "Enter an amount greater than 0.";
    public const string NothingToWaive = "There's no small balance left to waive.";
    public const string CantAdvance = "That booking can't move forward any more.";

    /// <summary>Toast after the PIN, when the order changed meanwhile (bookings spec §8.6).</summary>
    public const string CantCancelToast = "Can't cancel — part of this order is already cooked or completed.";

    /// <summary>Caption under the detail cards (bookings spec §8.5).</summary>
    public const string CantCancelCaption = "This order can't be cancelled: part of it is already cooked or completed.";

    /// <summary>Refusal when the order has a Cooked or Completed item (D35).</summary>
    public const string CantChangePickup = "Can't change the pickup — part of this order is already cooked or completed.";

    public static string NotAvailable(string product) => $"{product} isn't available for booking.";

    public static string AlreadyAtStatus(string bookingId, string status) => $"{bookingId} is already {status}.";
}
