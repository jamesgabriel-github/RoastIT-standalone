namespace RoastIT.Core.Abstractions;

/// <summary>
/// Tells open screens that data changed so they re-query (plan §5 rule 5). Services call these
/// <b>only after</b> the save succeeded. Handlers may run on any thread; pages marshal to the UI thread.
/// Phase 3 adds <c>BookingsChanged(date)</c> and <c>OrderChanged(orderKey)</c>.
/// </summary>
public interface IChangeNotifier
{
    /// <summary>A product was added, edited, activated or deactivated.</summary>
    event Action? CatalogChanged;

    void NotifyCatalogChanged();
}
