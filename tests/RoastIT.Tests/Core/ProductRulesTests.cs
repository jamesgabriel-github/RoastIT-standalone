using RoastIT.Core.Entities;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Tests.Core;

public class MoneyTests
{
    [Theory]
    [InlineData(0, "₱0")]
    [InlineData(220000, "₱2,200")]
    [InlineData(125050, "₱1,250.50")]
    [InlineData(5, "₱0.05")]
    [InlineData(100000000, "₱1,000,000")]
    [InlineData(-125050, "-₱1,250.50")]
    public void Format_shows_whole_pesos_without_decimals_and_centavos_with_two(long centavos, string expected) =>
        Assert.Equal(expected, Money.Format(centavos));

    [Theory]
    [InlineData("1200", 120000)]
    [InlineData(" 1250.5 ", 125050)]
    [InlineData("1250.50", 125050)]
    [InlineData("0", 0)]
    [InlineData("0.75", 75)]
    [InlineData(".5", 50)]
    public void Typed_pesos_become_centavos(string text, long expected)
    {
        Assert.True(Money.TryParsePesos(text, out var centavos));
        Assert.Equal(expected, centavos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1,200")]
    [InlineData("1e3")]
    [InlineData("12.345")]
    [InlineData("₱100")]
    [InlineData("99999999999999999999999999")]
    public void Anything_else_is_not_a_price(string? text) => Assert.False(Money.TryParsePesos(text, out _));

    [Theory]
    [InlineData(220000, "2200")]
    [InlineData(125050, "1250.50")]
    [InlineData(5, "0.05")]
    public void A_price_field_reads_back_what_it_was_filled_with(long centavos, string field)
    {
        Assert.Equal(field, Money.ToFieldText(centavos));
        Assert.True(Money.TryParsePesos(field, out var back));
        Assert.Equal(centavos, back);
    }

    [Theory]
    [InlineData(1, "1 min")]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 hr")]
    [InlineData(120, "2 hr")]
    [InlineData(150, "2 hr 30 min")]
    public void Cooking_time_reads_naturally(int minutes, string expected) =>
        Assert.Equal(expected, DurationFormat.Format(minutes));
}

public class ProductDraftTests
{
    private static ProductDraft Draft(string name = "Liempo", string price = "650", string time = "45", string desc = "", bool active = true) =>
        new(name, price, time, desc, active);

    [Fact]
    public void A_good_draft_has_no_error_and_converts_to_centavos()
    {
        var draft = Draft(name: "  Liempo ", price: "650.50", desc: "  Grilled. ");

        Assert.Null(draft.FirstError());
        var input = draft.ToInput();
        Assert.Equal("Liempo", input.Name);
        Assert.Equal(65050, input.Price);
        Assert.Equal(45, input.CookingTimeMin);
        Assert.Equal("Grilled.", input.Description);
    }

    [Theory]
    [InlineData("", "650", "45", ProductMessages.NameRequired)]
    [InlineData("  ", "650", "45", ProductMessages.NameRequired)]
    [InlineData("Liempo", "", "45", ProductMessages.PriceInvalid)]
    [InlineData("Liempo", "abc", "45", ProductMessages.PriceInvalid)]
    [InlineData("Liempo", "-1", "45", ProductMessages.PriceInvalid)]
    [InlineData("Liempo", "10.999", "45", ProductMessages.PriceInvalid)]
    [InlineData("Liempo", "650", "", ProductMessages.CookingTimeInvalid)]
    [InlineData("Liempo", "650", "0", ProductMessages.CookingTimeInvalid)]
    [InlineData("Liempo", "650", "1.5", ProductMessages.CookingTimeInvalid)]   // whole minutes only
    [InlineData("Liempo", "650", "-3", ProductMessages.CookingTimeInvalid)]
    [InlineData("Liempo", "650", "99999999999", ProductMessages.CookingTimeInvalid)]
    [InlineData("", "", "", ProductMessages.NameRequired)]                      // first failing rule wins
    [InlineData("Liempo", "", "", ProductMessages.PriceInvalid)]
    public void Only_the_first_failing_message_is_reported_in_form_order(string name, string price, string time, string message) =>
        Assert.Equal(message, Draft(name, price, time).FirstError());

    [Fact]
    public void Limits_have_their_own_messages()
    {
        Assert.Equal(ProductMessages.NameTooLong, Draft(name: new string('a', 61)).FirstError());
        Assert.Null(Draft(name: new string('a', 60)).FirstError());

        Assert.Equal(ProductMessages.PriceTooHigh, Draft(price: "10000000.01").FirstError());
        Assert.Null(Draft(price: "10000000").FirstError());

        Assert.Equal(ProductMessages.CookingTimeTooLong, Draft(time: "1441").FirstError());
        Assert.Null(Draft(time: "1440").FirstError());

        Assert.Equal(ProductMessages.DescriptionTooLong, Draft(desc: new string('d', 301)).FirstError());
        Assert.Null(Draft(desc: new string('d', 300)).FirstError());
    }
}

public class ProductListingTests
{
    private static int _n;

    private static Product P(string name, long price, int minutes, bool active = true, string? desc = null) => new()
    {
        Id = Guid.NewGuid(), Name = name, Price = price, CookingTimeMin = minutes, Active = active, Description = desc,
        CreatedAt = new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc).AddMinutes(_n++),
    };

    private static readonly Product[] Menu =
    [
        P("Lechon Head", 120000, 180, desc: "Whole roasted pig head"),
        P("Lechon Turkey", 450000, 150, desc: "Stuffed with herbs"),
        P("Lechon Chicken", 75000, 30),
        P("Lechon Belly", 220000, 120, desc: "Boneless rolled pork belly"),
        P("Liempo", 65000, 45, active: false, desc: "Grilled pork belly strips"),
    ];

    private static string[] Names(ProductListOptions o) => ProductListing.Apply(Menu, o).Select(p => p.Name).ToArray();

    [Fact]
    public void Counts_are_global()
    {
        var counts = ProductListing.Counts(Menu);

        Assert.Equal(new ProductCounts(5, 4, 1), counts);
    }

    [Fact]
    public void The_default_is_every_product_by_name_A_to_Z()
    {
        Assert.Equal(new[] { "Liempo", "Lechon Belly", "Lechon Chicken", "Lechon Head", "Lechon Turkey" }.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), Names(ProductListOptions.Default));
    }

    [Fact]
    public void Status_chips_filter_and_inactive_products_stay_under_Inactive()
    {
        Assert.Equal(["Liempo"], Names(ProductListOptions.Default with { Status = ProductStatusFilter.Inactive }));
        Assert.DoesNotContain("Liempo", Names(ProductListOptions.Default with { Status = ProductStatusFilter.Active }));
        Assert.Contains("Liempo", Names(ProductListOptions.Default));
    }

    [Theory]
    [InlineData("TURKEY", new[] { "Lechon Turkey" })]                       // name, any case
    [InlineData("  belly  ", new[] { "Lechon Belly", "Liempo" })]          // name or description, trimmed
    [InlineData("roasted", new[] { "Lechon Head" })]                        // description only
    [InlineData("nothing like this", new string[0])]
    public void Search_matches_name_or_description_case_insensitively(string search, string[] expected) =>
        Assert.Equal(expected.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), Names(ProductListOptions.Default with { Search = search }));

    [Fact]
    public void Search_does_not_change_the_global_counts()
    {
        _ = Names(ProductListOptions.Default with { Search = "turkey" });

        Assert.Equal(5, ProductListing.Counts(Menu).All);
    }

    [Fact]
    public void Price_and_cooking_time_sort_both_ways()
    {
        Assert.Equal(["Liempo", "Lechon Chicken", "Lechon Head", "Lechon Belly", "Lechon Turkey"],
            Names(ProductListOptions.Default with { Field = ProductSortField.Price }));
        Assert.Equal(["Lechon Turkey", "Lechon Belly", "Lechon Head", "Lechon Chicken", "Liempo"],
            Names(ProductListOptions.Default with { Field = ProductSortField.Price, Direction = SortDirection.Desc }));
        Assert.Equal(["Lechon Chicken", "Liempo", "Lechon Belly", "Lechon Turkey", "Lechon Head"],
            Names(ProductListOptions.Default with { Field = ProductSortField.CookingTime }));
        Assert.Equal(["Lechon Head", "Lechon Turkey", "Lechon Belly", "Liempo", "Lechon Chicken"],
            Names(ProductListOptions.Default with { Field = ProductSortField.CookingTime, Direction = SortDirection.Desc }));
    }

    [Fact]
    public void Ties_on_price_break_on_name()
    {
        var tie = new[] { P("Zebra", 100, 10), P("Apple", 100, 10), P("Mango", 100, 10) };

        var result = ProductListing.Apply(tie, ProductListOptions.Default with { Field = ProductSortField.Price, Direction = SortDirection.Desc });

        Assert.Equal(["Apple", "Mango", "Zebra"], result.Select(p => p.Name));
    }

    [Fact]
    public void Empty_state_texts_match_the_situation()
    {
        Assert.Equal("No products in the menu yet. Add your first one to get started.", ProductListing.EmptyMessage(0, ""));
        Assert.Equal("No products in the menu yet. Add your first one to get started.", ProductListing.EmptyMessage(0, "turkey"));
        Assert.Equal("No products match “turkey”.", ProductListing.EmptyMessage(5, "  turkey "));
        Assert.Equal("No products match this filter.", ProductListing.EmptyMessage(5, "   "));
    }

    [Fact]
    public void Labels_match_the_spec()
    {
        Assert.Equal("1 product", ProductListing.CountLabel(1));
        Assert.Equal("0 products", ProductListing.CountLabel(0));
        Assert.Equal("5 products", ProductListing.CountLabel(5));
        Assert.Equal("Name · A → Z", ProductListing.SortLabel(ProductSortField.Name, SortDirection.Asc));
        Assert.Equal("Name · Z → A", ProductListing.SortLabel(ProductSortField.Name, SortDirection.Desc));
        Assert.Equal("Price · Low to high", ProductListing.SortLabel(ProductSortField.Price, SortDirection.Asc));
        Assert.Equal("Price · High to low", ProductListing.SortLabel(ProductSortField.Price, SortDirection.Desc));
        Assert.Equal("Cooking time · Shortest first", ProductListing.SortLabel(ProductSortField.CookingTime, SortDirection.Asc));
        Assert.Equal("Cooking time · Longest first", ProductListing.SortLabel(ProductSortField.CookingTime, SortDirection.Desc));
    }
}
