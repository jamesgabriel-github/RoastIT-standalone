using FluentValidation;

namespace RoastIT.Core.Validation;

/// <summary>User-facing texts (menu spec §6.1). The first three are the spec's; the limit messages follow the same style.</summary>
public static class ProductMessages
{
    public const string NameRequired = "Give the product a name.";
    public const string PriceInvalid = "Price must be a number of 0 or more.";
    public const string CookingTimeInvalid = "Cooking time must be at least 1 minute.";

    public static string NameTooLong => $"Name must be {ProductLimits.NameMaxLength} characters or fewer.";
    public static string PriceTooHigh => "Price can't be more than ₱10,000,000.";
    public static string CookingTimeTooLong => $"Cooking time can't be more than {ProductLimits.CookingTimeMaxMinutes:N0} minutes (24 hours).";
    public static string DescriptionTooLong => $"Description must be {ProductLimits.DescriptionMaxLength} characters or fewer.";
}

public static class ProductLimits
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 300;
    public const int CookingTimeMaxMinutes = 1440;
    public const long PriceMaxCentavos = 1_000_000_000; // ₱10,000,000.00
}

/// <summary>
/// The product rules, in the order the form reports them: name, price, cooking time, description. Callers show only the
/// first failing message. Expects a <see cref="ProductInput.Normalized"/> input.
/// </summary>
public sealed class ProductInputValidator : AbstractValidator<ProductInput>
{
    public static readonly ProductInputValidator Instance = new();

    public ProductInputValidator()
    {
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage(ProductMessages.NameRequired)
            .Must(n => n.Length <= ProductLimits.NameMaxLength).WithMessage(_ => ProductMessages.NameTooLong);

        RuleFor(x => x.Price).Cascade(CascadeMode.Stop)
            .Must(p => p >= 0).WithMessage(ProductMessages.PriceInvalid)
            .Must(p => p <= ProductLimits.PriceMaxCentavos).WithMessage(_ => ProductMessages.PriceTooHigh);

        RuleFor(x => x.CookingTimeMin).Cascade(CascadeMode.Stop)
            .Must(m => m >= 1).WithMessage(ProductMessages.CookingTimeInvalid)
            .Must(m => m <= ProductLimits.CookingTimeMaxMinutes).WithMessage(_ => ProductMessages.CookingTimeTooLong);

        RuleFor(x => x.Description)
            .Must(d => d is null || d.Length <= ProductLimits.DescriptionMaxLength).WithMessage(_ => ProductMessages.DescriptionTooLong);
    }

    /// <summary>The first failing message, or null when the input is valid.</summary>
    public static string? FirstError(ProductInput input) => FirstFailure(input)?.Message;

    /// <summary>The first failing rule with the field it belongs to (so the form can focus it), or null when valid.</summary>
    public static ProductFailure? FirstFailure(ProductInput input)
    {
        var error = Instance.Validate(input).Errors.FirstOrDefault();
        if (error is null) return null;
        var field = error.PropertyName switch
        {
            nameof(ProductInput.Price) => ProductField.Price,
            nameof(ProductInput.CookingTimeMin) => ProductField.CookingTime,
            nameof(ProductInput.Description) => ProductField.Description,
            _ => ProductField.Name,
        };
        return new ProductFailure(field, error.ErrorMessage);
    }
}

public enum ProductField { Name, Price, CookingTime, Description }

public sealed record ProductFailure(ProductField Field, string Message);
