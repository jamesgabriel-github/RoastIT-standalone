namespace RoastIT.Core.Entities;

/// <summary>
/// A menu item (plan §6.2, §7.7). Price is <c>long</c> centavos. Products are deactivated, never deleted:
/// existing bookings keep pointing at them.
/// </summary>
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Centavos, ≥ 0 (₱2,200 = 220000).</summary>
    public long Price { get; set; }

    /// <summary>Whole minutes, ≥ 1.</summary>
    public int CookingTimeMin { get; set; }

    /// <summary>Null when blank.</summary>
    public string? Description { get; set; }

    /// <summary>False hides the product from new bookings. Existing bookings keep it.</summary>
    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
