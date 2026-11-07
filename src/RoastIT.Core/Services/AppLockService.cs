using Microsoft.AspNetCore.Identity;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;

namespace RoastIT.Core.Services;

/// <summary>
/// Plan §9. Register as a singleton: it holds the locked state and the PIN-confirmation windows in memory.
/// Never log the PIN, its hash or the recovery code.
/// </summary>
public sealed class AppLockService(IAppLockStore store, IClock clock) : IAppLock
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);
    /// <summary>How long a single-use (Menu or Data) confirmation stays usable.</summary>
    public static readonly TimeSpan MenuConfirmationLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan BookingsWindow = TimeSpan.FromMinutes(3);

    private static readonly object HasherUser = new();

    private readonly PasswordHasher<object> _hasher = new();
    private readonly SemaphoreSlim _gate = new(1, 1);   // serialises read-modify-write of the attempt counter
    private readonly object _state = new();             // guards the in-memory fields below
    private bool _locked = true;                        // the app always starts locked
    // Unused single-use confirmations (Menu and Data scopes): id → (scope, issued at).
    private readonly Dictionary<Guid, (PinScope Scope, DateTime IssuedAt)> _singleUse = [];
    private DateTime? _bookingsWindowUntil;

    public event Action? LockStateChanged;

    public bool IsLocked { get { lock (_state) return _locked; } }

    public void EnsureUnlocked()
    {
        if (IsLocked) throw new AccessDeniedException();
    }

    public async Task<bool> IsSetUpAsync(CancellationToken ct = default) =>
        await store.GetAsync(ct) is not null;

    public async Task<string> SetUpAsync(string pin, CancellationToken ct = default)
    {
        PinRules.EnsureValid(pin);
        await _gate.WaitAsync(ct);
        try
        {
            // Without this a page could overwrite an existing PIN without knowing it.
            if (await store.GetAsync(ct) is not null)
                throw new AccessDeniedException("A PIN is already set up.");

            var code = RecoveryCode.Generate();
            await store.SaveAsync(new AppLockState
            {
                PinHash = Hash(pin),
                RecoveryCodeHash = Hash(RecoveryCode.Normalize(code)),
                FailedAttempts = 0,
                LockedUntil = null,
                UpdatedAt = clock.UtcNow,
            }, audit: null, ct);

            SetLocked(false);
            return code;
        }
        finally { _gate.Release(); }
    }

    public async Task<PinAttempt> UnlockAsync(string pin, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var (attempt, _) = await CheckAsync(s => Verify(s.PinHash, pin), ct);
            if (attempt.Succeeded) SetLocked(false);
            return attempt;
        }
        finally { _gate.Release(); }
    }

    public async Task<PinAttempt> VerifyPinAsync(string pin, CancellationToken ct = default)
    {
        EnsureUnlocked();
        await _gate.WaitAsync(ct);
        try { return (await CheckAsync(s => Verify(s.PinHash, pin), ct)).Attempt; }
        finally { _gate.Release(); }
    }

    public async Task<PinAttempt> ChangeAsync(string currentPin, string newPin, CancellationToken ct = default)
    {
        EnsureUnlocked();
        PinRules.EnsureValid(newPin);
        await _gate.WaitAsync(ct);
        try
        {
            var (attempt, state) = await CheckAsync(s => Verify(s.PinHash, currentPin), ct);
            if (!attempt.Succeeded) return attempt;

            var now = clock.UtcNow;
            state!.PinHash = Hash(newPin);
            state.UpdatedAt = now;
            await store.SaveAsync(state, AuditLog.PinChanged(now), ct);
            return attempt;
        }
        finally { _gate.Release(); }
    }

    public async Task<PinAttempt> VerifyRecoveryCodeAsync(string code, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return (await CheckAsync(s => Verify(s.RecoveryCodeHash, RecoveryCode.Normalize(code)), ct)).Attempt; }
        finally { _gate.Release(); }
    }

    public async Task<RecoveryResetResult> ResetWithRecoveryCodeAsync(string code, string newPin, CancellationToken ct = default)
    {
        PinRules.EnsureValid(newPin);
        await _gate.WaitAsync(ct);
        try
        {
            var (attempt, state) = await CheckAsync(s => Verify(s.RecoveryCodeHash, RecoveryCode.Normalize(code)), ct);
            if (!attempt.Succeeded) return new RecoveryResetResult(attempt, null);

            var now = clock.UtcNow;
            var newCode = RecoveryCode.Generate();
            state!.PinHash = Hash(newPin);
            state.RecoveryCodeHash = Hash(RecoveryCode.Normalize(newCode)); // the old code stops working
            state.UpdatedAt = now;
            await store.SaveAsync(state, AuditLog.PinResetWithRecovery(now), ct);

            SetLocked(false);
            return new RecoveryResetResult(attempt, newCode);
        }
        finally { _gate.Release(); }
    }

    public async Task<LockoutStatus> GetLockoutAsync(CancellationToken ct = default)
    {
        var state = await store.GetAsync(ct);
        if (state is null) return new LockoutStatus(false, null, MaxFailedAttempts);

        var now = clock.UtcNow;
        var lockedOut = state.LockedUntil is { } until && until > now;
        return new LockoutStatus(lockedOut, lockedOut ? state.LockedUntil : null, MaxFailedAttempts - state.FailedAttempts);
    }

    public async Task<ConfirmResult> ConfirmAsync(string pin, PinScope scope, CancellationToken ct = default)
    {
        EnsureUnlocked();
        await _gate.WaitAsync(ct);
        try
        {
            var (attempt, _) = await CheckAsync(s => Verify(s.PinHash, pin), ct);
            if (!attempt.Succeeded) return new ConfirmResult(attempt, null);

            var now = clock.UtcNow;
            var confirmation = new PinConfirmation(Guid.NewGuid(), scope, now);
            lock (_state)
            {
                if (scope != PinScope.Bookings)
                {
                    PruneExpiredSingleUse(now);
                    _singleUse[confirmation.Id] = (scope, now);
                }
                else if (_bookingsWindowUntil is not { } until || until <= now)
                {
                    // Fixed from the moment of entry; later actions never extend it.
                    _bookingsWindowUntil = now + BookingsWindow;
                }
            }
            return new ConfirmResult(attempt, confirmation);
        }
        finally { _gate.Release(); }
    }

    public bool HasRecentConfirmation(PinScope scope)
    {
        if (scope != PinScope.Bookings) return false;
        lock (_state)
        {
            return !_locked && _bookingsWindowUntil is { } until && until > clock.UtcNow;
        }
    }

    public void EnsureConfirmed(PinConfirmation? confirmation, PinScope scope)
    {
        EnsureUnlocked();
        if (scope == PinScope.Bookings)
        {
            if (!HasRecentConfirmation(PinScope.Bookings)) throw new PinRequiredException();
            return;
        }

        // Menu and Data: present, for this scope, unexpired, and not used before (consumed here).
        if (confirmation is null || confirmation.Scope != scope) throw new PinRequiredException();
        lock (_state)
        {
            var now = clock.UtcNow;
            if (!_singleUse.Remove(confirmation.Id, out var issued)
                || issued.Scope != scope
                || now - issued.IssuedAt > MenuConfirmationLifetime)
            {
                throw new PinRequiredException();
            }
        }
    }

    public void Lock() => SetLocked(true);

    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The shared attempt counter. Caller holds <see cref="_gate"/>. A locked-out call is refused without
    /// checking the secret and without counting. 5th wrong try starts the 30 s lockout and resets the counter.
    /// </summary>
    private async Task<(PinAttempt Attempt, AppLockState? State)> CheckAsync(Func<AppLockState, bool> verify, CancellationToken ct)
    {
        var state = await store.GetAsync(ct) ?? throw new AccessDeniedException("No PIN is set up yet.");
        var now = clock.UtcNow;

        if (state.LockedUntil is { } until && until > now)
            return (new PinAttempt(PinAttemptStatus.LockedOut, 0, until), null);

        if (verify(state))
        {
            if (state.FailedAttempts != 0 || state.LockedUntil is not null)
            {
                state.FailedAttempts = 0;
                state.LockedUntil = null;
                state.UpdatedAt = now;
                await store.SaveAsync(state, audit: null, ct);
            }
            return (new PinAttempt(PinAttemptStatus.Success, MaxFailedAttempts), state);
        }

        state.LockedUntil = null; // any earlier lockout has run out
        state.FailedAttempts++;
        state.UpdatedAt = now;

        if (state.FailedAttempts >= MaxFailedAttempts)
        {
            state.FailedAttempts = 0;
            state.LockedUntil = now + LockoutDuration;
            await store.SaveAsync(state, audit: null, ct);
            return (new PinAttempt(PinAttemptStatus.LockedOut, 0, state.LockedUntil), null);
        }

        await store.SaveAsync(state, audit: null, ct);
        return (new PinAttempt(PinAttemptStatus.Wrong, MaxFailedAttempts - state.FailedAttempts), null);
    }

    private string Hash(string secret) => _hasher.HashPassword(HasherUser, secret);

    private bool Verify(string hash, string provided) =>
        !string.IsNullOrEmpty(hash)
        && _hasher.VerifyHashedPassword(HasherUser, hash, provided) != PasswordVerificationResult.Failed;

    private void SetLocked(bool locked)
    {
        bool changed;
        lock (_state)
        {
            changed = _locked != locked;
            _locked = locked;
            if (locked)
            {
                _singleUse.Clear();
                _bookingsWindowUntil = null;
            }
        }
        if (changed) LockStateChanged?.Invoke();
    }

    private void PruneExpiredSingleUse(DateTime now)
    {
        foreach (var id in _singleUse.Where(kv => now - kv.Value.IssuedAt > MenuConfirmationLifetime).Select(kv => kv.Key).ToList())
            _singleUse.Remove(id);
    }
}
