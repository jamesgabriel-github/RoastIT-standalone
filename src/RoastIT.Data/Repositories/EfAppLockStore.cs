using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Repositories;

/// <summary>Stores the single <c>app_lock</c> row. One short-lived context per call.</summary>
public sealed class EfAppLockStore(IDbContextFactory<RoastItDbContext> dbFactory) : IAppLockStore
{
    public async Task<AppLockState?> GetAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AppLock.AsNoTracking().SingleOrDefaultAsync(x => x.Id == AppLockState.SingleRowId, ct);
    }

    public async Task SaveAsync(AppLockState state, AuditLog? audit, CancellationToken ct = default)
    {
        state.Id = AppLockState.SingleRowId;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.AppLock.AnyAsync(x => x.Id == AppLockState.SingleRowId, ct)) db.AppLock.Update(state);
        else db.AppLock.Add(state);
        if (audit is not null) db.AuditLogs.Add(audit);

        await db.SaveChangesAsync(ct); // one implicit transaction: row + audit together
    }
}
