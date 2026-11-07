namespace RoastIT.Core.Enums;

/// <summary>
/// Which family of critical actions a PIN confirmation is for (plan §9.5).
/// Menu: asked every time, single-use. Bookings: remembered for 3 minutes.
/// </summary>
public enum PinScope
{
    Menu,
    Bookings,
}
