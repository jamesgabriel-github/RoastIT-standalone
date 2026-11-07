using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Services;
using RoastIT.Core.Validation;
using RoastIT.Data;

namespace RoastIT.Tests.Data;

/// <summary>Settings → Export / Import (plan §10.5, D14–D17, D25) over real SQLite files.</summary>
public sealed class BackupTests : IDisposable
{
    private readonly TestDevice _a = new("4821");
    private readonly TestDevice _b = new("1111");
    private readonly string _out = Path.Combine(Path.GetTempPath(), "roastit-backups-" + Guid.NewGuid().ToString("N"));

    public BackupTests() => Directory.CreateDirectory(_out);

    public void Dispose()
    {
        _a.Dispose();
        _b.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_out, recursive: true); } catch (IOException) { }
    }

    private async Task<string> ExportAAsync()
    {
        var lechon = await _a.AddProductAsync("Lechon Belly");
        await _a.BookAsync(lechon, "Diego Santos", bookings: 2, paid: 100000);
        await _a.BookAsync(lechon, "Grace Villanueva");
        var exported = await _a.Backups.ExportAsync(await _a.DataPinAsync(), _out);
        return exported.Path;
    }

    // ---- Export ------------------------------------------------------------------

    [Fact]
    public async Task Export_names_the_file_by_business_time_and_contains_the_data()
    {
        var lechon = await _a.AddProductAsync("Lechon Belly");
        await _a.BookAsync(lechon, "Diego Santos", bookings: 2, paid: 100000);

        var exported = await _a.Backups.ExportAsync(await _a.DataPinAsync(), _out);

        Assert.Equal("roastline-backup-20261107-1100.roastline", exported.FileName); // 03:00 UTC = 11:00 Manila
        Assert.True(File.Exists(exported.Path));
        Assert.Equal("2", Sql.Scalar(exported.Path, "SELECT COUNT(*) FROM items;"));
        Assert.Equal("1", Sql.Scalar(exported.Path, "SELECT COUNT(*) FROM payments;"));
        Assert.Equal("1", Sql.Scalar(exported.Path, "SELECT COUNT(*) FROM products;"));
        Assert.Equal("2026-11-07T03:00:00.0000000Z", Sql.Scalar(exported.Path, "SELECT value FROM app_meta WHERE key = 'exported_at';"));
        Assert.Equal("delete", Sql.Scalar(exported.Path, "PRAGMA journal_mode;"));
        Assert.False(File.Exists(exported.Path + "-wal"));
    }

    [Fact]
    public async Task Export_strips_app_lock_and_leaves_no_hash_bytes_in_the_file()
    {
        var pinHash = _a.Scalar("SELECT pin_hash FROM app_lock;");
        var codeHash = _a.Scalar("SELECT recovery_code_hash FROM app_lock;");
        Assert.NotEmpty(pinHash);

        var path = await ExportAAsync();

        Assert.Equal("0", Sql.Scalar(path, "SELECT COUNT(*) FROM app_lock;"));
        SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(pinHash)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(codeHash)));
        // A shorter piece of the hash too, in case a page kept part of it.
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(pinHash[10..30])));

        // The live database is untouched apart from its audit row.
        Assert.Equal(pinHash, _a.Scalar("SELECT pin_hash FROM app_lock;"));
        Assert.Equal("1", _a.Scalar("SELECT COUNT(*) FROM audit_logs WHERE action = 'data.exported';"));
        Assert.Equal("0", Sql.Scalar(path, "SELECT COUNT(*) FROM audit_logs WHERE action = 'data.exported';"));
    }

    [Fact]
    public async Task Export_asks_for_a_fresh_data_pin_every_time()
    {
        await Assert.ThrowsAsync<PinRequiredException>(() => _a.Backups.ExportAsync(null, _out));

        var menu = (await _a.Lock.ConfirmAsync(_a.Pin, PinScope.Menu)).Confirmation;
        await Assert.ThrowsAsync<PinRequiredException>(() => _a.Backups.ExportAsync(menu, _out));

        await _a.Lock.ConfirmAsync(_a.Pin, PinScope.Bookings); // opens the 3-minute Bookings window
        await Assert.ThrowsAsync<PinRequiredException>(() => _a.Backups.ExportAsync(null, _out));

        var data = await _a.DataPinAsync();
        await _a.Backups.ExportAsync(data, _out);
        await Assert.ThrowsAsync<PinRequiredException>(() => _a.Backups.ExportAsync(data, _out)); // single-use

        var again = await _a.DataPinAsync();
        _a.Lock.Lock();
        await Assert.ThrowsAsync<AccessDeniedException>(() => _a.Backups.ExportAsync(again, _out));
    }

    [Fact]
    public async Task A_data_confirmation_expires_after_a_minute_and_never_opens_the_bookings_window()
    {
        var data = await _a.DataPinAsync();
        Assert.False(_a.Lock.HasRecentConfirmation(PinScope.Bookings));
        Assert.Throws<PinRequiredException>(() => _a.Lock.EnsureConfirmed(null, PinScope.Bookings));

        _a.Clock.Advance(TimeSpan.FromSeconds(61));
        await Assert.ThrowsAsync<PinRequiredException>(() => _a.Backups.ExportAsync(data, _out));

        // And a Data confirmation can't be spent on a Menu action.
        var other = await _a.DataPinAsync();
        Assert.Throws<PinRequiredException>(() => _a.Lock.EnsureConfirmed(other, PinScope.Menu));
    }

    // ---- Inspect -----------------------------------------------------------------

    [Fact]
    public async Task Inspecting_a_backup_shows_its_summary()
    {
        var path = await ExportAAsync();

        var result = await _b.Backups.InspectAsync(path);

        Assert.Equal(BackupFileStatus.Valid, result.Status);
        var s = result.Summary!;
        Assert.Equal(new DateTime(2026, 11, 7, 3, 0, 0, DateTimeKind.Utc), s.ExportedAtUtc);
        Assert.Equal(DateTimeKind.Utc, s.ExportedAtUtc!.Value.Kind);
        Assert.Equal(TestDevice.AppVersion, s.AppVersion);
        Assert.Equal(_b.BackupStore.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal((2, 3, 1, 1), (s.Orders, s.Bookings, s.Payments, s.Products));
    }

    [Fact]
    public async Task Inspecting_never_changes_the_file()
    {
        var path = await ExportAAsync();
        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(path);

        await _b.Backups.InspectAsync(path);

        SqliteConnection.ClearAllPools();
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task A_backup_still_in_wal_mode_can_be_inspected()
    {
        var path = await ExportAAsync();
        Sql.Exec(path, "PRAGMA journal_mode=WAL;");
        SqliteConnection.ClearAllPools();

        Assert.Equal(BackupFileStatus.Valid, (await _b.Backups.InspectAsync(path)).Status);
    }

    [Fact]
    public async Task Files_that_are_not_roastline_backups_are_refused()
    {
        var random = Path.Combine(_out, "random.roastline");
        await File.WriteAllBytesAsync(random, Enumerable.Range(0, 5000).Select(i => (byte)(i * 37)).ToArray());
        var text = Path.Combine(_out, "notes.roastline");
        await File.WriteAllTextAsync(text, "{\"app\":\"roastline\"}");
        var empty = Path.Combine(_out, "empty.roastline");
        await File.WriteAllBytesAsync(empty, []);

        var otherDb = Path.Combine(_out, "other.db");
        Sql.Exec(otherDb, "CREATE TABLE notes (id INTEGER PRIMARY KEY, body TEXT); INSERT INTO notes (body) VALUES ('hi');");

        var otherApp = await ExportAAsync();
        Sql.Exec(otherApp, "UPDATE app_meta SET value = 'someapp' WHERE key = 'app_id';");

        foreach (var file in new[] { random, text, empty, otherDb, otherApp })
        {
            var result = await _b.Backups.InspectAsync(file);
            Assert.Equal(BackupFileStatus.NotRoastline, result.Status);
            Assert.Equal(BackupMessages.NotRoastline, BackupService.ErrorFor(result));
        }
    }

    [Fact]
    public async Task A_corrupt_backup_is_refused()
    {
        var path = await ExportAAsync();
        SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(path);
        for (var i = 4096; i < bytes.Length; i++) bytes[i] = 0xA5; // every page after the first is garbage
        await File.WriteAllBytesAsync(path, bytes);

        var result = await _b.Backups.InspectAsync(path);

        Assert.Equal(BackupFileStatus.NotRoastline, result.Status);
    }

    [Fact]
    public async Task A_backup_from_a_newer_schema_is_refused_with_its_app_version()
    {
        var path = await ExportAAsync();
        Sql.Exec(path, "UPDATE app_meta SET value = '99' WHERE key = 'schema_version'; UPDATE app_meta SET value = '2.4.0' WHERE key = 'app_version';");

        var result = await _b.Backups.InspectAsync(path);

        Assert.Equal(BackupFileStatus.NewerSchema, result.Status);
        Assert.Equal(
            "This backup was made by a newer version of Roastline (2.4.0). Update the app first, then import it.",
            BackupService.ErrorFor(result));
    }

    [Fact]
    public async Task A_missing_file_is_unreadable()
    {
        var result = await _b.Backups.InspectAsync(Path.Combine(_out, "gone.roastline"));

        Assert.Equal(BackupFileStatus.Unreadable, result.Status);
        Assert.Equal(BackupMessages.Unreadable, BackupService.ErrorFor(result));
    }

    // ---- Import ------------------------------------------------------------------

    [Fact]
    public async Task Import_replaces_everything_and_this_device_keeps_its_own_pin()
    {
        var path = await ExportAAsync();
        var crispy = await _b.AddProductAsync("Crispy Pata", 95000, 90);
        await _b.BookAsync(crispy, "Walk-in on B");
        var bHash = _b.Scalar("SELECT pin_hash FROM app_lock;");
        var replaced = 0;
        _b.Changes.DataReplaced += () => replaced++;

        await _b.Backups.ImportAsync(path, "roastline-backup-20261107-1100.roastline", await _b.DataPinAsync());

        // A's data, none of B's.
        Assert.Equal("3", _b.Scalar("SELECT COUNT(*) FROM items;"));
        Assert.Equal("Lechon Belly", _b.Scalar("SELECT group_concat(name) FROM products;"));
        Assert.Equal("0", _b.Scalar("SELECT COUNT(*) FROM orders WHERE customer = 'Walk-in on B';"));
        // B's own lock row, so B's PIN opens it and A's doesn't.
        Assert.Equal(bHash, _b.Scalar("SELECT pin_hash FROM app_lock;"));
        var restarted = _b.Restart();
        Assert.False((await restarted.UnlockAsync("4821")).Succeeded);
        Assert.True((await restarted.UnlockAsync("1111")).Succeeded);
        // Bookkeeping.
        Assert.Equal(1, replaced);
        Assert.Equal("1", _b.Scalar("SELECT COUNT(*) FROM audit_logs WHERE action = 'data.imported';"));
        Assert.Contains("2026-11-07T03:00:00", _b.Scalar("SELECT details FROM audit_logs WHERE action = 'data.imported';"));
        Assert.Equal("0", _b.Scalar("SELECT COUNT(*) FROM app_meta WHERE key = 'exported_at';"));
        Assert.Equal("wal", _b.Scalar("PRAGMA journal_mode;"));
        Assert.False(File.Exists(DatabaseFiles.MarkerPath(_b.DbPath)));
        Assert.False(File.Exists(DatabaseFiles.StagingPath(_b.DbPath)));
        // The safety copy holds B's previous data.
        var safety = Assert.Single(Directory.GetFiles(_b.Dir, "*.bak-before-import-*"));
        Assert.Equal("1", Sql.Scalar(safety, "SELECT COUNT(*) FROM orders WHERE customer = 'Walk-in on B';"));
        // The app stays unlocked (the PIN didn't change) and the new data can be booked against.
        Assert.False(_b.Lock.IsLocked);
        var lechon = await _b.Bookings.ListAsync();
        Assert.Equal(3, lechon.Count);
    }

    [Fact]
    public async Task Import_numbering_continues_from_the_backups_counters()
    {
        var path = await ExportAAsync(); // A's three bookings are 261107-001 … 003 (numbered by the day they were made)

        await _b.Backups.ImportAsync(path, "a.roastline", await _b.DataPinAsync());
        var product = (await _b.Bookings.ListAsync())[0].ProductId;
        var created = await _b.Bookings.CreateAsync(new NewBookingInput(
            "Next", null, _b.Calendar.ToUtc(new DateTime(2026, 11, 8, 12, 0, 0)), [new NewBookingLine(product, 1000)]));

        Assert.Equal("261107-004", created.Units[0].BookingId);
    }

    [Fact]
    public async Task An_older_backup_that_still_has_an_app_lock_row_never_replaces_this_devices_pin()
    {
        await _a.AddProductAsync("Lechon Belly");
        // A raw copy of A's live database: it still carries A's lock row (as files made before the rule might).
        var raw = Path.Combine(_out, "raw.roastline");
        DatabaseInitializer.CopyDatabase(_a.DbPath, raw);
        Assert.Equal("1", Sql.Scalar(raw, "SELECT COUNT(*) FROM app_lock;"));

        await _b.Backups.ImportAsync(raw, "raw.roastline", await _b.DataPinAsync());

        var restarted = _b.Restart();
        Assert.False((await restarted.UnlockAsync("4821")).Succeeded);
        Assert.True((await restarted.UnlockAsync("1111")).Succeeded);
    }

    [Fact]
    public async Task An_older_schema_backup_is_migrated_on_import()
    {
        // A backup made by an app that only had the first two migrations (no bookings tables yet).
        var old = Path.Combine(_out, "old.roastline");
        var options = new DbContextOptionsBuilder<RoastItDbContext>().UseSqlite($"Data Source={old};Pooling=False").Options;
        await using (var db = new RoastItDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("AddProductsCatalog");
            db.AppMeta.Add(new AppMeta { Key = AppMeta.SchemaVersionKey, Value = "2" });
            db.AppMeta.Add(new AppMeta { Key = AppMeta.AppVersionKey, Value = "0.9.0" });
            db.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Liempo", Price = 65000, CookingTimeMin = 120, Active = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var inspection = await _b.Backups.InspectAsync(old);
        Assert.Equal(BackupFileStatus.Valid, inspection.Status);
        Assert.Equal((0, 0, 1), (inspection.Summary!.Orders, inspection.Summary.Bookings, inspection.Summary.Products));

        await _b.Backups.ImportAsync(old, "old.roastline", await _b.DataPinAsync());

        Assert.Equal("Liempo", _b.Scalar("SELECT name FROM products;"));
        Assert.Equal("0", _b.Scalar("SELECT COUNT(*) FROM orders;")); // the table exists now
        Assert.Equal(_b.BackupStore.CurrentSchemaVersion.ToString(), _b.Scalar("SELECT value FROM app_meta WHERE key = 'schema_version';"));
        Assert.Equal(TestDevice.AppVersion, _b.Scalar("SELECT value FROM app_meta WHERE key = 'app_version';"));
    }

    [Fact]
    public async Task Import_refuses_a_bad_file_and_touches_nothing()
    {
        var crispy = await _b.AddProductAsync("Crispy Pata");
        await _b.BookAsync(crispy, "Stays");
        var bad = Path.Combine(_out, "bad.roastline");
        await File.WriteAllTextAsync(bad, "not a database");

        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await _b.Backups.ImportAsync(bad, "bad.roastline", await _b.DataPinAsync()));

        Assert.Equal(BackupMessages.NotRoastline, ex.Message);
        Assert.Equal("1", _b.Scalar("SELECT COUNT(*) FROM orders WHERE customer = 'Stays';"));
        Assert.Empty(Directory.GetFiles(_b.Dir, "*.bak-before-import-*"));
    }

    [Fact]
    public async Task Import_asks_for_a_fresh_data_pin()
    {
        var path = await ExportAAsync();

        await Assert.ThrowsAsync<PinRequiredException>(() => _b.Backups.ImportAsync(path, "a.roastline", null));
        var menu = (await _b.Lock.ConfirmAsync(_b.Pin, PinScope.Menu)).Confirmation;
        await Assert.ThrowsAsync<PinRequiredException>(() => _b.Backups.ImportAsync(path, "a.roastline", menu));
        Assert.Equal("0", _b.Scalar("SELECT COUNT(*) FROM items;"));
    }

    [Fact]
    public async Task A_failed_swap_puts_the_previous_data_back()
    {
        var path = await ExportAAsync();
        var crispy = await _b.AddProductAsync("Crispy Pata");
        await _b.BookAsync(crispy, "Stays");
        _b.BackupStore.AfterSwapForTests = () => throw new IOException("disk full");
        var replaced = 0;
        _b.Changes.DataReplaced += () => replaced++;

        await Assert.ThrowsAsync<IOException>(async () => await _b.Backups.ImportAsync(path, "a.roastline", await _b.DataPinAsync()));

        Assert.Equal("1", _b.Scalar("SELECT COUNT(*) FROM orders WHERE customer = 'Stays';"));
        Assert.Equal("0", _b.Scalar("SELECT COUNT(*) FROM orders WHERE customer = 'Diego Santos';"));
        Assert.False(File.Exists(DatabaseFiles.MarkerPath(_b.DbPath)));
        Assert.False(File.Exists(DatabaseFiles.StagingPath(_b.DbPath)));
        Assert.Equal(0, replaced);
        Assert.True((await _b.Restart().UnlockAsync("1111")).Succeeded);
    }

    [Fact]
    public async Task A_force_close_during_the_swap_is_undone_at_the_next_start()
    {
        var path = await ExportAAsync();
        var crispy = await _b.AddProductAsync("Crispy Pata");
        await _b.BookAsync(crispy, "Stays");

        // Simulate the process dying mid-swap: the safety copy and marker are written, the live file is already replaced.
        var safety = DatabaseFiles.ImportCopyPath(_b.DbPath, _b.Clock.UtcNow);
        DatabaseInitializer.CopyDatabase(_b.DbPath, safety);
        await File.WriteAllTextAsync(DatabaseFiles.MarkerPath(_b.DbPath), Path.GetFileName(safety));
        SqliteConnection.ClearAllPools();
        DatabaseFiles.DeleteSidecars(_b.DbPath);
        File.Copy(path, _b.DbPath, overwrite: true);
        await File.WriteAllTextAsync(DatabaseFiles.StagingPath(_b.DbPath), "half-written");

        _b.Initialize(); // next app start

        Assert.Equal("1", _b.Scalar("SELECT COUNT(*) FROM orders WHERE customer = 'Stays';"));
        Assert.Equal("1", _b.Scalar("SELECT COUNT(*) FROM app_lock;"));
        Assert.False(File.Exists(DatabaseFiles.MarkerPath(_b.DbPath)));
        Assert.False(File.Exists(DatabaseFiles.StagingPath(_b.DbPath)));
        Assert.True((await _b.Restart().UnlockAsync("1111")).Succeeded);
    }

    [Fact]
    public async Task Counting_bookings_on_the_device_includes_cancelled_ones()
    {
        Assert.Equal(0, await _b.Backups.CountBookingsOnDeviceAsync());
        var crispy = await _b.AddProductAsync("Crispy Pata");
        await _b.BookAsync(crispy, "One", bookings: 2);
        Sql.Exec(_b.DbPath, "UPDATE items SET status = 4;");

        Assert.Equal(2, await _b.Backups.CountBookingsOnDeviceAsync());
    }

    [Fact]
    public void Only_the_newest_three_import_copies_are_kept()
    {
        for (var i = 1; i <= 5; i++)
            File.WriteAllText(DatabaseFiles.ImportCopyPath(_b.DbPath, new DateTime(2026, 11, i, 9, 0, 0, DateTimeKind.Utc)), "x");
        var migrationCopy = _b.DbPath + ".bak-before-1.0.1-from-1.0.0";
        File.WriteAllText(migrationCopy, "x");

        DatabaseFiles.PruneImportCopies(_b.DbPath, DatabaseFiles.ImportCopiesToKeep);

        var left = Directory.GetFiles(_b.Dir, "*.bak-before-import-*").Select(p => Path.GetFileName(p)!).Order().ToArray();
        Assert.Equal(
            ["roastit.db3.bak-before-import-20261103-090000", "roastit.db3.bak-before-import-20261104-090000", "roastit.db3.bak-before-import-20261105-090000"],
            left);
        Assert.True(File.Exists(migrationCopy));
    }
}
