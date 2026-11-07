using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Repositories;

/// <summary>
/// Export and import of the whole database as a <c>.roastline</c> file (an ordinary SQLite file, plan §10.5).
/// The PIN never leaves the device: an export has no <c>app_lock</c> row and no leftover hash bytes, and an import keeps the
/// importing device's own row. Import works on a staged copy, so the live database is only touched by the final file swap,
/// and a marker file lets the next start undo a swap that was cut off (<see cref="DatabaseFiles.RecoverInterruptedImport"/>).
/// </summary>
public sealed class SqliteBackupStore : IBackupStore
{
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    private readonly IDbContextFactory<RoastItDbContext> _dbFactory;
    private readonly string _dbPath;
    private readonly string _appVersion;
    private readonly ILogger<SqliteBackupStore> _logger;
    private readonly Lazy<int> _schemaVersion;

    public SqliteBackupStore(IDbContextFactory<RoastItDbContext> dbFactory, string dbPath, string appVersion, ILogger<SqliteBackupStore> logger)
    {
        _dbFactory = dbFactory;
        _dbPath = dbPath;
        _appVersion = appVersion;
        _logger = logger;
        _schemaVersion = new Lazy<int>(() =>
        {
            using var db = dbFactory.CreateDbContext();
            return db.Database.GetMigrations().Count();
        });
    }

    public int CurrentSchemaVersion => _schemaVersion.Value;

    /// <summary>Test hook: runs right after the new file is moved into place, before it is checked. Throwing simulates a failed swap.</summary>
    internal Action? AfterSwapForTests { get; set; }

    // ---------------------------------------------------------------------------------
    // Export
    // ---------------------------------------------------------------------------------

    public async Task ExportAsync(string destinationPath, DateTime exportedAtUtc, AuditLog audit, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        DatabaseFiles.DeleteWithSidecars(destinationPath); // VACUUM INTO refuses an existing file

        // 1. A consistent copy, WAL content included (plan §10.5 step 2).
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var cmd = db.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = "VACUUM INTO $path;";
                var p = cmd.CreateParameter();
                p.ParameterName = "$path";
                p.Value = destinationPath;
                cmd.Parameters.Add(p);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        // 2. Strip the lock data from the throwaway copy and vacuum it, so no hash bytes are left in free pages (step 3).
        try
        {
            using var copy = Open(destinationPath, readOnly: false);
            Exec(copy, "PRAGMA journal_mode=DELETE;"); // one self-contained file, no -wal next to it
            Exec(copy, "PRAGMA secure_delete=ON;");
            Exec(copy, "DELETE FROM app_lock;");
            Exec(copy, "INSERT INTO app_meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                ("$k", AppMeta.ExportedAtKey), ("$v", exportedAtUtc.ToString("O", CultureInfo.InvariantCulture)));
            Exec(copy, "VACUUM;");
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            DatabaseFiles.DeleteWithSidecars(destinationPath);
            throw;
        }

        // 3. The audit row goes into the live database (the file is already made, so it isn't in the backup itself).
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            db.AuditLogs.Add(audit);
            await db.SaveChangesAsync(ct);
        }
    }

    // ---------------------------------------------------------------------------------
    // Inspect
    // ---------------------------------------------------------------------------------

    public Task<BackupInspection> InspectAsync(string path, CancellationToken ct = default) => Task.Run(() => Inspect(path), ct);

    private BackupInspection Inspect(string path)
    {
        try
        {
            if (!File.Exists(path)) return new BackupInspection(BackupFileStatus.Unreadable);
            if (!HasSqliteHeader(path)) return new BackupInspection(BackupFileStatus.NotRoastline);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BackupInspection(BackupFileStatus.Unreadable);
        }

        try
        {
            using var c = Open(path, readOnly: true);
            if (!string.Equals(Scalar(c, "PRAGMA integrity_check;"), "ok", StringComparison.Ordinal))
                return new BackupInspection(BackupFileStatus.NotRoastline);
            if (!TableExists(c, "app_meta")) return new BackupInspection(BackupFileStatus.NotRoastline);

            var meta = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM app_meta;";
                using var r = cmd.ExecuteReader();
                while (r.Read()) meta[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
            }
            if (meta.GetValueOrDefault(AppMeta.AppIdKey) != AppMeta.AppIdValue) return new BackupInspection(BackupFileStatus.NotRoastline);
            if (!int.TryParse(meta.GetValueOrDefault(AppMeta.SchemaVersionKey), NumberStyles.None, CultureInfo.InvariantCulture, out var schema))
                return new BackupInspection(BackupFileStatus.NotRoastline);

            DateTime? exportedAt = DateTime.TryParse(meta.GetValueOrDefault(AppMeta.ExportedAtKey), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at : null;

            var summary = new BackupSummary(
                exportedAt,
                meta.GetValueOrDefault(AppMeta.AppVersionKey),
                schema,
                CountRows(c, "orders"),
                CountRows(c, "items"),
                CountRows(c, "payments"),
                CountRows(c, "products"));

            return schema > CurrentSchemaVersion
                ? new BackupInspection(BackupFileStatus.NewerSchema, summary)
                : new BackupInspection(BackupFileStatus.Valid, summary);
        }
        catch (SqliteException ex)
        {
            _logger.LogInformation("Picked file is not a usable SQLite database (code {Code})", ex.SqliteErrorCode);
            return new BackupInspection(BackupFileStatus.NotRoastline);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BackupInspection(BackupFileStatus.Unreadable);
        }
    }

    public async Task<int> CountBookingsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Items.CountAsync(ct);
    }

    // ---------------------------------------------------------------------------------
    // Import
    // ---------------------------------------------------------------------------------

    public async Task ImportAsync(string path, AuditLog audit, CancellationToken ct = default)
    {
        var staging = DatabaseFiles.StagingPath(_dbPath);
        var marker = DatabaseFiles.MarkerPath(_dbPath);
        var safety = DatabaseFiles.ImportCopyPath(_dbPath, audit.CreatedAt);

        // 1. This device's own lock row (plan §10.5 step 6.1). The app is unlocked, so there is one.
        AppLockState? lockRow;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
            lockRow = await db.AppLock.AsNoTracking().SingleOrDefaultAsync(x => x.Id == AppLockState.SingleRowId, ct);

        // 2. Prepare the new data on a staged copy: migrate it, put this device's lock row in, add the audit row.
        //    Until the swap below, the live database is untouched.
        DatabaseFiles.DeleteWithSidecars(staging);
        File.Copy(path, staging);
        try
        {
            var options = new DbContextOptionsBuilder<RoastItDbContext>()
                .UseSqlite($"Data Source={staging};Foreign Keys=True;Pooling=False")
                .Options;
            await using (var sdb = new RoastItDbContext(options))
            {
                await sdb.Database.MigrateAsync(ct); // an older backup is upgraded (step 6.4)
                await sdb.AppLock.ExecuteDeleteAsync(ct); // older backups may still carry one (step 6.5)
                if (lockRow is not null) sdb.AppLock.Add(lockRow);
                await sdb.AppMeta.Where(m => m.Key == AppMeta.ExportedAtKey).ExecuteDeleteAsync(ct); // set on exported copies only
                sdb.AuditLogs.Add(audit);
                await sdb.SaveChangesAsync(ct);
                await DatabaseInitializer.StampMetaAsync(sdb, _appVersion, ct);
            }
            using (var check = Open(staging, readOnly: true))
            {
                if (!string.Equals(Scalar(check, "PRAGMA integrity_check;"), "ok", StringComparison.Ordinal))
                    throw new InvalidOperationException("The staged import failed its integrity check.");
            }
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            DatabaseFiles.DeleteWithSidecars(staging);
            throw;
        }

        // 3. Safety copy of the current data (step 6.3), and the marker that lets the next start undo a cut-off swap.
        try
        {
            DatabaseInitializer.CopyDatabase(_dbPath, safety);
            await File.WriteAllTextAsync(marker, Path.GetFileName(safety), CancellationToken.None);
        }
        catch
        {
            if (File.Exists(marker)) File.Delete(marker);
            DatabaseFiles.DeleteWithSidecars(staging);
            throw;
        }

        // 4. The swap. Any failure from here puts the safety copy back (step 8).
        try
        {
            SqliteConnection.ClearAllPools();
            DatabaseFiles.DeleteSidecars(_dbPath);
            File.Move(staging, _dbPath, overwrite: true);
            DatabaseFiles.DeleteSidecars(staging);
            AfterSwapForTests?.Invoke();

            await using (var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None))
            {
                await db.Database.OpenConnectionAsync(CancellationToken.None);
                try
                {
                    await using var cmd = db.Database.GetDbConnection().CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode=WAL;";
                    await cmd.ExecuteScalarAsync(CancellationToken.None);
                }
                finally
                {
                    await db.Database.CloseConnectionAsync();
                }
                if (!await db.AppMeta.AnyAsync(m => m.Key == AppMeta.AppIdKey, CancellationToken.None))
                    throw new InvalidOperationException("The imported database has no app_meta row.");
            }

            File.Delete(marker);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import swap failed; restoring the safety copy");
            DatabaseFiles.Restore(_dbPath, safety);
            if (File.Exists(marker)) File.Delete(marker);
            DatabaseFiles.DeleteWithSidecars(staging);
            throw;
        }

        DatabaseFiles.PruneImportCopies(_dbPath, DatabaseFiles.ImportCopiesToKeep);
    }

    // ---------------------------------------------------------------------------------

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        c.Open();
        return c;
    }

    private static bool HasSqliteHeader(string path)
    {
        using var f = File.OpenRead(path);
        if (f.Length < 100) return false;
        Span<byte> head = stackalloc byte[16];
        f.ReadExactly(head);
        return head.SequenceEqual(SqliteMagic);
    }

    private static bool TableExists(SqliteConnection c, string table) =>
        Convert.ToInt64(ScalarObject(c, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $t;", ("$t", table)), CultureInfo.InvariantCulture) > 0;

    private static int CountRows(SqliteConnection c, string table) =>
        TableExists(c, table) ? Convert.ToInt32(ScalarObject(c, $"SELECT COUNT(*) FROM \"{table}\";"), CultureInfo.InvariantCulture) : 0;

    private static string? Scalar(SqliteConnection c, string sql) => Convert.ToString(ScalarObject(c, sql), CultureInfo.InvariantCulture);

    private static object? ScalarObject(SqliteConnection c, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteScalar();
    }

    private static void Exec(SqliteConnection c, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
