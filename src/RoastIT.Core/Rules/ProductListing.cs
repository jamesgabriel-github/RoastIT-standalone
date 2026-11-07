using RoastIT.Core.Entities;

namespace RoastIT.Core.Rules;

public enum ProductStatusFilter { All, Active, Inactive }

public enum ProductSortField { Name, Price, CookingTime }

public enum SortDirection { Asc, Desc }

/// <summary>The Menu screen's search, filter and sort state (menu spec §2).</summary>
public sealed record ProductListOptions(
    string Search,
    ProductStatusFilter Status = ProductStatusFilter.All,
    ProductSortField Field = ProductSortField.Name,
    SortDirection Direction = SortDirection.Asc)
{
    public static ProductListOptions Default => new("");
}

/// <summary>Global counts for the status chips. They ignore the search (menu spec §3.2).</summary>
public sealed record ProductCounts(int All, int Active, int Inactive);

public static class ProductListing
{
    public static ProductCounts Counts(IReadOnlyCollection<Product> products)
    {
        var active = products.Count(p => p.Active);
        return new ProductCounts(products.Count, active, products.Count - active);
    }

    /// <summary>
    /// Filter by status, then search (name or description, case-insensitive substring), then sort.
    /// Ties on price or cooking time fall back to name A → Z, so the order never jumps between refreshes.
    /// </summary>
    public static List<Product> Apply(IEnumerable<Product> products, ProductListOptions options)
    {
        var q = options.Search.Trim();
        var filtered = products.Where(p =>
        {
            if (options.Status == ProductStatusFilter.Active && !p.Active) return false;
            if (options.Status == ProductStatusFilter.Inactive && p.Active) return false;
            if (q.Length == 0) return true;
            return p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        });

        var desc = options.Direction == SortDirection.Desc;
        var byName = StringComparer.OrdinalIgnoreCase;
        var sorted = options.Field switch
        {
            ProductSortField.Price => Order(filtered, p => p.Price, desc).ThenBy(p => p.Name, byName),
            ProductSortField.CookingTime => Order(filtered, p => p.CookingTimeMin, desc).ThenBy(p => p.Name, byName),
            _ => Order(filtered, p => p.Name, desc, byName),
        };
        // Identical names (allowed): oldest first.
        return sorted.ThenBy(p => p.CreatedAt).ThenBy(p => p.Id).ToList();
    }

    private static IOrderedEnumerable<Product> Order<TKey>(IEnumerable<Product> source, Func<Product, TKey> key, bool desc, IComparer<TKey>? comparer = null) =>
        desc ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);

    /// <summary>The empty-state text for the situation (menu spec §3.6).</summary>
    public static string EmptyMessage(int totalProducts, string search)
    {
        if (totalProducts == 0) return "No products in the menu yet. Add your first one to get started.";
        var q = search.Trim();
        return q.Length > 0 ? $"No products match “{q}”." : "No products match this filter.";
    }

    public static string CountLabel(int shown) => shown == 1 ? "1 product" : $"{shown} products";

    public static string FieldLabel(ProductSortField field) => field switch
    {
        ProductSortField.Price => "Price",
        ProductSortField.CookingTime => "Cooking time",
        _ => "Name",
    };

    public static string DirectionLabel(ProductSortField field, SortDirection direction) => (field, direction) switch
    {
        (ProductSortField.Price, SortDirection.Asc) => "Low to high",
        (ProductSortField.Price, _) => "High to low",
        (ProductSortField.CookingTime, SortDirection.Asc) => "Shortest first",
        (ProductSortField.CookingTime, _) => "Longest first",
        (_, SortDirection.Asc) => "A → Z",
        _ => "Z → A",
    };

    /// <summary>The Sort button text, e.g. "Name · A → Z".</summary>
    public static string SortLabel(ProductSortField field, SortDirection direction) =>
        $"{FieldLabel(field)} · {DirectionLabel(field, direction)}";
}
