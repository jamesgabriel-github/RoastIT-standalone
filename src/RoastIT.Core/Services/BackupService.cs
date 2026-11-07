using Microsoft.Extensions.Logging;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Core.Services;

/// <summary>A backup file just written to the cache folder, ready for the share sheet.</summary>
public sealed record ExportedBackup(string Path, string FileName, DateTime CreatedAtUtc);

/// <summary>
/// Settings → Export data / Import data (plan §10.5, D14–D17, D25). Both ask for the PIN every time
/// (<see cref="PinScope.Data"/>, single-use), enforced here and not only in the UI. Backups never carry the PIN:
/// export strips <c>app_lock</c>, import keeps this device's own row. Import always replaces, never appends.
/// </summary>
public sealed class BackupService(
    IBackupStore store,
    IAppLock appLock,
    IChangeNotifier changes,
    IClock clock,
    IBusinessCalendar calendar,
    ILogger<BackupService> logger)
{
    /// <summary>Writes <c>roastline-backup-YYYYMMDD-HHmm.roastline</c> into <paramref name="directory"/> and audits it.</summary>
    public async Task<ExportedBackup> ExportAsync(PinConfirmation? confirmation, string directory, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Data);

        var now = clock.UtcNow;
        var name = BackupFormat.FileName(calendar.ToLocal(now));
        var path = Path.Combine(directory, name);
        var audit = AuditLog.Create(AuditActions.DataExported, "backup", name, now, new { file = name });

        await store.ExportAsync(path, now, audit, ct);
        logger.LogInformation("Exported a backup file");
        return new ExportedBackup(path, name, now);
    }

    /// <summary>Checks a picked file without changing anything.</summary>
    public Task<BackupInspection> InspectAsync(string path, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        return store.InspectAsync(path, ct);
    }

    /// <summary>Bookings on this phone now, for the "This phone already has N bookings" warning (D25).</summary>
    public Task<int> CountBookingsOnDeviceAsync(CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        return store.CountBookingsAsync(ct);
    }

    /// <summary>The message for a file that can't be imported, or null when it can.</summary>
    public static string? ErrorFor(BackupInspection inspection) => inspection.Status switch
    {
        BackupFileStatus.Valid => null,
        BackupFileStatus.NewerSchema => BackupMessages.NewerVersion(inspection.Summary?.AppVersion),
        BackupFileStatus.Unreadable => BackupMessages.Unreadable,
        _ => BackupMessages.NotRoastline,
    };

    /// <summary>
    /// Replaces all data on this device with the backup at <paramref name="path"/>. The file is checked again here (it may
    /// have changed since the sheet inspected it); an unusable file throws <see cref="ValidationException"/> and nothing is
    /// touched. <paramref name="fileName"/> is what the owner picked, for the audit row.
    /// </summary>
    public async Task ImportAsync(string path, string fileName, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Data);

        var inspection = await store.InspectAsync(path, ct);
        if (ErrorFor(inspection) is { } error) throw new ValidationException(error);

        var s = inspection.Summary!;
        var now = clock.UtcNow;
        var audit = AuditLog.Create(AuditActions.DataImported, "backup", fileName, now, new
        {
            file = fileName,
            exportedAt = s.ExportedAtUtc,
            appVersion = s.AppVersion,
            schemaVersion = s.SchemaVersion,
            orders = s.Orders,
            bookings = s.Bookings,
            payments = s.Payments,
            products = s.Products,
        });

        await store.ImportAsync(path, audit, ct);
        logger.LogInformation("Imported a backup file (schema {Schema})", s.SchemaVersion);
        changes.NotifyDataReplaced();
    }
}
