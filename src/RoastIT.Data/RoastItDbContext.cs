using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Entities;

namespace RoastIT.Data;

/// <summary>
/// One short-lived context per operation (created through <c>IDbContextFactory</c>).
/// Entity sets are added phase by phase: app_lock/audit_logs/app_meta (Phase 1),
/// products (Phase 2), orders/items/payments/daily_counters (Phase 3).
/// </summary>
public class RoastItDbContext(DbContextOptions<RoastItDbContext> options) : DbContext(options)
{
    public DbSet<AppLockState> AppLock => Set<AppLockState>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppMeta> AppMeta => Set<AppMeta>();
    public DbSet<Product> Products => Set<Product>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RoastItDbContext).Assembly);
    }
}
