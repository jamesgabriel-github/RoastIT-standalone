using System.Globalization;
using RoastIT.Core.Rules;

namespace RoastIT.Core.Validation;

/// <summary>What the Menu form submits, as numbers (menu spec §6, §7). Price is centavos.</summary>
public sealed record ProductInput(string Name, long Price, int CookingTimeMin, string? Description, bool Active)
{
    /// <summary>Name trimmed; a blank description becomes null (stored as NULL).</summary>
    public ProductInput Normalized() => this with
    {
        Name = (Name ?? "").Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
    };
}

/// <summary>
/// The Menu form's fields exactly as typed (strings). <see cref="ToInput"/> turns them into a <see cref="ProductInput"/>;
/// anything that can't be read becomes an out-of-range number, so the one <see cref="ProductInputValidator"/> reports it
/// with the right message and in the right order.
/// </summary>
public sealed record ProductDraft(string Name, string Price, string CookingTime, string Description, bool Active)
{
    public static ProductDraft Empty => new("", "", "", "", Active: true);

    public ProductInput ToInput()
    {
        // -1 and 0 are always rejected by the validator ("Price must be…", "Cooking time must be…").
        var price = Money.TryParsePesos(Price, out var centavos) ? centavos : -1;
        var cooking = int.TryParse(CookingTime?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ? minutes : 0;
        return new ProductInput(Name, price, cooking, Description, Active).Normalized();
    }

    /// <summary>The first failing message in form order, or null when the draft is valid.</summary>
    public string? FirstError() => ProductInputValidator.FirstError(ToInput());

    public ProductFailure? FirstFailure() => ProductInputValidator.FirstFailure(ToInput());
}
