using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoastIT.Core.Entities;

namespace RoastIT.Data;

/// <summary>
/// Runs at app start, before the first page loads (plan §6.3): if migrations are pending on an
/// existing database, saves a copy first, then migrates and turns on WAL.
/// Kept free of MAUI types so it can be tested with a real SQLite file.
/// </summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<RoastItDbContext> dbFactory,
    string dbPath,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>How many pre-migration copies are kept.</summary>
    public const int BackupsToKeep = 3;

    public async Task InitializeAsync(string appVersion, string? previousVersion, CancellationToken ct = default)
    {
        // A force-close during an import swap leaves a marker: put the previous data back first (plan §10.5 step 8).
        DatabaseFiles.RecoverInterruptedImport(dbPath, logger);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (File.Exists(dbPath))
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                var backupPath = $"{dbPath}.bak-before-{appVersion}-from-{previousVersion ?? "unknown"}";
                logger.LogInformation("{Count} pending migration(s); saving a copy of the database before migrating", pending.Count);
                CopyDatabase(dbPath, backupPath);
                PruneBackups(dbPath, BackupsToKeep);
            }
        }

        await db.Database.MigrateAsync(ct);
        await StampMetaAsync(db, appVersion, ct);
        // PRAGMA journal_mode returns a row, so it can't go through ExecuteSqlRaw.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            await cmd.ExecuteScalarAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        logger.LogInformation("Database ready");
    }

    /// <summary>
    /// Records which app and schema wrote this database (import refuses newer schemas, plan §10.5).
    /// The schema version is the number of applied migrations, so it can't be forgotten.
    /// </summary>
    internal static async Task StampMetaAsync(RoastItDbContext db, string appVersion, CancellationToken ct)
    {
        var schemaVersion = (await db.Database.GetAppliedMigrationsAsync(ct)).Count();
        await UpsertAsync(db, AppMeta.SchemaVersionKey, schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        await UpsertAsync(db, AppMeta.AppVersionKey, appVersion, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task UpsertAsync(RoastItDbContext db, string key, string value, CancellationToken ct)
    {
        var row = await db.AppMeta.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) db.AppMeta.Add(new AppMeta { Key = key, Value = value });
        else row.Value = value;
    }

    /// <summary>
    /// Consistent copy through SQLite's backup API. A plain File.Copy of the main file would miss
    /// anything still sitting in the -wal file once WAL mode is on.
    /// </summary>
    internal static void CopyDatabase(string source, string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        using var src = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Pooling = false,
        }.ToString());
        src.Open();
        dst.Open();
        src.BackupDatabase(dst);
    }

    /// <summary>Keeps the newest <paramref name="keep"/> pre-migration copies; import safety copies are not touched.</summary>
    internal static void PruneBackups(string dbPath, int keep)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;
        var name = Path.GetFileName(dbPath);
        var stale = new DirectoryInfo(dir)
            .EnumerateFiles($"{name}.bak-before-*-from-*")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(keep)
            .ToList();
        foreach (var file in stale)
        {
            try { file.Delete(); } catch (IOException) { /* best effort */ }
        }
    }
}
