using RoastIT.Core.Abstractions;
using RoastIT.Core.Enums;

namespace RoastIT.App.Services;

/// <param name="Summary">The one-line action summary, e.g. "Hide Liempo from new bookings." (shared-ui-spec §7.3).</param>
/// <param name="ConfirmLabel">The accent key on the keypad, e.g. "Deactivate".</param>
public sealed record PinPromptRequest(string Summary, string ConfirmLabel, PinScope Scope);

/// <summary>
/// <see cref="Confirmed"/> false = the user cancelled (or the app locked): do nothing and leave the form open.
/// <see cref="Confirmation"/> is null when a Bookings action was allowed by the remembered 3-minute window.
/// </summary>
public readonly record struct PinPromptResult(bool Confirmed, PinConfirmation? Confirmation)
{
    public static PinPromptResult Cancelled => new(false, null);
}

/// <summary>
/// Asks for the PIN before a critical action (plan §9.5). The prompt is drawn by <c>PinPrompt</c>, hosted in
/// <c>AppHost</c>. The service methods still refuse without a valid confirmation; this only collects it.
/// </summary>
public sealed class PinPromptService
{
    private readonly IAppLock _appLock;
    private readonly object _gate = new();
    private TaskCompletionSource<PinPromptResult>? _pending;

    public PinPromptService(IAppLock appLock)
    {
        _appLock = appLock;
        // Locking closes any open prompt: the window is gone, nothing should run.
        _appLock.LockStateChanged += () => { if (_appLock.IsLocked) Cancel(); };
    }

    public PinPromptRequest? Current { get; private set; }

    /// <summary>Raised (possibly off the UI thread) when a prompt opens or closes.</summary>
    public event Action? Changed;

    public Task<PinPromptResult> RequestAsync(string summary, string confirmLabel, PinScope scope)
    {
        if (_appLock.IsLocked) return Task.FromResult(PinPromptResult.Cancelled);

        // Bookings: skip the prompt inside the remembered window.
        if (scope == PinScope.Bookings && _appLock.HasRecentConfirmation(PinScope.Bookings))
            return Task.FromResult(new PinPromptResult(true, null));

        TaskCompletionSource<PinPromptResult> tcs;
        lock (_gate)
        {
            if (_pending is not null) return Task.FromResult(PinPromptResult.Cancelled); // one prompt at a time
            tcs = _pending = new TaskCompletionSource<PinPromptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Current = new PinPromptRequest(summary, confirmLabel, scope);
        }
        Changed?.Invoke();
        return tcs.Task;
    }

    /// <summary>Called by the prompt after a correct PIN.</summary>
    public void Complete(PinConfirmation confirmation) => Finish(new PinPromptResult(true, confirmation));

    /// <summary>Cancel, tapping outside, Back, or the app locking.</summary>
    public void Cancel() => Finish(PinPromptResult.Cancelled);

    private void Finish(PinPromptResult result)
    {
        TaskCompletionSource<PinPromptResult>? tcs;
        lock (_gate)
        {
            tcs = _pending;
            _pending = null;
            Current = null;
        }
        if (tcs is null) return;
        Changed?.Invoke();
        tcs.TrySetResult(result);
    }
}
