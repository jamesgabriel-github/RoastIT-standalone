namespace RoastIT.Core.Enums;

/// <summary>
/// Where a booking (one unit) is in the kitchen (plan §6.1). The numbers are the pipeline order, so sorting by status
/// is a plain sort. They are stored as integers: never renumber.
/// </summary>
public enum ItemStatus
{
    Pending = 0,
    Cooking = 1,
    Cooked = 2,
    Completed = 3,
    Cancelled = 4,
}
