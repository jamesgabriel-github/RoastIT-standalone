namespace RoastIT.App.Services;

/// <summary>Toast durations per screen (shared-ui-spec §8).</summary>
public static class ToastDuration
{
    public static readonly TimeSpan Bookings = TimeSpan.FromMilliseconds(2600);
    public static readonly TimeSpan Menu = TimeSpan.FromMilliseconds(2400);
    public static readonly TimeSpan Settings = TimeSpan.FromMilliseconds(3000);
    public static readonly TimeSpan Lock = TimeSpan.FromMilliseconds(2400);
}

/// <summary>One toast at a time: a new toast replaces the current one and restarts the timer.</summary>
public sealed class ToastService
{
    private CancellationTokenSource? _cts;

    public string? Message { get; private set; }

    /// <summary>Raised (possibly off the UI thread) when the message appears or disappears.</summary>
    public event Action? Changed;

    public void Show(string message, TimeSpan? duration = null)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Message = message;
        Changed?.Invoke();
        _ = HideAfterAsync(duration ?? ToastDuration.Bookings, cts);
    }

    private async Task HideAfterAsync(TimeSpan duration, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(duration, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // replaced by a newer toast
        }
        if (!ReferenceEquals(_cts, cts)) return;
        Message = null;
        Changed?.Invoke();
    }
}
