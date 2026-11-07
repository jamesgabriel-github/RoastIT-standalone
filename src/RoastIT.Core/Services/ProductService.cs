using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Validation;

namespace RoastIT.Core.Services;

/// <summary>
/// The menu catalog (plan §7.7, menu spec §5–§8). Every write starts with <c>EnsureUnlocked</c>, then needs a Menu PIN
/// confirmation (single-use, asked every time), then re-checks the input. Products are never deleted, only deactivated.
/// <see cref="IChangeNotifier.CatalogChanged"/> is raised only after the save succeeded.
/// </summary>
public sealed class ProductService(IProductStore store, IAppLock appLock, IChangeNotifier changes, IClock clock)
{
    /// <summary>All products, active and inactive. Reading needs no PIN.</summary>
    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        return await store.ListAsync(ct);
    }

    public async Task<Product> CreateAsync(ProductInput input, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Menu);
        input = Validated(input);

        var now = clock.UtcNow;
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = input.Name,
            Price = input.Price,
            CookingTimeMin = input.CookingTimeMin,
            Description = input.Description,
            Active = input.Active,
            CreatedAt = now,
        };
        var audit = AuditLog.Create(AuditActions.ProductCreated, "product", product.Id.ToString(), now, new
        {
            name = product.Name,
            price = product.Price,
            cookingTimeMin = product.CookingTimeMin,
            active = product.Active,
        });

        await store.AddAsync(product, audit, ct);
        changes.NotifyCatalogChanged();
        return product;
    }

    /// <summary>
    /// Saves edits. If the "Available for booking" switch changed, the same save changes <c>active</c> (one PIN, not two).
    /// Nothing is written, audited or announced when no field actually changed.
    /// </summary>
    public async Task<Product> UpdateAsync(Guid id, ProductInput input, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Menu);
        input = Validated(input);

        var product = await GetOrThrowAsync(id, ct);
        var changed = new Dictionary<string, object>();
        void Track<T>(string field, T before, T after) where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(before, after)) changed[field] = new { before, after };
        }
        Track("name", product.Name, input.Name);
        Track("price", product.Price, input.Price);
        Track("cookingTimeMin", product.CookingTimeMin, input.CookingTimeMin);
        Track("description", product.Description ?? "", input.Description ?? "");
        Track("active", product.Active, input.Active);
        if (changed.Count == 0) return product;

        product.Name = input.Name;
        product.Price = input.Price;
        product.CookingTimeMin = input.CookingTimeMin;
        product.Description = input.Description;
        product.Active = input.Active;

        var audit = AuditLog.Create(AuditActions.ProductUpdated, "product", product.Id.ToString(), clock.UtcNow,
            new { name = product.Name, changed });

        await store.UpdateAsync(product, audit, ct);
        changes.NotifyCatalogChanged();
        return product;
    }

    /// <summary>Activate or deactivate from a card. Already in that state: nothing to do.</summary>
    public async Task<Product> SetActiveAsync(Guid id, bool active, PinConfirmation? confirmation, CancellationToken ct = default)
    {
        appLock.EnsureUnlocked();
        appLock.EnsureConfirmed(confirmation, PinScope.Menu);

        var product = await GetOrThrowAsync(id, ct);
        if (product.Active == active) return product;

        product.Active = active;
        var audit = AuditLog.Create(AuditActions.ProductActiveChanged, "product", product.Id.ToString(), clock.UtcNow,
            new { name = product.Name, before = !active, after = active });

        await store.UpdateAsync(product, audit, ct);
        changes.NotifyCatalogChanged();
        return product;
    }

    private async Task<Product> GetOrThrowAsync(Guid id, CancellationToken ct) =>
        await store.GetAsync(id, ct) ?? throw new StaleStateException("That product isn't on the menu any more.");

    /// <summary>Normalizes the input and throws the first failing message (menu spec §6.1).</summary>
    private static ProductInput Validated(ProductInput input)
    {
        input = input.Normalized();
        if (ProductInputValidator.FirstError(input) is { } message) throw new ValidationException(message);
        return input;
    }
}
