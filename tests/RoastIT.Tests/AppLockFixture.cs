using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Services;
using RoastIT.Data;
using RoastIT.Data.Repositories;

namespace RoastIT.Tests;

public sealed class FakeClock(DateTime? start = null) : IClock
{
    public DateTime UtcNow { get; set; } = start ?? new DateTime(2026, 11, 7, 3, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A migrated temp SQLite database plus a way to build AppLockService instances over it ("app restarts").</summary>
public sealed class AppLockFixture : IDisposable
{
    private readonly TempDb _temp = new();

    public FakeClock Clock { get; } = new();
    public IDbContextFactory<RoastItDbContext> Factory { get; }

    public AppLockFixture()
    {
        Factory = _temp.CreateFactory();
        using var db = Factory.CreateDbContext();
        db.Database.Migrate();
    }

    /// <summary>A new service instance: locked, with empty in-memory windows, same database.</summary>
    public AppLockService CreateService() => new(new EfAppLockStore(Factory), Clock);

    public string DbPath => _temp.DbPath;

    public void Dispose() => _temp.Dispose();
}
