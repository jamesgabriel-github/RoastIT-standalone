using RoastIT.Core.Entities;

namespace RoastIT.Core.Abstractions;

/// <summary>Persistence for the single <c>app_lock</c> row. Implemented in RoastIT.Data.</summary>
public interface IAppLockStore
{
    /// <summary>Null on first run.</summary>
    Task<AppLockState?> GetAsync(CancellationToken ct = default);

    /// <summary>Inserts or replaces the row; writes the audit row (if any) in the same transaction.</summary>
    Task SaveAsync(AppLockState state, AuditLog? audit, CancellationToken ct = default);
}
