using RoastIT.Core.Entities;

namespace RoastIT.Core.Abstractions;

/// <summary>Persistence for <c>products</c>. There is deliberately no delete (plan §7.7, D10).</summary>
public interface IProductStore
{
    /// <summary>Every product, active and inactive, in no particular order.</summary>
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct = default);

    Task<Product?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Inserts the product and its audit row in one transaction.</summary>
    Task AddAsync(Product product, AuditLog audit, CancellationToken ct = default);

    /// <summary>Updates an existing product and its audit row in one transaction.</summary>
    Task UpdateAsync(Product product, AuditLog audit, CancellationToken ct = default);
}
