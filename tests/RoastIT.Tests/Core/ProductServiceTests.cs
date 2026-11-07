using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Exceptions;
using RoastIT.Core.Services;
using RoastIT.Core.Validation;
using RoastIT.Data.Repositories;

namespace RoastIT.Tests.Core;

public class ProductServiceTests : IDisposable
{
    private const string Pin = "4821";

    private readonly AppLockFixture _fx = new();
    private readonly ChangeNotifier _changes = new();
    private readonly AppLockService _lock;
    private readonly ProductService _svc;
    private int _catalogChanged;

    public ProductServiceTests()
    {
        _lock = _fx.CreateService();
        _lock.SetUpAsync(Pin).GetAwaiter().GetResult(); // unlocked
        _svc = new ProductService(new EfProductStore(_fx.Factory), _lock, _changes, _fx.Clock);
        _changes.CatalogChanged += () => _catalogChanged++;
    }

    public void Dispose() => _fx.Dispose();

    private static ProductInput Belly(bool active = true) =>
        new("Lechon Belly", 220000, 120, "Boneless rolled pork belly.", active);

    private async Task<PinConfirmation> MenuPinAsync()
    {
        var result = await _lock.ConfirmAsync(Pin, PinScope.Menu);
        Assert.True(result.Attempt.Succeeded);
        return result.Confirmation!;
    }

    private async Task<Product> AddBellyAsync(bool active = true) =>
        await _svc.CreateAsync(Belly(active), await MenuPinAsync());

    private async Task<List<AuditLog>> AuditAsync(string action)
    {
        await using var db = _fx.Factory.CreateDbContext();
        return await db.AuditLogs.AsNoTracking().Where(a => a.Action == action).ToListAsync();
    }

    private async Task<int> ProductCountAsync()
    {
        await using var db = _fx.Factory.CreateDbContext();
        return await db.Products.CountAsync();
    }

    // ---- PIN and lock ----------------------------------------------------------

    [Fact]
    public async Task Create_is_refused_without_a_PIN_confirmation_and_writes_nothing()
    {
        await Assert.ThrowsAsync<PinRequiredException>(() => _svc.CreateAsync(Belly(), null));

        Assert.Equal(0, await ProductCountAsync());
        Assert.Empty(await AuditAsync(AuditActions.ProductCreated));
        Assert.Equal(0, _catalogChanged);
    }

    [Fact]
    public async Task Update_and_SetActive_are_refused_without_a_PIN_confirmation()
    {
        var product = await AddBellyAsync();

        await Assert.ThrowsAsync<PinRequiredException>(() => _svc.UpdateAsync(product.Id, Belly() with { Price = 1 }, null));
        await Assert.ThrowsAsync<PinRequiredException>(() => _svc.SetActiveAsync(product.Id, false, null));

        var stored = Assert.Single(await _svc.ListAsync());
        Assert.Equal(220000, stored.Price);
        Assert.True(stored.Active);
    }

    [Fact]
    public async Task A_Menu_confirmation_is_single_use()
    {
        var confirmation = await MenuPinAsync();
        await _svc.CreateAsync(Belly(), confirmation);

        await Assert.ThrowsAsync<PinRequiredException>(() => _svc.CreateAsync(Belly() with { Name = "Liempo" }, confirmation));
        Assert.Equal(1, await ProductCountAsync());
    }

    [Fact]
    public async Task A_Menu_confirmation_expires_after_a_minute()
    {
        var confirmation = await MenuPinAsync();
        _fx.Clock.Advance(AppLockService.MenuConfirmationLifetime + TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<PinRequiredException>(() => _svc.CreateAsync(Belly(), confirmation));
        Assert.Equal(0, await ProductCountAsync());
    }

    [Fact]
    public async Task The_Bookings_window_never_authorises_a_Menu_action()
    {
        var bookings = await _lock.ConfirmAsync(Pin, PinScope.Bookings);
        Assert.True(_lock.HasRecentConfirmation(PinScope.Bookings));

        await Assert.ThrowsAsync<PinRequiredException>(() => _svc.CreateAsync(Belly(), null));
        await Assert.ThrowsAsync<PinRequiredException>(() => _svc.CreateAsync(Belly(), bookings.Confirmation));
        Assert.Equal(0, await ProductCountAsync());
    }

    [Fact]
    public async Task Writes_are_refused_while_the_app_is_locked()
    {
        var confirmation = await MenuPinAsync();
        _lock.Lock();

        await Assert.ThrowsAsync<AccessDeniedException>(() => _svc.CreateAsync(Belly(), confirmation));
        await Assert.ThrowsAsync<AccessDeniedException>(() => _svc.SetActiveAsync(Guid.NewGuid(), false, confirmation));
        Assert.Equal(0, await ProductCountAsync());
    }

    // ---- Validation -------------------------------------------------------------

    [Theory]
    [InlineData("", 100, 30, ProductMessages.NameRequired)]
    [InlineData("   ", 100, 30, ProductMessages.NameRequired)]
    [InlineData("Liempo", -1, 30, ProductMessages.PriceInvalid)]
    [InlineData("Liempo", 100, 0, ProductMessages.CookingTimeInvalid)]
    [InlineData("Liempo", 100, -5, ProductMessages.CookingTimeInvalid)]
    // Several wrong at once: only the first in form order is reported.
    [InlineData("", -1, 0, ProductMessages.NameRequired)]
    [InlineData("Liempo", -1, 0, ProductMessages.PriceInvalid)]
    public async Task The_service_rechecks_validation_and_reports_the_first_failing_message(string name, long price, int minutes, string message)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
            await _svc.CreateAsync(new ProductInput(name, price, minutes, null, true), await MenuPinAsync()));

        Assert.Equal(message, ex.Message);
        Assert.Equal(0, await ProductCountAsync());
        Assert.Equal(0, _catalogChanged);
    }

    [Fact]
    public async Task Update_validates_too()
    {
        var product = await AddBellyAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
            await _svc.UpdateAsync(product.Id, Belly() with { Name = " " }, await MenuPinAsync()));

        Assert.Equal(ProductMessages.NameRequired, ex.Message);
        Assert.Equal("Lechon Belly", (await _svc.ListAsync()).Single().Name);
    }

    [Fact]
    public async Task A_zero_peso_price_is_allowed_and_a_blank_description_is_stored_as_null()
    {
        var product = await _svc.CreateAsync(new ProductInput("  Sauce  ", 0, 1, "   ", true), await MenuPinAsync());

        var stored = (await _svc.ListAsync()).Single();
        Assert.Equal("Sauce", stored.Name);
        Assert.Equal(0, stored.Price);
        Assert.Null(stored.Description);
        Assert.Equal(product.Id, stored.Id);
    }

    // ---- Create / update / toggle ----------------------------------------------

    [Fact]
    public async Task Create_stores_centavos_stamps_created_at_and_writes_an_audit_row()
    {
        var created = await AddBellyAsync();

        var stored = (await _svc.ListAsync()).Single();
        Assert.Equal(220000, stored.Price);
        Assert.Equal(120, stored.CookingTimeMin);
        Assert.True(stored.Active);
        Assert.Equal(_fx.Clock.UtcNow, stored.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);

        var audit = Assert.Single(await AuditAsync(AuditActions.ProductCreated));
        Assert.Equal("product", audit.TargetType);
        Assert.Equal(created.Id.ToString(), audit.TargetId);
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.Equal("Lechon Belly", details.RootElement.GetProperty("name").GetString());
        Assert.Equal(220000, details.RootElement.GetProperty("price").GetInt64());
        Assert.Equal(1, _catalogChanged);
    }

    [Fact]
    public async Task Create_can_add_a_product_that_starts_inactive()
    {
        await AddBellyAsync(active: false);

        Assert.False((await _svc.ListAsync()).Single().Active);
    }

    [Fact]
    public async Task Duplicate_names_are_allowed()
    {
        await AddBellyAsync();
        await AddBellyAsync();

        Assert.Equal(2, await ProductCountAsync());
    }

    [Fact]
    public async Task Update_changes_the_fields_and_audits_what_changed()
    {
        var product = await AddBellyAsync();
        _catalogChanged = 0;

        var updated = await _svc.UpdateAsync(product.Id,
            new ProductInput("Lechon Belly XL", 250050, 150, null, true), await MenuPinAsync());

        Assert.Equal("Lechon Belly XL", updated.Name);
        var stored = (await _svc.ListAsync()).Single();
        Assert.Equal(250050, stored.Price);
        Assert.Equal(150, stored.CookingTimeMin);
        Assert.Null(stored.Description);
        Assert.Equal(product.CreatedAt, stored.CreatedAt);

        var audit = Assert.Single(await AuditAsync(AuditActions.ProductUpdated));
        using var details = JsonDocument.Parse(audit.Details!);
        var changed = details.RootElement.GetProperty("changed");
        Assert.Equal(220000, changed.GetProperty("price").GetProperty("before").GetInt64());
        Assert.Equal(250050, changed.GetProperty("price").GetProperty("after").GetInt64());
        Assert.False(changed.TryGetProperty("active", out _));
        Assert.Equal(1, _catalogChanged);
    }

    [Fact]
    public async Task Changing_the_switch_in_an_edit_changes_active_in_the_same_save_with_one_PIN()
    {
        var product = await AddBellyAsync();

        await _svc.UpdateAsync(product.Id, Belly(active: false), await MenuPinAsync());

        Assert.False((await _svc.ListAsync()).Single().Active);
        var audit = Assert.Single(await AuditAsync(AuditActions.ProductUpdated));
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.True(details.RootElement.GetProperty("changed").GetProperty("active").GetProperty("before").GetBoolean());
    }

    [Fact]
    public async Task Saving_without_any_change_writes_nothing_and_announces_nothing()
    {
        var product = await AddBellyAsync();
        _catalogChanged = 0;

        await _svc.UpdateAsync(product.Id, Belly(), await MenuPinAsync());

        Assert.Empty(await AuditAsync(AuditActions.ProductUpdated));
        Assert.Equal(0, _catalogChanged);
    }

    [Fact]
    public async Task Editing_a_product_that_does_not_exist_is_refused()
    {
        await Assert.ThrowsAsync<StaleStateException>(async () =>
            await _svc.UpdateAsync(Guid.NewGuid(), Belly(), await MenuPinAsync()));
    }

    [Fact]
    public async Task Deactivating_keeps_the_product_listed_as_inactive_and_audits_it()
    {
        var product = await AddBellyAsync();
        _catalogChanged = 0;

        var result = await _svc.SetActiveAsync(product.Id, false, await MenuPinAsync());

        Assert.False(result.Active);
        var stored = Assert.Single(await _svc.ListAsync()); // still there
        Assert.False(stored.Active);
        var audit = Assert.Single(await AuditAsync(AuditActions.ProductActiveChanged));
        using var details = JsonDocument.Parse(audit.Details!);
        Assert.True(details.RootElement.GetProperty("before").GetBoolean());
        Assert.False(details.RootElement.GetProperty("after").GetBoolean());
        Assert.Equal(1, _catalogChanged);
    }

    [Fact]
    public async Task Activating_again_makes_it_available()
    {
        var product = await AddBellyAsync(active: false);

        await _svc.SetActiveAsync(product.Id, true, await MenuPinAsync());

        Assert.True((await _svc.ListAsync()).Single().Active);
    }

    [Fact]
    public async Task Setting_the_state_it_already_has_does_nothing()
    {
        var product = await AddBellyAsync();
        _catalogChanged = 0;

        await _svc.SetActiveAsync(product.Id, true, await MenuPinAsync());

        Assert.Empty(await AuditAsync(AuditActions.ProductActiveChanged));
        Assert.Equal(0, _catalogChanged);
    }

    [Fact]
    public void There_is_no_way_to_delete_a_product()
    {
        var verbs = new[] { "Delete", "Remove", "Drop", "Purge" };

        foreach (var type in new[] { typeof(ProductService), typeof(IProductStore), typeof(EfProductStore) })
        {
            var offenders = type.GetMethods().Select(m => m.Name).Where(n => verbs.Any(v => n.Contains(v, StringComparison.OrdinalIgnoreCase)));
            Assert.Empty(offenders);
        }
    }

    [Fact]
    public async Task A_failing_change_listener_does_not_fail_the_save()
    {
        _changes.CatalogChanged += () => throw new InvalidOperationException("broken screen");

        var created = await AddBellyAsync();

        Assert.Equal(1, await ProductCountAsync());
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task The_database_itself_rejects_a_negative_price_or_a_zero_cooking_time()
    {
        await using var db = _fx.Factory.CreateDbContext();
        db.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Bad", Price = -1, CookingTimeMin = 30, CreatedAt = _fx.Clock.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var db2 = _fx.Factory.CreateDbContext();
        db2.Products.Add(new Product { Id = Guid.NewGuid(), Name = "Bad", Price = 0, CookingTimeMin = 0, CreatedAt = _fx.Clock.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }
}
