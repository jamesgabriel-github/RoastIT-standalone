using RoastIT.Core.Entities;

namespace RoastIT.Core.Abstractions;

/// <summary>What a picked file turned out to be (plan §10.5 step 2, settings spec §4.4).</summary>
public enum BackupFileStatus
{
    Valid,

    /// <summary>Not SQLite, failed <c>integrity_check</c>, or no <c>app_meta.app_id = 'roastline'</c>.</summary>
    NotRoastline,

    /// <summary>Its <c>schema_version</c> is newer than this app's; the summary still carries the file's app version.</summary>
    NewerSchema,

    /// <summary>The file couldn't be read at all.</summary>
    Unreadable,
}

/// <summary>What the import sheet shows about a backup file. Counts come from the file (bookings = items, cancelled included).</summary>
public sealed record BackupSummary(
    DateTime? ExportedAtUtc,
    string? AppVersion,
    int SchemaVersion,
    int Orders,
    int Bookings,
    int Payments,
    int Products);

public sealed record BackupInspection(BackupFileStatus Status, BackupSummary? Summary = null)
{
    public bool IsValid => Status == BackupFileStatus.Valid;
}

/// <summary>
/// The database file work behind Settings → Export / Import (plan §10.5). Rules (PIN, re-checks, audit content) live in
/// <c>BackupService</c>; this only does the SQLite work. Never copies the PIN or recovery code out of the device.
/// </summary>
public interface IBackupStore
{
    /// <summary>The schema this app writes: the number of migrations it knows. A file with a higher number is refused.</summary>
    int CurrentSchemaVersion { get; }

    /// <summary>
    /// Writes a consistent copy of the live database to <paramref name="destinationPath"/>, strips <c>app_lock</c>, stamps
    /// <c>app_meta.exported_at</c>, vacuums the copy (no leftover hash bytes), then saves <paramref name="audit"/> in the
    /// live database. The live database is never changed otherwise.
    /// </summary>
    Task ExportAsync(string destinationPath, DateTime exportedAtUtc, AuditLog audit, CancellationToken ct = default);

    /// <summary>Opens the file read-only and checks it. Never changes anything.</summary>
    Task<BackupInspection> InspectAsync(string path, CancellationToken ct = default);

    /// <summary>Bookings (items) in the live database, cancelled included.</summary>
    Task<int> CountBookingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Replaces the live database with the backup at <paramref name="path"/>: keeps this device's <c>app_lock</c> row,
    /// saves a safety copy first, migrates an older file, and writes <paramref name="audit"/> into the new data. If anything
    /// fails, the previous data is restored and the exception is rethrown. The device is never left half-imported.
    /// </summary>
    Task ImportAsync(string path, AuditLog audit, CancellationToken ct = default);
}
