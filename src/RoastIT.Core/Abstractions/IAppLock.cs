using RoastIT.Core.Enums;

namespace RoastIT.Core.Abstractions;

public enum PinAttemptStatus
{
    Success,
    Wrong,
    /// <summary>5 wrong tries in a row: input is refused until <see cref="PinAttempt.LockedUntil"/>.</summary>
    LockedOut,
}

/// <summary>Result of checking a PIN or recovery code. Tries left and lockout come from the persisted counter.</summary>
public sealed record PinAttempt(PinAttemptStatus Status, int TriesLeft = 0, DateTime? LockedUntil = null)
{
    public bool Succeeded => Status == PinAttemptStatus.Success;

    public int SecondsLeft(DateTime nowUtc) =>
        LockedUntil is { } until ? Math.Max(0, (int)Math.Ceiling((until - nowUtc).TotalSeconds)) : 0;
}

/// <summary>Current lockout state, for opening a prompt that must show a running countdown.</summary>
public sealed record LockoutStatus(bool IsLockedOut, DateTime? LockedUntil, int TriesLeft);

/// <summary>
/// Proof that the PIN was entered for a critical action. Menu and Data confirmations are single-use and expire
/// after ~60 s; Bookings confirmations open a 3-minute window (plan §9.5).
/// </summary>
public sealed record PinConfirmation(Guid Id, PinScope Scope, DateTime IssuedAtUtc);

public sealed record ConfirmResult(PinAttempt Attempt, PinConfirmation? Confirmation);

public sealed record RecoveryResetResult(PinAttempt Attempt, string? NewRecoveryCode);

/// <summary>
/// The app lock: PIN setup, unlock, change, recovery reset, and PIN confirmations for critical actions.
/// Locked state lives in memory only, so the app always starts locked. Wrong-try counting and lockout
/// are persisted in <c>app_lock</c> and shared by the lock screen and the in-app PIN prompts.
/// </summary>
public interface IAppLock
{
    bool IsLocked { get; }

    /// <summary>Raised (possibly off the UI thread) after the app locks or unlocks.</summary>
    event Action? LockStateChanged;

    /// <summary>Throws <see cref="Exceptions.AccessDeniedException"/> when locked. First line of every data-changing service method.</summary>
    void EnsureUnlocked();

    Task<bool> IsSetUpAsync(CancellationToken ct = default);

    /// <summary>First run only. Saves the PIN, unlocks, and returns the recovery code (shown once; only its hash is stored).</summary>
    Task<string> SetUpAsync(string pin, CancellationToken ct = default);

    Task<PinAttempt> UnlockAsync(string pin, CancellationToken ct = default);

    /// <summary>Checks the current PIN without changing anything (the change-PIN flow's first step). Counts toward lockout.</summary>
    Task<PinAttempt> VerifyPinAsync(string pin, CancellationToken ct = default);

    /// <summary>Settings → Update PIN. The recovery code is unchanged.</summary>
    Task<PinAttempt> ChangeAsync(string currentPin, string newPin, CancellationToken ct = default);

    /// <summary>Checks a recovery code without changing anything. Counts toward lockout.</summary>
    Task<PinAttempt> VerifyRecoveryCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Forgot PIN: new PIN and a new recovery code (the old one stops working); unlocks the app.</summary>
    Task<RecoveryResetResult> ResetWithRecoveryCodeAsync(string code, string newPin, CancellationToken ct = default);

    Task<LockoutStatus> GetLockoutAsync(CancellationToken ct = default);

    /// <summary>Checks the PIN for a critical action. A wrong PIN counts toward the shared lockout.</summary>
    Task<ConfirmResult> ConfirmAsync(string pin, PinScope scope, CancellationToken ct = default);

    /// <summary>True only for Bookings, within the 3-minute window.</summary>
    bool HasRecentConfirmation(PinScope scope);

    /// <summary>
    /// Throws <see cref="Exceptions.PinRequiredException"/> if there's no valid confirmation. Bookings passes inside the
    /// remembered window (the confirmation can be null); a Menu or Data confirmation must be present, issued for the same
    /// scope, unexpired and unused, and is consumed.
    /// </summary>
    void EnsureConfirmed(PinConfirmation? confirmation, PinScope scope);

    /// <summary>Locks the app and clears every PIN-confirmation window.</summary>
    void Lock();
}
