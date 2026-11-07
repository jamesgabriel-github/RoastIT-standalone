using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Rules;

/// <summary>Starting values of the New booking form.</summary>
public static class BookingDefaults
{
    public const int PickupLeadHours = 5;

    /// <summary>
    /// The default pickup: the current time plus 5 hours, as business-local wall-clock time (what the date-time field holds),
    /// cut to the minute. Replaces the prototype's "tomorrow 10:00" (Q24).
    /// </summary>
    public static DateTime PickupLocal(DateTime nowUtc, IBusinessCalendar calendar)
    {
        var local = calendar.ToLocal(nowUtc.AddHours(PickupLeadHours));
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0);
    }
}
