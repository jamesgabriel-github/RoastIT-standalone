using RoastIT.Core.Enums;

namespace RoastIT.Core.Entities;

/// <summary>One product line of an order (plan §6.2): its own booking ID, its own status, and its own weight (D34).</summary>
public class Item
{
    /// <summary><c>YYMMDD-NNN</c> (plan §7.1). The primary key.</summary>
    public string BookingId { get; set; } = "";
    public Guid OrderKey { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>How much was booked, in grams (3200 = 3.2 kg). The product's price is per kilo (D34).</summary>
    public int WeightGrams { get; set; } = Rules.Weight.LegacyGrams;
    public ItemStatus Status { get; set; } = ItemStatus.Pending;

    /// <summary>Stamped when the item moves Pending → Cooking.</summary>
    public DateTime? StartedCookingAt { get; set; }

    public Order? Order { get; set; }
    public Product? Product { get; set; }
}
