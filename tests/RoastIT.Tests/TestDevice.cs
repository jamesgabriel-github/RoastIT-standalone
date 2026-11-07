using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;
using RoastIT.Data;
using RoastIT.Data.Repositories;

namespace RoastIT.Tests;

/// <summary>
/// One "phone": its own database file (initialised like app start), its own PIN, and the services that export and import.
/// Two of these simulate moving a backup from one install to another.
/// </summary>
public sealed class TestDevice : IDisposable
{
    public const string AppVersion = "1.0.0";

    private readonly TempDb _temp = new();

    public FakeClock Clock { get; } = new();
    public IDbContextFactory<RoastItDbContext> Factory { get; }
    public string DbPath => _temp.DbPath;
    public string Dir => _temp.Dir;
    public string Pin { get; }
    public ChangeNotifier Changes { get; } = new();
    public AppLockService Lock { get; private set; }
    public BusinessCalendar Calendar { get; }
    public EfBookingStore BookingStore { get; }
    public BookingService Bookings { get; }
    public SqliteBackupStore BackupStore { get; }
    public BackupService Backups { get; }

    public TestDevice(string pin = "4821")
    {
        Pin = pin;
        Factory = _temp.CreateFactory();
        Initialize();
        Lock = new AppLockService(new EfAppLockStore(Factory), Clock);
        Lock.SetUpAsync(pin).GetAwaiter().GetResult(); // unlocked
        Calendar = new BusinessCalendar(Clock);
        BookingStore = new EfBookingStore(Factory);
        Bookings = new BookingService(BookingStore, new EfProductStore(Factory), Lock, Changes, Clock, Calendar);
        BackupStore = new SqliteBackupStore(Factory, DbPath, AppVersion, NullLogger<SqliteBackupStore>.Instance);
        Backups = new BackupService(BackupStore, Lock, Changes, Clock, Calendar, NullLogger<BackupService>.Instance);
    }

    /// <summary>What app start does: recover an interrupted import, migrate, stamp app_meta, WAL.</summary>
    public void Initialize() =>
        new DatabaseInitializer(Factory, DbPath, NullLogger<DatabaseInitializer>.Instance)
            .InitializeAsync(AppVersion, null).GetAwaiter().GetResult();

    /// <summary>A fresh AppLockService over the same file, as after an app restart (locked).</summary>
    public AppLockService Restart() => new(new EfAppLockStore(Factory), Clock);

    public async Task<PinConfirmation> DataPinAsync()
    {
        var result = await Lock.ConfirmAsync(Pin, PinScope.Data);
        Assert.True(result.Attempt.Succeeded);
        return result.Confirmation!;
    }

    public async Task<Product> AddProductAsync(string name, long price = 450000, int cookingMin = 240)
    {
        var product = new Product { Id = Guid.NewGuid(), Name = name, Price = price, CookingTimeMin = cookingMin, Active = true, CreatedAt = Clock.UtcNow };
        await using var db = Factory.CreateDbContext();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    /// <summary>One order for tomorrow 10:00 Manila with <paramref name="bookings"/> bookings of 1 kg each, and a payment.</summary>
    public Task<CreatedBooking> BookAsync(Product product, string customer, int bookings = 1, long paid = 0) =>
        Bookings.CreateAsync(new NewBookingInput(customer, null, Calendar.ToUtc(new DateTime(2026, 11, 8, 10, 0, 0)),
            Enumerable.Range(0, bookings).Select(_ => new NewBookingLine(product.Id, Weight.GramsPerKilo)).ToList(), 0, paid));

    public string Scalar(string sql) => Sql.Scalar(DbPath, sql);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }
}

/// <summary>Raw SQLite reads against a file, without pooling (so no handle stays open).</summary>
public static class Sql
{
    public static string Scalar(string dbPath, string sql)
    {
        using var c = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    public static void Exec(string dbPath, string sql)
    {
        using var c = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
