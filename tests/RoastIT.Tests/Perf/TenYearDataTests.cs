using System.Diagnostics;
using Microsoft.Data.Sqlite;
using RoastIT.Core.Entities;
using RoastIT.Core.Enums;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;
using RoastIT.Data;
using Xunit.Abstractions;

namespace RoastIT.Tests.Perf;

/// <summary>Runs only when the ROASTIT_PERF environment variable is set (it takes a while).</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROASTIT_PERF")))
            Skip = "Set ROASTIT_PERF=1 to run the ten-year data timing.";
    }
}

/// <summary>
/// D21: ~10 years of generated bookings, the main reads timed on this machine, and the same data exported as a
/// <c>.roastline</c> file (in <c>artifacts/perf/</c>) to import on the cheapest target phone and time the real screens.
/// <code>$env:ROASTIT_PERF = "1"; dotnet test tests\RoastIT.Tests --filter "FullyQualifiedName~TenYearData" --logger "console;verbosity=detailed"</code>
/// The generated names are test data only; nothing like this ships in the app.
/// </summary>
public sealed class TenYearDataTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestDevice _device = new();

    public void Dispose() => _device.Dispose();

    [PerfFact]
    public async Task Main_reads_over_ten_years_of_bookings()
    {
        var seeded = Stopwatch.StartNew();
        var (orders, items) = Seed(_device, years: 10, seed: 20261107);
        output.WriteLine($"Seeded {orders:N0} orders / {items:N0} bookings in {seeded.Elapsed.TotalSeconds:N1} s");

        var today = _device.Calendar.Today();
        var dashboard = new DashboardService(_device.BookingStore, _device.Lock, _device.Clock, _device.Calendar);

        async Task<T> Time<T>(string what, Func<Task<T>> run)
        {
            await run(); // warm up
            var sw = Stopwatch.StartNew();
            var result = await run();
            output.WriteLine($"{what,-40} {sw.Elapsed.TotalMilliseconds,8:N0} ms");
            return result;
        }

        var all = await Time("List: all dates (today ± 6 months)", () => _device.Bookings.ListAsync(null));
        await Time("Unscoped read of every booking", () => _device.BookingStore.ListRowsAsync());
        await Time("List: today", () => _device.Bookings.ListAsync(today));
        await Time("Status chip counts", () => _device.Bookings.StatusCountsAsync());
        await Time("Date strip / calendar counts", () => _device.Bookings.CountsByDateAsync());
        await Time("Dashboard: all dates", () => dashboard.GetAsync(null));
        await Time("Dashboard: today", () => dashboard.GetAsync(today));
        await Time("Filter + group all (in memory)", () => Task.FromResult(BookingListing.Apply(all, new BookingListOptions(), _device.Calendar)));
        await Time("Search all for \"santos\" (in memory)", () => Task.FromResult(BookingListing.Apply(all, new BookingListOptions(Search: "santos"), _device.Calendar)));
        await Time("CSV window rows", () => _device.BookingStore.ListRowsByPickupAsync(_device.Clock.UtcNow.AddHours(-24), _device.Clock.UtcNow.AddHours(24)));

        Assert.InRange(all.Count, 1, items - 1); // the window holds only about a year of the ten

        // The file for the phone test (import it on a test install: it replaces that phone's data).
        var dir = Path.Combine(RepoRoot(), "artifacts", "perf");
        Directory.CreateDirectory(dir);
        var exported = await _device.Backups.ExportAsync(await _device.DataPinAsync(), dir);
        var target = Path.Combine(dir, "roastline-10-years-sample.roastline");
        SqliteConnection.ClearAllPools();
        File.Move(exported.Path, target, overwrite: true);
        output.WriteLine($"Phone test file: {target} ({new FileInfo(target).Length / 1024 / 1024.0:N1} MB)");
    }

    /// <summary>
    /// Ten years up to the end of the schedulable window: 6–14 orders a day, 1–3 bookings each, past bookings mostly
    /// completed (5% of orders cancelled), most paid, some discounted. IDs and daily counters follow the app's rules.
    /// </summary>
    internal static (int Orders, int Items) Seed(TestDevice device, int years, int seed)
    {
        var rng = new Random(seed);
        var cal = device.Calendar;
        var today = cal.Today();
        var first = today.AddYears(-years);
        var last = ScheduleWindow.Max(today);
        string[] names = ["Diego Santos", "Grace Villanueva", "Ana Reyes", "Ben Cruz", "Carla Mendoza", "Paolo Garcia", "Liza Ramos", "Marco Tan", "Nina Lopez", "Oscar Dela Cruz"];

        var products = new List<Product>();
        foreach (var (name, price, minutes) in new[] { ("Lechon Belly", 440000L, 240), ("Whole Lechon", 1200000L, 360), ("Crispy Pata", 95000L, 90), ("Liempo", 65000L, 120), ("Lechon Manok", 38000L, 75), ("Lechon Kawali", 55000L, 60) })
            products.Add(new Product { Id = Guid.NewGuid(), Name = name, Price = price, CookingTimeMin = minutes, Active = true, CreatedAt = device.Clock.UtcNow });

        int orderCount = 0, itemCount = 0;
        using var db = device.Factory.CreateDbContext();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Products.AddRange(products);
        db.SaveChanges();

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var seq = 0;
            var ordersToday = rng.Next(6, 15);
            for (var o = 0; o < ordersToday; o++)
            {
                var product = products[rng.Next(products.Count)];
                var units = rng.Next(1, 4);
                var total = product.Price * units;
                var discount = rng.Next(10) == 0 ? total / 20 : 0;
                var pickup = cal.ToUtc(day.ToDateTime(new TimeOnly(rng.Next(8, 20), rng.Next(4) * 15)));
                var past = day < today;
                var cancelled = rng.Next(20) == 0;
                var paid = past && !cancelled ? total - discount : rng.Next(3) == 0 ? (total - discount) / 2 : 0;

                var order = new Order
                {
                    Key = Guid.NewGuid(),
                    Customer = rng.Next(8) == 0 ? null : names[rng.Next(names.Length)],
                    Phone = rng.Next(2) == 0 ? $"0917 {rng.Next(100, 999)} {rng.Next(1000, 9999)}" : null,
                    OrderDate = day,
                    PickupAt = pickup,
                    TotalAmount = total,
                    DiscountAmount = discount,
                    AmountPaid = paid,
                };
                for (var u = 0; u < units; u++)
                {
                    order.Items.Add(new Item
                    {
                        BookingId = BookingIdFormat.Format(day, ++seq),
                        OrderKey = order.Key,
                        ProductId = product.Id,
                        Status = cancelled ? ItemStatus.Cancelled : past ? ItemStatus.Completed : ItemStatus.Pending,
                        StartedCookingAt = past && !cancelled ? pickup.AddMinutes(-product.CookingTimeMin) : null,
                    });
                }
                if (paid > 0) order.Payments.Add(new Payment { Id = Guid.NewGuid(), OrderKey = order.Key, Amount = paid, RecordedAt = pickup.AddDays(-1) });
                db.Orders.Add(order);
                orderCount++;
                itemCount += units;
            }
            db.DailyCounters.Add(new DailyCounter { Date = day, LastSeq = seq });

            if (day.Day == 1)
            {
                db.SaveChanges();
                db.ChangeTracker.Clear();
            }
        }
        db.SaveChanges();
        return (orderCount, itemCount);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoastIT.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
