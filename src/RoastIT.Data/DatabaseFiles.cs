using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace RoastIT.Data;

/// <summary>
/// File-level helpers for the live database and the files that sit next to it: the WAL side files, the import staging
/// copy, the import safety copies and the marker that says an import swap is under way (plan §10.5 step 6).
/// </summary>
public static class DatabaseFiles
{
    /// <summary>How many import safety copies are kept (newest first). Pre-migration copies are pruned separately.</summary>
    public const int ImportCopiesToKeep = 3;

    public static string StagingPath(string dbPath) => dbPath + ".import-staging";

    /// <summary>Exists only while an import swap is running; holds the safety copy's file name.</summary>
    public static string MarkerPath(string dbPath) => dbPath + ".import-pending";

    /// <summary><c>roastit.db3.bak-before-import-20261107-031500</c> (UTC stamp).</summary>
    public static string ImportCopyPath(string dbPath, DateTime nowUtc) =>
        $"{dbPath}.bak-before-import-{nowUtc:yyyyMMdd-HHmmss}";

    public static void DeleteSidecars(string path)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            var side = path + suffix;
            if (File.Exists(side)) File.Delete(side);
        }
    }

    public static void DeleteWithSidecars(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        DeleteSidecars(path);
    }

    /// <summary>Puts a safety copy back as the live database. Pools are cleared first so no old handle writes to it.</summary>
    public static void Restore(string dbPath, string safetyCopyPath)
    {
        SqliteConnection.ClearAllPools();
        DeleteSidecars(dbPath);
        File.Copy(safetyCopyPath, dbPath, overwrite: true);
    }

    /// <summary>
    /// Runs at startup before anything opens the database. If the app died during an import swap (the marker is still
    /// there), the safety copy goes back in place, so the device is never left half-imported. A leftover staging file
    /// from an import that died earlier is just deleted: the live database was never touched then.
    /// </summary>
    public static void RecoverInterruptedImport(string dbPath, ILogger logger)
    {
        var marker = MarkerPath(dbPath);
        if (File.Exists(marker))
        {
            var name = File.ReadAllText(marker).Trim();
            var safety = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, name);
            if (name.Length > 0 && File.Exists(safety))
            {
                logger.LogWarning("An import was interrupted; restoring the safety copy");
                Restore(dbPath, safety);
            }
            else
            {
                logger.LogError("An import was interrupted but its safety copy is missing");
            }
            File.Delete(marker);
        }

        var staging = StagingPath(dbPath);
        if (File.Exists(staging)) DeleteWithSidecars(staging);
    }

    /// <summary>Keeps the newest <paramref name="keep"/> import safety copies.</summary>
    public static void PruneImportCopies(string dbPath, int keep)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;
        var name = Path.GetFileName(dbPath);
        var stale = new DirectoryInfo(dir)
            .EnumerateFiles($"{name}.bak-before-import-*")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal) // the UTC stamp sorts by time
            .Skip(keep)
            .ToList();
        foreach (var file in stale)
        {
            try { file.Delete(); } catch (IOException) { /* best effort */ }
        }
    }
}
