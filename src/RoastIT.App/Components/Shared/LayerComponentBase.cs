using Microsoft.AspNetCore.Components;
using RoastIT.App.Services;

namespace RoastIT.App.Components.Shared;

/// <summary>
/// Base for anything that opens over the page (full sheet, bottom sheet, dialog, PIN prompt).
/// While <see cref="Open"/> it is registered with <see cref="BackNavigationService"/>, so Android
/// Back / Escape close it through <see cref="OnClose"/> (same as tapping the backdrop).
/// </summary>
public abstract class LayerComponentBase : ComponentBase, IDisposable
{
    private IDisposable? _registration;

    [Inject] protected BackNavigationService BackNavigation { get; set; } = default!;

    [Parameter] public bool Open { get; set; }

    /// <summary>Raised for backdrop tap, the close button, Back and Escape. The parent sets Open to false.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    protected abstract LayerKind Kind { get; }

    protected override void OnParametersSet()
    {
        if (Open && _registration is null)
        {
            _registration = BackNavigation.Push(Kind, CloseAsync);
        }
        else if (!Open && _registration is not null)
        {
            _registration.Dispose();
            _registration = null;
        }
    }

    protected Task CloseAsync() => InvokeAsync(() => OnClose.InvokeAsync());

    public virtual void Dispose()
    {
        _registration?.Dispose();
        _registration = null;
        GC.SuppressFinalize(this);
    }
}
