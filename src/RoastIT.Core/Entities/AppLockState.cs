namespace RoastIT.Core.Entities;

/// <summary>
/// The single <c>app_lock</c> row (id always 1). No row means first run. Never included in backups
/// (plan §10.5) and never written to Preferences or logs.
/// </summary>
public class AppLockState
{
    public const int SingleRowId = 1;

    public int Id { get; set; } = SingleRowId;

    /// <summary>PasswordHasher hash of the 4–6 digit PIN.</summary>
    public string PinHash { get; set; } = "";

    /// <summary>PasswordHasher hash of the normalised 12-character recovery code.</summary>
    public string RecoveryCodeHash { get; set; } = "";

    /// <summary>Wrong tries since the last success or lockout; shared by the lock screen and PIN prompts.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>Set after 5 wrong tries; input is refused until this instant (UTC).</summary>
    public DateTime? LockedUntil { get; set; }

    public DateTime UpdatedAt { get; set; }
}
