using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RoastIT.Core.Rules;
using RoastIT.Data;

namespace RoastIT.Tests.Core;

/// <summary>D34: a booking's quantity is a weight in kilos, kept as whole grams, priced per kilo.</summary>
public class WeightTests
{
    [Theory]
    [InlineData("3", 3000)]
    [InlineData("3.2", 3200)]
    [InlineData("3.25", 3250)]
    [InlineData(" 0.5 ", 500)]
    [InlineData(".5", 500)]
    [InlineData("0.01", 10)]
    [InlineData("500", 500000)]
    public void Kilos_typed_in_the_field_become_grams(string text, int grams)
    {
        Assert.True(Weight.TryParseKilos(text, out var parsed));
        Assert.Equal(grams, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("0.005")]     // below 0.01 kg
    [InlineData("3.205")]     // more than two decimals
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("1e2")]
    [InlineData("1,5")]
    [InlineData("1,000")]
    [InlineData("500.01")]
    [InlineData("3200")]
    public void Anything_else_is_not_a_weight(string? text)
    {
        Assert.False(Weight.TryParseKilos(text, out var grams));
        Assert.Equal(0, grams);
    }

    [Theory]
    [InlineData(3000, "3", "3 kg")]
    [InlineData(3200, "3.2", "3.2 kg")]
    [InlineData(3250, "3.25", "3.25 kg")]
    [InlineData(500, "0.5", "0.5 kg")]
    [InlineData(10, "0.01", "0.01 kg")]
    public void Weights_read_back_the_way_they_were_typed(int grams, string field, string label)
    {
        Assert.Equal(field, Weight.ToFieldText(grams));
        Assert.Equal(label, Weight.Format(grams));
        Assert.True(Weight.TryParseKilos(field, out var back));
        Assert.Equal(grams, back);
    }

    [Theory]
    [InlineData(0, 1, 500)]          // an empty field starts at half a kilo
    [InlineData(3200, 1, 3700)]
    [InlineData(3200, -1, 2700)]
    [InlineData(1000, -1, 500)]
    [InlineData(500, -1, 500)]       // − stops at one step
    [InlineData(300, -1, 300)]       // and never raises a smaller weight
    [InlineData(0, -1, 0)]
    [InlineData(500000, 1, 500000)]  // + stops at the maximum
    public void The_plus_and_minus_buttons_move_by_half_a_kilo(int from, int direction, int expected) =>
        Assert.Equal(expected, Weight.Step(from, direction));

    [Theory]
    [InlineData(220000, 3200, 704000)]     // ₱2,200/kg × 3.2 kg = ₱7,040
    [InlineData(220000, 1000, 220000)]
    [InlineData(220000, 500, 110000)]
    [InlineData(33333, 10, 333)]           // 333.33 centavos
    [InlineData(33333, 2550, 84999)]       // 84,999.15 centavos
    [InlineData(100, 5, 1)]                // 0.5 centavo rounds away from zero
    [InlineData(100, 4, 0)]
    [InlineData(0, 3200, 0)]
    public void A_line_costs_the_price_per_kilo_times_the_weight_rounded_to_the_centavo(long price, int grams, long expected) =>
        Assert.Equal(expected, OrderMath.LineTotal(price, grams));

    [Fact]
    public async Task Bookings_made_before_weights_existed_weigh_one_kilo_after_the_upgrade()
    {
        using var temp = new TempDb();
        await using (var db = temp.CreateFactory().CreateDbContext())
        {
            // The app as shipped before D34: every migration except AddItemWeight, with a booking in it.
            await db.GetService<IMigrator>().MigrateAsync("AddPerformanceIndexes");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO products (id, name, price, cooking_time_min, active, created_at) VALUES ('p1', 'Liempo', 65000, 120, 1, '2026-11-01 00:00:00');");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO orders (key, order_date, pickup_at, total_amount, discount_amount, amount_paid) VALUES ('o1', '2026-11-01', '2026-11-02 02:00:00', 130000, 0, 0);");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO items (booking_id, order_key, product_id, status) VALUES ('261101-001', 'o1', 'p1', 0), ('261101-002', 'o1', 'p1', 0);");
        }

        await using (var db = temp.CreateFactory().CreateDbContext())
            await db.Database.MigrateAsync();

        Assert.Equal("2", Sql.Scalar(temp.DbPath, "SELECT COUNT(*) FROM items WHERE weight_g = 1000;"));
    }
}
