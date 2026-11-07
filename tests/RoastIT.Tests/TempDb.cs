using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoastIT.Data;

namespace RoastIT.Tests;

/// <summary>A real SQLite file in a temp folder, deleted on dispose.</summary>
public sealed class TempDb : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "roastit-tests-" + Guid.NewGuid().ToString("N"));

    public TempDb() => Directory.CreateDirectory(_dir);

    public string DbPath => Path.Combine(_dir, "roastit.db3");
    public string Dir => _dir;

    public IDbContextFactory<RoastItDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<RoastItDbContext>()
            .UseSqlite($"Data Source={DbPath};Foreign Keys=True")
            .Options;
        return new PooledlessFactory(options);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* temp folder, best effort */ }
    }

    private sealed class PooledlessFactory(DbContextOptions<RoastItDbContext> options) : IDbContextFactory<RoastItDbContext>
    {
        public RoastItDbContext CreateDbContext() => new(options);
    }
}
