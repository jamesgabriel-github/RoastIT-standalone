using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Rules;

/// <summary>Starting values of the New booking form.</summary>
public static class BookingDefaults
{
    public const int PickupLeadHours = 5;

    /// <summary>
    /// The default pickup: the current time plus 5 hours, as business-local wall-clock time (what the date-time field holds),
    /// with the minutes set to 00 (the hour is kept, so 11:42 + 5 h → 4:00 PM). Replaces the prototype's "tomorrow 10:00" (Q24).
    /// </summary>
    public static DateTime PickupLocal(DateTime nowUtc, IBusinessCalendar calendar)
    {
        var local = calendar.ToLocal(nowUtc.AddHours(PickupLeadHours));
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0);
    }
}
