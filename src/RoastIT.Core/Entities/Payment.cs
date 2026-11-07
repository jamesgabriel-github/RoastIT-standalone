namespace RoastIT.Core.Entities;

/// <summary>One payment on an order. Payments are additive and stay recorded even if the order is cancelled (D23).</summary>
public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderKey { get; set; }

    /// <summary>Centavos, &gt; 0.</summary>
    public long Amount { get; set; }
    public DateTime RecordedAt { get; set; }
}
