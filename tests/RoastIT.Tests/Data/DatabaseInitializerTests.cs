using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using RoastIT.Data;

namespace RoastIT.Tests.Data;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task First_run_creates_the_database_in_wal_mode_without_a_backup_copy()
    {
        using var temp = new TempDb();
        var init = new DatabaseInitializer(temp.CreateFactory(), temp.DbPath, NullLogger<DatabaseInitializer>.Instance);

        await init.InitializeAsync("1.0.0", previousVersion: null);

        Assert.True(File.Exists(temp.DbPath));
        Assert.Equal("wal", ScalarText(temp.DbPath, "PRAGMA journal_mode;"));
        Assert.Empty(Directory.GetFiles(temp.Dir, "*.bak-*"));
    }

    [Fact]
    public async Task Starting_again_with_nothing_pending_makes_no_backup_copy()
    {
        using var temp = new TempDb();
        var init = new DatabaseInitializer(temp.CreateFactory(), temp.DbPath, NullLogger<DatabaseInitializer>.Instance);

        await init.InitializeAsync("1.0.0", null);
        await init.InitializeAsync("1.0.1", "1.0.0");

        Assert.Empty(Directory.GetFiles(temp.Dir, "*.bak-*"));
    }

    [Fact]
    public async Task First_run_applies_the_migration_and_stamps_app_meta()
    {
        using var temp = new TempDb();
        var init = new DatabaseInitializer(temp.CreateFactory(), temp.DbPath, NullLogger<DatabaseInitializer>.Instance);

        await init.InitializeAsync("1.2.3", previousVersion: null);

        Assert.Equal("roastline", ScalarText(temp.DbPath, "SELECT value FROM app_meta WHERE key = 'app_id';"));
        Assert.Equal("2", ScalarText(temp.DbPath, "SELECT value FROM app_meta WHERE key = 'schema_version';")); // migrations applied so far: InitialAppLock, AddProductsCatalog
        Assert.Equal("1.2.3", ScalarText(temp.DbPath, "SELECT value FROM app_meta WHERE key = 'app_version';"));
        Assert.Equal("0", ScalarText(temp.DbPath, "SELECT COUNT(*) FROM app_lock;")); // no row = first run
        Assert.Empty(Directory.GetFiles(temp.Dir, "*.bak-*"));                       // new database: nothing to back up
    }

    [Fact]
    public async Task An_existing_database_with_pending_migrations_is_copied_before_migrating()
    {
        using var temp = new TempDb();
        // An older install: the file exists and holds data, but none of today's migrations have run.
        using (var old = new SqliteConnection($"Data Source={temp.DbPath};Pooling=False"))
        {
            old.Open();
            Exec(old, "CREATE TABLE legacy (id INTEGER PRIMARY KEY, note TEXT); INSERT INTO legacy (note) VALUES ('kept');");
        }
        var init = new DatabaseInitializer(temp.CreateFactory(), temp.DbPath, NullLogger<DatabaseInitializer>.Instance);

        await init.InitializeAsync("1.1.0", previousVersion: "1.0.0");

        var backup = temp.DbPath + ".bak-before-1.1.0-from-1.0.0";
        Assert.True(File.Exists(backup));
        Assert.Equal("kept", ScalarText(backup, "SELECT note FROM legacy;"));
        Assert.Equal("0", ScalarText(backup, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'app_lock';")); // copy is pre-migration
        Assert.Equal("1", ScalarText(temp.DbPath, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'app_lock';"));
    }

    [Fact]
    public async Task The_app_lock_table_only_ever_holds_row_one()
    {
        using var temp = new TempDb();
        var init = new DatabaseInitializer(temp.CreateFactory(), temp.DbPath, NullLogger<DatabaseInitializer>.Instance);
        await init.InitializeAsync("1.0.0", null);

        using var c = new SqliteConnection($"Data Source={temp.DbPath};Pooling=False");
        c.Open();
        var ex = Assert.Throws<SqliteException>(() => Exec(c,
            "INSERT INTO app_lock (id, pin_hash, recovery_code_hash, failed_attempts, updated_at) VALUES (2, 'x', 'y', 0, '2026-01-01');"));
        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copy_includes_rows_that_are_still_in_the_wal_file()
    {
        using var temp = new TempDb();
        var copy = temp.DbPath + ".bak-before-1.0.1-from-1.0.0";

        // Keep this connection open so SQLite can't checkpoint the WAL into the main file.
        using var writer = new SqliteConnection($"Data Source={temp.DbPath};Pooling=False");
        writer.Open();
        Exec(writer, "PRAGMA journal_mode=WAL;");
        Exec(writer, "PRAGMA wal_autocheckpoint=0;");
        Exec(writer, "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT);");
        Exec(writer, "INSERT INTO t (name) VALUES ('Diego Santos');");
        Assert.True(File.Exists(temp.DbPath + "-wal") && new FileInfo(temp.DbPath + "-wal").Length > 0);

        DatabaseInitializer.CopyDatabase(temp.DbPath, copy);

        Assert.Equal("Diego Santos", ScalarText(copy, "SELECT name FROM t;"));
    }

    [Fact]
    public void Pruning_keeps_the_newest_three_pre_migration_copies_and_leaves_import_copies_alone()
    {
        using var temp = new TempDb();
        var start = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 5; i++)
        {
            var path = $"{temp.DbPath}.bak-before-1.0.{i}-from-1.0.{i - 1}";
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, start.AddDays(i));
        }
        var importCopy = $"{temp.DbPath}.bak-before-import-20261101-0900";
        File.WriteAllText(importCopy, "x");
        File.SetLastWriteTimeUtc(importCopy, start);

        DatabaseInitializer.PruneBackups(temp.DbPath, DatabaseInitializer.BackupsToKeep);

        var left = Directory.GetFiles(temp.Dir, "*.bak-before-*-from-*").Select(p => Path.GetFileName(p)!).Order().ToArray();
        Assert.Equal(
            ["roastit.db3.bak-before-1.0.3-from-1.0.2", "roastit.db3.bak-before-1.0.4-from-1.0.3", "roastit.db3.bak-before-1.0.5-from-1.0.4"],
            left);
        Assert.True(File.Exists(importCopy));
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string ScalarText(string dbPath, string sql)
    {
        using var c = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar())!;
    }
}
