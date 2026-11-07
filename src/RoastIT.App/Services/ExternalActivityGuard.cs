using RoastIT.Core.Abstractions;

namespace RoastIT.App.Services;

/// <summary>
/// The app locks whenever its window stops and resumes (D6). The system file picker (Import) and share sheet (Export,
/// CSV) also take the screen, which would lock the app mid-import and on the way back (Q11). Just before opening one of
/// those, the caller calls <see cref="Expect"/>; the next stop/resume pair within <see cref="MaxAway"/> is then treated
/// as that system screen and doesn't lock. Leaving for anything else afterwards locks as usual. MAUI-free so it is tested.
/// </summary>
/// <remarks>
/// Android order: picker or share target → <c>Deactivated</c>, <c>Stopped</c>, … , <c>Resumed</c>, <c>Activated</c>.
/// A share sheet dismissed straight away only pauses the app (<c>Deactivated</c> → <c>Activated</c>), so
/// <see cref="OnActivated"/> drops an expectation that never turned into a stop.
/// </remarks>
public sealed class ExternalActivityGuard(IClock clock)
{
    /// <summary>A system screen that keeps the app away longer than this locks it as usual.</summary>
    public static readonly TimeSpan MaxAway = TimeSpan.FromMinutes(5);

    private readonly object _gate = new();
    private DateTime? _expectUntil;
    private bool _away;

    /// <summary>Call right before opening the file picker or a share sheet.</summary>
    public void Expect()
    {
        lock (_gate)
        {
            _expectUntil = clock.UtcNow + MaxAway;
            _away = false;
        }
    }

    /// <summary>The system screen is done (or never opened) and the app never left: forget the expectation.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (!_away) _expectUntil = null;
        }
    }

    /// <summary><c>Window.Stopped</c>. True = this is the expected system screen: don't lock.</summary>
    public bool OnStopped()
    {
        lock (_gate)
        {
            if (_expectUntil is { } until && clock.UtcNow <= until)
            {
                _away = true;
                return true;
            }
            _expectUntil = null;
            _away = false;
            return false;
        }
    }

    /// <summary><c>Window.Resumed</c>. True = coming back from the expected system screen in time: don't lock.</summary>
    public bool OnResumed()
    {
        lock (_gate)
        {
            var back = _away && _expectUntil is { } until && clock.UtcNow <= until;
            _away = false;
            _expectUntil = null;
            return back;
        }
    }

    /// <summary><c>Window.Activated</c>: the app has the screen again without having stopped.</summary>
    public void OnActivated() => Cancel();
}
