namespace RoastIT.Core.Abstractions;

/// <summary>
/// Tells open screens that data changed so they re-query (plan §5 rule 5). Services call these
/// <b>only after</b> the save succeeded. Handlers may run on any thread; pages marshal to the UI thread.
/// </summary>
public interface IChangeNotifier
{
    /// <summary>A product was added, edited, activated or deactivated.</summary>
    event Action? CatalogChanged;

    /// <summary>Bookings were created, or moved between statuses. The argument is the business date of the pickup.</summary>
    event Action<DateOnly>? BookingsChanged;

    /// <summary>An order's money, discount or items changed. The argument is the order key.</summary>
    event Action<Guid>? OrderChanged;

    /// <summary>Import replaced the whole database (plan §10.5 step 7): every screen and cached state re-reads everything.</summary>
    event Action? DataReplaced;

    void NotifyCatalogChanged();

    void NotifyDataReplaced();

    void NotifyBookingsChanged(DateOnly pickupDate);

    void NotifyOrderChanged(Guid orderKey);
}
