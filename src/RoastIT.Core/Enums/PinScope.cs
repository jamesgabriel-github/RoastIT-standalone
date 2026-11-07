namespace RoastIT.Core.Enums;

/// <summary>
/// Which family of critical actions a PIN confirmation is for (plan §9.5).
/// Menu and Data: asked every time, single-use. Bookings: remembered for 3 minutes.
/// </summary>
public enum PinScope
{
    Menu,
    Bookings,

    /// <summary>Settings → Export data / Import data (D15). Single-use, like Menu; never opens or uses the Bookings window.</summary>
    Data,
}
