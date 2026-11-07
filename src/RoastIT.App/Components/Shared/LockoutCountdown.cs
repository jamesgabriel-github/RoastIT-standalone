using RoastIT.Core.Abstractions;

namespace RoastIT.App.Components.Shared;

/// <summary>
/// Drives the "Too many tries. Try again in 27s." countdown on the lock screen and the PIN prompt.
/// The deadline itself lives in <c>app_lock.locked_until</c>; this only ticks the display once a second.
/// </summary>
public sealed class LockoutCountdown(IClock clock, Func<Task> onChanged) : IDisposable
{
    private DateTime _until;
    private System.Threading.Timer? _timer;

    public bool Active { get; private set; }

    public int SecondsLeft => Math.Max(0, (int)Math.Ceiling((_until - clock.UtcNow).TotalSeconds));

    public void Start(DateTime untilUtc)
    {
        Stop();
        _until = untilUtc;
        Active = SecondsLeft > 0;
        if (!Active) return;
        _timer = new System.Threading.Timer(_ => Tick(), null, 1000, 1000);
    }

    public void Stop()
    {
        Active = false;
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        if (SecondsLeft <= 0) Stop(); // the wait is over: input comes back
        _ = onChanged();
    }

    public void Dispose() => Stop();
}
