using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Services;

/// <summary>Register as a singleton. A subscriber that throws must not stop the others or fail the save that raised it.</summary>
public sealed class ChangeNotifier : IChangeNotifier
{
    public event Action? CatalogChanged;

    public void NotifyCatalogChanged() => Raise(CatalogChanged);

    private static void Raise(Action? handlers)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch { /* the data is already saved; a broken screen must not turn that into an error */ }
        }
    }
}
