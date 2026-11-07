using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Services;

/// <summary>Register as a singleton. A subscriber that throws must not stop the others or fail the save that raised it.</summary>
public sealed class ChangeNotifier : IChangeNotifier
{
    public event Action? CatalogChanged;
    public event Action<DateOnly>? BookingsChanged;
    public event Action<Guid>? OrderChanged;

    public void NotifyCatalogChanged() => Raise(CatalogChanged, h => ((Action)h)());

    public void NotifyBookingsChanged(DateOnly pickupDate) => Raise(BookingsChanged, h => ((Action<DateOnly>)h)(pickupDate));

    public void NotifyOrderChanged(Guid orderKey) => Raise(OrderChanged, h => ((Action<Guid>)h)(orderKey));

    private static void Raise(Delegate? handlers, Action<Delegate> invoke)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try { invoke(handler); }
            catch { /* the data is already saved; a broken screen must not turn that into an error */ }
        }
    }
}
