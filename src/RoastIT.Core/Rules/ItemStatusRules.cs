using RoastIT.Core.Enums;

namespace RoastIT.Core.Rules;

/// <summary>The status pipeline and the cancel rule (plan §7.3, D8, D22).</summary>
public static class ItemStatusRules
{
    /// <summary>Pending → Cooking → Cooked → Completed. Completed and Cancelled have no next step.</summary>
    public static ItemStatus? Next(ItemStatus status) => status switch
    {
        ItemStatus.Pending => ItemStatus.Cooking,
        ItemStatus.Cooking => ItemStatus.Cooked,
        ItemStatus.Cooked => ItemStatus.Completed,
        _ => null,
    };

    public static string Label(ItemStatus status) => status switch
    {
        ItemStatus.Pending => "Pending",
        ItemStatus.Cooking => "Cooking",
        ItemStatus.Cooked => "Cooked",
        ItemStatus.Completed => "Completed",
        _ => "Cancelled",
    };

    /// <summary>The button text for the step out of this status ("Start cooking"), or null when there is none.</summary>
    public static string? ActionLabel(ItemStatus status) => status switch
    {
        ItemStatus.Pending => "Start cooking",
        ItemStatus.Cooking => "Mark cooked",
        ItemStatus.Cooked => "Mark completed",
        _ => null,
    };

    /// <summary>
    /// A whole order can be cancelled only while it has a Pending or Cooking item and none that is Cooked or Completed
    /// (D22). Already-cancelled items don't count either way.
    /// </summary>
    public static bool CanCancelOrder(IEnumerable<ItemStatus> statuses)
    {
        var any = false;
        foreach (var s in statuses)
        {
            if (s is ItemStatus.Cooked or ItemStatus.Completed) return false;
            if (s is ItemStatus.Pending or ItemStatus.Cooking) any = true;
        }
        return any;
    }

    /// <summary>True when the order has items and every one is Cancelled. Such an order drops out of the money figures (D23).</summary>
    public static bool IsFullyCancelled(IEnumerable<ItemStatus> statuses)
    {
        var any = false;
        foreach (var s in statuses)
        {
            if (s != ItemStatus.Cancelled) return false;
            any = true;
        }
        return any;
    }
}
