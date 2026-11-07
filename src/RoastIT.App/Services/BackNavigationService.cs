namespace RoastIT.App.Services;

/// <summary>
/// Layer kinds in closing priority order (shared-ui-spec §5.3): the PIN prompt closes first,
/// then a dialog, then a bottom sheet, then a full sheet. <see cref="PageBack"/> is a page's own
/// Back behaviour (the lock screen); it only runs when nothing is open over the page.
/// </summary>
public enum LayerKind
{
    PageBack = 0,
    FullSheet = 1,
    BottomSheet = 2,
    Dialog = 3,
    PinPrompt = 4,
}

/// <summary>
/// Keeps the stack of open layers. Android Back (hooked from <c>MainPage.OnBackButtonPressed</c>)
/// and Escape on Windows call <see cref="TryCloseTopAsync"/>, which closes only the top layer and
/// reports whether it handled the press, so Back never exits the app from inside a layer.
/// </summary>
public sealed class BackNavigationService
{
    private sealed record Layer(long Id, LayerKind Kind, Func<Task> Close);

    private readonly object _gate = new();
    private readonly List<Layer> _layers = [];
    private long _nextId;

    /// <summary>Raised after a layer opens or closes (possibly off the UI thread).</summary>
    public event Action? LayersChanged;

    /// <summary>True when Back has something to handle (any layer, including a page's own Back).</summary>
    public bool HasLayers { get { lock (_gate) return _layers.Count > 0; } }

    /// <summary>True while a sheet, dialog or PIN prompt is open over the page (the page doesn't scroll meanwhile).</summary>
    public bool HasOverlay { get { lock (_gate) return _layers.Any(l => l.Kind != LayerKind.PageBack); } }

    public bool HasFullSheet { get { lock (_gate) return _layers.Any(l => l.Kind == LayerKind.FullSheet); } }

    /// <summary>True while a bottom sheet, dialog or PIN prompt is open (toasts hide meanwhile).</summary>
    public bool HasBlockingOverlay { get { lock (_gate) return _layers.Any(l => l.Kind is LayerKind.BottomSheet or LayerKind.Dialog or LayerKind.PinPrompt); } }

    /// <summary>Registers an open layer. Dispose the result when the layer closes.</summary>
    public IDisposable Push(LayerKind kind, Func<Task> close)
    {
        Layer layer;
        lock (_gate)
        {
            layer = new Layer(++_nextId, kind, close);
            _layers.Add(layer);
        }
        LayersChanged?.Invoke();
        return new Registration(this, layer.Id);
    }

    /// <summary>Closes the top layer. Returns false when no layer is open (normal page Back applies).</summary>
    public async Task<bool> TryCloseTopAsync()
    {
        Layer? top;
        lock (_gate)
        {
            top = _layers
                .OrderByDescending(l => l.Kind)
                .ThenByDescending(l => l.Id)
                .FirstOrDefault();
        }
        if (top is null) return false;
        await top.Close();
        return true;
    }

    private void Remove(long id)
    {
        bool removed;
        lock (_gate) removed = _layers.RemoveAll(l => l.Id == id) > 0;
        if (removed) LayersChanged?.Invoke();
    }

    private sealed class Registration(BackNavigationService owner, long id) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Remove(id);
        }
    }
}
