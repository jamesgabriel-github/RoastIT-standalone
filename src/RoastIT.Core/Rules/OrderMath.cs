using RoastIT.Core.Entities;

namespace RoastIT.Core.Rules;

/// <summary>
/// The one calculation of what an order costs and what is still owed (plan §7.2). The Dashboard, the list, the detail
/// sheet and the services all call this; nothing recomputes it on its own. Centavos throughout.
/// </summary>
public static class OrderMath
{
    public static long NetTotal(long total, long discount) => total - Clamp(discount, total);

    public static long Balance(long total, long discount, long paid) => Math.Max(0, NetTotal(total, discount) - paid);

    public static long NetTotal(Order o) => NetTotal(o.TotalAmount, o.DiscountAmount);

    public static long Balance(Order o) => Balance(o.TotalAmount, o.DiscountAmount, o.AmountPaid);

    /// <summary>
    /// What one booking costs: the product's price is per kilo (D34), so price × weight, rounded to the nearest centavo
    /// (halves away from zero). Decimal maths, so 3.2 kg of ₱2,200 is exactly ₱7,040.
    /// </summary>
    public static long LineTotal(long pricePerKilo, int weightGrams) =>
        (long)Math.Round((decimal)pricePerKilo * weightGrams / Weight.GramsPerKilo, 0, MidpointRounding.AwayFromZero);

    /// <summary>A discount is a flat amount within [0, total].</summary>
    public static long Clamp(long discount, long total) => Math.Min(Math.Max(discount, 0), Math.Max(total, 0));

    /// <summary>
    /// True when a leftover balance is small enough to offer "Discount remaining balance": 0 &lt; balance &lt; 10% of the
    /// <b>gross</b> total (plan §7.2). Integer maths, so exactly 10% is not small.
    /// </summary>
    public static bool IsWriteOffBalance(long total, long discount, long paid)
    {
        var balance = Balance(total, discount, paid);
        return balance > 0 && balance * 10 < total;
    }

    /// <summary>The balance as a percentage of the gross total, rounded to 0.1 (the write-off banner text).</summary>
    public static decimal BalancePercentOfTotal(long total, long balance) =>
        total <= 0 ? 0 : Math.Round(balance * 100m / total, 1, MidpointRounding.AwayFromZero);
}
