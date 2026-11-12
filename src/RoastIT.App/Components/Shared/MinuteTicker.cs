using RoastIT.App.Services;
using RoastIT.Core.Abstractions;

namespace RoastIT.App.Components.Shared;

/// <summary>
/// Calls its <c>onTick</c> callback at the start of every minute and when the app comes back to the front, so text that
/// depends on the time ("late to start", D38) stays current. Dispose it with the component that owns it.
/// </summary>
public sealed class MinuteTicker : IDisposable
{
    private readonly IClock _clock;
    private readonly AppForeground _foreground;
    private readonly Func<Task> _onTick;
    private readonly CancellationTokenSource _cts = new();

    public MinuteTicker(IClock clock, AppForeground foreground, Func<Task> onTick)
    {
        _clock = clock;
        _foreground = foreground;
        _onTick = onTick;
        _foreground.Resumed += OnResumed;
        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Manila is a whole number of hours from UTC, so the UTC second is also the local second.
                var now = _clock.UtcNow;
                var wait = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
                await Task.Delay(wait, ct);
                await TickAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // The owner was disposed.
        }
    }

    private void OnResumed() => _ = TickAsync();

    private async Task TickAsync()
    {
        try { await _onTick(); }
        catch (ObjectDisposedException) { /* the owner went away while the tick was queued */ }
    }

    public void Dispose()
    {
        _foreground.Resumed -= OnResumed;
        _cts.Cancel();
        _cts.Dispose();
    }
}
