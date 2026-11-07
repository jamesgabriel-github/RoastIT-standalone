using RoastIT.Core.Enums;

namespace RoastIT.Core.Entities;

/// <summary>One unit of a product (plan §6.2): its own booking ID, its own status.</summary>
public class Item
{
    /// <summary><c>YYMMDD-NNN</c> (plan §7.1). The primary key.</summary>
    public string BookingId { get; set; } = "";
    public Guid OrderKey { get; set; }
    public Guid ProductId { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Pending;

    /// <summary>Stamped when the item moves Pending → Cooking.</summary>
    public DateTime? StartedCookingAt { get; set; }

    public Order? Order { get; set; }
    public Product? Product { get; set; }
}
