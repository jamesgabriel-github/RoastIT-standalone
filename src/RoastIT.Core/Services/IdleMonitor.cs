using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Services;

/// <summary>
/// Locks the app after 30 minutes without taps (plan §9.4, D37). The UI reports activity
/// (<see cref="NotifyActivity"/>) and a timer calls <see cref="CheckAndLock"/>. Time spent in another app counts as
/// idle time, so the app also calls <see cref="CheckAndLock"/> when it comes back to the foreground.
/// </summary>
public sealed class IdleMonitor : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private readonly IClock _clock;
    private readonly IAppLock _appLock;
    private long _lastActivityTicks;

    public IdleMonitor(IClock clock, IAppLock appLock)
    {
        _clock = clock;
        _appLock = appLock;
        _lastActivityTicks = clock.UtcNow.Ticks;
        // A fresh unlock starts a fresh idle period.
        _appLock.LockStateChanged += NotifyActivity;
    }

    public void NotifyActivity() => Interlocked.Exchange(ref _lastActivityTicks, _clock.UtcNow.Ticks);

    /// <summary>Locks and returns true if the app has been idle for the full timeout.</summary>
    public bool CheckAndLock()
    {
        if (_appLock.IsLocked) return false;
        var idle = _clock.UtcNow - new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
        if (idle < Timeout) return false;
        _appLock.Lock();
        return true;
    }

    public void Dispose() => _appLock.LockStateChanged -= NotifyActivity;
}
