using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;

namespace RoastIT.Data.Repositories;

/// <summary>Stores <c>products</c>. One short-lived context per call; a product and its audit row save together. No delete.</summary>
public sealed class EfProductStore(IDbContextFactory<RoastItDbContext> dbFactory) : IProductStore
{
    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().ToListAsync(ct);
    }

    public async Task<Product?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task AddAsync(Product product, AuditLog audit, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Products.Add(product);
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync(ct); // one implicit transaction: product + audit together
    }

    public async Task UpdateAsync(Product product, AuditLog audit, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Products.Update(product);
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync(ct);
    }
}
