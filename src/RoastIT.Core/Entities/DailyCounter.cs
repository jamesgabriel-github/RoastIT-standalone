namespace RoastIT.Core.Entities;

/// <summary>
/// The last booking number handed out for a business date (plan §7.1). Only ever changed by the store's claim statement,
/// inside the transaction that inserts the order.
/// </summary>
public class DailyCounter
{
    public DateOnly Date { get; set; }
    public int LastSeq { get; set; }
}
