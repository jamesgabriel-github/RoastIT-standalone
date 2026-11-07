using Microsoft.EntityFrameworkCore;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Services;
using RoastIT.Data.Repositories;

namespace RoastIT.Tests;

/// <summary>
/// Services over a real migrated SQLite file. The fake clock starts at Sat 2026-11-07 11:00 in Manila (03:00 UTC), so
/// "today" is 2026-11-07. The app is unlocked.
/// </summary>
public abstract class BookingsTestBase : IDisposable
{
    protected const string Pin = "4821";
    protected static readonly DateOnly Today = new(2026, 11, 7);

    protected readonly AppLockFixture Fx = new();
    protected readonly ChangeNotifier Changes = new();
    protected readonly AppLockService Lock;
    protected readonly BusinessCalendar Calendar;
    protected readonly EfBookingStore Store;
    protected readonly BookingService Bookings;
    protected readonly OrderService Orders;
    protected readonly DashboardService DashboardSvc;

    protected readonly List<string> Events = [];

    protected BookingsTestBase()
    {
        Lock = Fx.CreateService();
        Lock.SetUpAsync(Pin).GetAwaiter().GetResult(); // unlocked
        Calendar = new BusinessCalendar(Fx.Clock);
        Store = new EfBookingStore(Fx.Factory);
        var products = new EfProductStore(Fx.Factory);
        Bookings = new BookingService(Store, products, Lock, Changes, Fx.Clock, Calendar);
        Orders = new OrderService(Store, Lock, Changes, Fx.Clock, Calendar);
        DashboardSvc = new DashboardService(Store, Lock, Fx.Clock, Calendar);

        Changes.BookingsChanged += d => Events.Add($"bookings:{d:yyyy-MM-dd}");
        Changes.OrderChanged += k => Events.Add("order");
    }

    public void Dispose() => Fx.Dispose();

    protected async Task<Product> AddProductAsync(string name, long price, int cookingMin = 60, bool active = true)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = name, Price = price, CookingTimeMin = cookingMin, Active = active,
            CreatedAt = Fx.Clock.UtcNow,
        };
        await using var db = Fx.Factory.CreateDbContext();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    /// <summary>A business-local time as the UTC instant the form would send.</summary>
    protected DateTime Local(int year, int month, int day, int hour = 10, int minute = 0) =>
        Calendar.ToUtc(new DateTime(year, month, day, hour, minute, 0));

    /// <summary>Tomorrow (Nov 8) at 10:00 Manila.</summary>
    protected DateTime Tomorrow10 => Local(2026, 11, 8);

    /// <summary>Grace Villanueva, tomorrow 10:00, no discount, nothing paid.</summary>
    protected Task<CreatedBooking> CreateAsync(params (Product Product, int Qty)[] lines) =>
        CreateFullAsync("Grace Villanueva", null, 0, 0, lines);

    protected Task<CreatedBooking> CreateFullAsync(
        string? customer, DateTime? pickup, long discount, long paid, params (Product Product, int Qty)[] lines) =>
        Bookings.CreateAsync(new NewBookingInput(
            customer, null, pickup ?? Tomorrow10,
            lines.Select(l => new NewBookingLine(l.Product.Id, l.Qty)).ToList(), discount, paid));

    protected async Task<PinConfirmation> BookingsPinAsync()
    {
        var result = await Lock.ConfirmAsync(Pin, PinScope.Bookings);
        Assert.True(result.Attempt.Succeeded);
        return result.Confirmation!;
    }

    protected async Task<Order> LoadOrderAsync(Guid key)
    {
        await using var db = Fx.Factory.CreateDbContext();
        return await db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Payments).SingleAsync(o => o.Key == key);
    }

    protected async Task<List<AuditLog>> AuditAsync(string action)
    {
        await using var db = Fx.Factory.CreateDbContext();
        return await db.AuditLogs.AsNoTracking().Where(a => a.Action == action).ToListAsync();
    }

    protected async Task SetStatusAsync(string bookingId, ItemStatus status)
    {
        await using var db = Fx.Factory.CreateDbContext();
        var item = await db.Items.SingleAsync(i => i.BookingId == bookingId);
        item.Status = status;
        await db.SaveChangesAsync();
    }
}
