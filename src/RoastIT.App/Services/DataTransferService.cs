using Microsoft.Extensions.Logging;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Services;

namespace RoastIT.App.Services;

/// <summary>A file the owner picked for import, copied into the cache folder and inspected.</summary>
/// <param name="ExistingBookings">Bookings on this phone right now (the D25 warning); 0 when the file isn't usable.</param>
public sealed record PickedBackup(string FileName, string LocalPath, BackupInspection Inspection, int ExistingBookings);

/// <summary>
/// The platform side of Settings → Data (settings spec §4.4): the cache folder, the share sheet and the file picker.
/// The rules and the PIN check are in <see cref="BackupService"/>. Opening a system screen goes through
/// <see cref="ExternalActivityGuard"/> so it doesn't lock the app (Q11).
/// </summary>
public sealed class DataTransferService(
    BackupService backups,
    PreferencesStore preferences,
    ExternalActivityGuard guard,
    IBackupFileSaver saver,
    ILogger<DataTransferService> logger)
{
    private static string ExportDir => Path.Combine(FileSystem.CacheDirectory, "exports");
    private static string ImportDir => Path.Combine(FileSystem.CacheDirectory, "import");

    public DateTime? LastExportAt => preferences.LastExportAt;

    /// <summary>
    /// Makes the backup file, opens the share sheet, then records the export time (Android can't tell whether the share
    /// target actually saved it, Q29). Throws when the file couldn't be made or the share sheet didn't open; the export
    /// time is not touched then.
    /// </summary>
    public async Task<ExportedBackup> ExportAsync(PinConfirmation confirmation)
    {
        var exported = await MakeFileAsync(confirmation);

        guard.Expect();
        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Save your Roastline backup",
                File = new ShareFile(exported.Path, "application/octet-stream"),
            });
        }
        catch (Exception ex)
        {
            guard.Cancel();
            logger.LogWarning(ex, "Share sheet failed to open for the backup file");
            throw;
        }
        preferences.LastExportAt = exported.CreatedAtUtc; // counts once the file exists and the share sheet opened (Q29)
        return exported;
    }

    /// <summary>True where the system "save as" screen is available (Android); the Export sheet shows "Save to a folder…".</summary>
    public bool CanSaveToFolder => saver.IsSupported;

    /// <summary>
    /// "Save to a folder…" (D33): makes the backup file, then lets the owner choose where it goes. Unlike the share sheet,
    /// the app knows the file was written, so the export time is recorded only then. Returns null when the owner backed
    /// out of the "save as" screen (nothing saved, export time untouched; the PIN confirmation is already used up).
    /// The returned name is the one the owner saved under. Throws when the file couldn't be made or written.
    /// </summary>
    public async Task<ExportedBackup?> SaveToFolderAsync(PinConfirmation confirmation)
    {
        if (!saver.IsSupported) throw new PlatformNotSupportedException("Saving to a folder is Android only for now.");
        var exported = await MakeFileAsync(confirmation);

        guard.Expect();
        try
        {
            var savedName = await saver.SaveAsync(exported.FileName, exported.Path);
            if (savedName is null) return null;
            preferences.LastExportAt = exported.CreatedAtUtc;
            return exported with { FileName = savedName };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saving the backup file to a folder failed");
            throw;
        }
        finally
        {
            guard.Cancel();
            ClearFolder(ExportDir); // the saved copy is the backup; don't leave another one in the cache
        }
    }

    private async Task<ExportedBackup> MakeFileAsync(PinConfirmation confirmation)
    {
        ClearFolder(ExportDir); // only the newest throwaway copy is kept
        return await Task.Run(() => backups.ExportAsync(confirmation, ExportDir));
    }

    /// <summary>Opens the file picker, copies the pick into the cache and inspects it. Null when the owner cancelled.</summary>
    public async Task<PickedBackup?> PickAsync()
    {
        FileResult? file;
        guard.Expect();
        try
        {
            // Any file type: Android often can't map ".roastline" to a MIME type; the content is what gets checked.
            file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a Roastline backup" });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "File picker failed");
            return new PickedBackup("", "", new BackupInspection(BackupFileStatus.Unreadable), 0);
        }
        finally
        {
            guard.Cancel();
        }
        if (file is null) return null;

        ClearFolder(ImportDir);
        Directory.CreateDirectory(ImportDir);
        var local = Path.Combine(ImportDir, "candidate.roastline");
        try
        {
            await using var source = await file.OpenReadAsync();
            await using var target = File.Create(local);
            await source.CopyToAsync(target);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't copy the picked file");
            return new PickedBackup(file.FileName, local, new BackupInspection(BackupFileStatus.Unreadable), 0);
        }

        var inspection = await backups.InspectAsync(local);
        var existing = inspection.IsValid ? await backups.CountBookingsOnDeviceAsync() : 0;
        return new PickedBackup(file.FileName, local, inspection, existing);
    }

    /// <summary>Replaces this device's data with the picked file. Throws on failure (the previous data is back in place).</summary>
    public async Task ImportAsync(PickedBackup picked, PinConfirmation confirmation)
    {
        await Task.Run(() => backups.ImportAsync(picked.LocalPath, picked.FileName, confirmation));
        Discard();
    }

    /// <summary>Deletes the cached pick (closing the sheet, or after a successful import).</summary>
    public void Discard() => ClearFolder(ImportDir);

    private void ClearFolder(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogInformation("Couldn't clear {Folder}", Path.GetFileName(dir));
        }
    }
}
