using System.Text;
using Microsoft.Extensions.Logging;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Rules;

namespace RoastIT.Core.Services;

/// <summary>
/// The automatic CSV backup (plan §10.6, D27, D29, D30). The app calls <see cref="CheckAsync"/> on start, when it comes back
/// to the front, after unlocking and once a minute while it is in the foreground; the file is rewritten only when the last
/// save is an hour old or more. Nothing here is called by booking saves, and nothing here ever throws: a failed write is
/// logged as <c>csv.save_failed</c> and shown on the Settings card. Skipped while the app is locked. Register as a singleton.
/// </summary>
public sealed class CsvBackupService(
    IBookingStore store,
    ICsvFileStore files,
    ICsvBackupLog log,
    IAppLock appLock,
    IClock clock,
    IBusinessCalendar calendar,
    ILogger<CsvBackupService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsSupported => files.IsSupported;

    public CsvSaveInfo? LastSave => log.LastSave;

    /// <summary>The most recent save attempt failed (cleared by the next success).</summary>
    public bool LastSaveFailed { get; private set; }

    /// <summary>Raised (possibly off the UI thread) after every save attempt.</summary>
    public event Action? Changed;

    /// <summary>Saves when due. Returns true when a file was written.</summary>
    public async Task<bool> CheckAsync(CancellationToken ct = default)
    {
        if (!files.IsSupported || appLock.IsLocked) return false;
        if (!BookingsCsv.IsDue(log.LastSave?.SavedAtUtc, clock.UtcNow)) return false;
        return await SaveAsync(ct);
    }

    /// <summary>Writes the file now (the hourly save, or "tap to retry"). Returns true when it was written.</summary>
    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        if (!files.IsSupported || appLock.IsLocked) return false;
        if (!await _gate.WaitAsync(0, ct)) return false; // a save is already running

        try
        {
            var now = clock.UtcNow;
            var (from, to) = BookingsCsv.Window(now);
            var rows = BookingRows.WithOrderInfo(await store.ListRowsByPickupAsync(from, to.AddTicks(1), ct));
            var text = BookingsCsv.Build(rows, calendar);

            await files.WriteAsync(BookingsCsv.FileName, Encoding.UTF8.GetBytes(text), ct);

            log.Record(new CsvSaveInfo(now, rows.Count));
            LastSaveFailed = false;
            logger.LogInformation("csv.saved ({Rows} bookings)", rows.Count);
            return true;
        }
        catch (Exception ex)
        {
            LastSaveFailed = true;
            logger.LogWarning(ex, "csv.save_failed");
            return false;
        }
        finally
        {
            _gate.Release();
            try { Changed?.Invoke(); } catch { /* a broken screen must not break the save loop */ }
        }
    }
}
