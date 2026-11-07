namespace RoastIT.Core.Entities;

/// <summary>
/// One customer order (plan §6.2). Money is <c>long</c> centavos. <see cref="TotalAmount"/> is computed from the catalog
/// at creation and never rewritten. <see cref="AmountPaid"/> is the rollup of <see cref="Payments"/>, updated in the same
/// transaction. Use <see cref="Rules.OrderMath"/> for the net total and the balance; never recompute them.
/// </summary>
public class Order
{
    public Guid Key { get; set; }

    /// <summary>Null or blank = walk-in customer.</summary>
    public string? Customer { get; set; }
    public string? Phone { get; set; }

    /// <summary>Business-local (Asia/Manila) date the order was created.</summary>
    public DateOnly OrderDate { get; set; }

    /// <summary>UTC instant of the pickup.</summary>
    public DateTime PickupAt { get; set; }

    public long TotalAmount { get; set; }

    /// <summary>Always within [0, <see cref="TotalAmount"/>].</summary>
    public long DiscountAmount { get; set; }
    public long AmountPaid { get; set; }
    public string? Notes { get; set; }

    public List<Item> Items { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}
