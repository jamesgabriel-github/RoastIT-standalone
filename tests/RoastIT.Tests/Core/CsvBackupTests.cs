using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Enums;
using RoastIT.Core.Rules;
using RoastIT.Core.Services;

namespace RoastIT.Tests.Core;

/// <summary>The automatic CSV backup (plan §10.6, §12 "CSV backup").</summary>
public sealed class CsvBackupTests : BookingsTestBase
{
    private readonly FakeCsvFiles _files = new();
    private readonly FakeCsvLog _log = new();
    private readonly CsvBackupService _csv;

    public CsvBackupTests() =>
        _csv = new CsvBackupService(Store, _files, _log, Lock, Fx.Clock, Calendar, NullLogger<CsvBackupService>.Instance);

    private static BookingRow Row(string id, Guid order, DateTime pickup, string? customer = "Grace Villanueva", ItemStatus status = ItemStatus.Pending,
        long total = 440000, long discount = 0, long paid = 0, string product = "Lechon Belly", string? phone = null, string? notes = null,
        DateTime? started = null, int weightGrams = 1000) =>
        new(id, order, status, started, Guid.Empty, product, 240, customer, phone, pickup, total, discount, paid, notes, weightGrams);

    private string Text() => Encoding.UTF8.GetString(_files.Last!);

    // ---- The writer ------------------------------------------------------------------

    [Fact]
    public void The_file_starts_with_a_bom_and_the_header_and_uses_crlf()
    {
        var text = BookingsCsv.Build([], Calendar);

        Assert.StartsWith("﻿", text);
        Assert.Equal(
            "﻿Booking ID,Order,Customer,Phone,Product,Weight (kg),Status,Pickup date,Pickup time,Cooking time (min),Started cooking," +
            "Order total,Order discount,Order net total,Order paid,Order balance,Notes\r\n",
            text);
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public void A_row_has_local_times_per_order_money_with_two_decimals_and_the_orders_first_id()
    {
        var order = Guid.NewGuid();
        var pickup = Local(2026, 11, 8, 17, 30);
        var rows = BookingRows.WithOrderInfo(
        [
            Row("261107-002", order, pickup, total: 880000, discount: 50000, paid: 300050, phone: "0917 123 4567",
                status: ItemStatus.Cooking, started: Local(2026, 11, 8, 13, 5), weightGrams: 3200),
            Row("261107-001", order, pickup, total: 880000, discount: 50000, paid: 300050, phone: "0917 123 4567"),
        ]);

        var lines = BookingsCsv.Build(rows, Calendar).Split("\r\n");

        Assert.Equal("261107-001,261107-001,Grace Villanueva,0917 123 4567,Lechon Belly,1,Pending,2026-11-08,5:30 PM,240,,8800.00,500.00,8300.00,3000.50,5299.50,", lines[1]);
        Assert.Equal("261107-002,261107-001,Grace Villanueva,0917 123 4567,Lechon Belly,3.2,Cooking,2026-11-08,5:30 PM,240,2026-11-08 1:05 PM,8800.00,500.00,8300.00,3000.50,5299.50,", lines[2]);
        Assert.Equal("", lines[3]); // the file ends with CRLF
    }

    [Fact]
    public void Fields_with_commas_quotes_or_line_breaks_are_quoted_and_formulas_are_defused()
    {
        Assert.Equal("\"Santos, Diego\"", BookingsCsv.Cell("Santos, Diego"));
        Assert.Equal("\"The \"\"Garden\"\" Co\"", BookingsCsv.Cell("The \"Garden\" Co"));
        Assert.Equal("\"line one\nline two\"", BookingsCsv.Cell("line one\nline two"));
        Assert.Equal("\"a\r\nb\"", BookingsCsv.Cell("a\r\nb"));
        Assert.Equal("\"'=HYPERLINK(\"\"x\"\")\"", BookingsCsv.Cell("=HYPERLINK(\"x\")"));
        Assert.Equal("'+639171234567", BookingsCsv.Cell("+639171234567"));
        Assert.Equal("'-1", BookingsCsv.Cell("-1"));
        Assert.Equal("'@SUM(A1)", BookingsCsv.Cell("@SUM(A1)"));
        Assert.Equal("Peña", BookingsCsv.Cell("Peña"));
        Assert.Equal("", BookingsCsv.Cell(""));
    }

    [Fact]
    public void A_walk_in_shows_its_label_and_a_cancelled_order_shows_no_balance()
    {
        var order = Guid.NewGuid();
        var rows = BookingRows.WithOrderInfo([Row("261107-001", order, Local(2026, 11, 8), customer: null, status: ItemStatus.Cancelled, paid: 100000)]);

        var line = BookingsCsv.Build(rows, Calendar).Split("\r\n")[1];

        Assert.Equal("261107-001,261107-001,Walk-in customer,,Lechon Belly,1,Cancelled,2026-11-08,10:00 AM,240,,4400.00,0.00,4400.00,1000.00,0.00,", line);
    }

    [Fact]
    public void Rows_are_sorted_by_pickup_then_booking_id()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = BookingRows.WithOrderInfo(
        [
            Row("261107-003", a, Local(2026, 11, 8, 12)),
            Row("261107-010", b, Local(2026, 11, 8, 9)),
            Row("261107-002", a, Local(2026, 11, 8, 12)),
        ]);

        var ids = BookingsCsv.Build(rows, Calendar).Split("\r\n").Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')[0]).ToArray();

        Assert.Equal(["261107-010", "261107-002", "261107-003"], ids);
    }

    [Fact]
    public void Money_has_no_peso_sign_or_separators()
    {
        Assert.Equal("4400.00", BookingsCsv.Pesos(440000));
        Assert.Equal("1250.50", BookingsCsv.Pesos(125050));
        Assert.Equal("0.00", BookingsCsv.Pesos(0));
        Assert.Equal("1234567.89", BookingsCsv.Pesos(123456789));
    }

    // ---- The window ------------------------------------------------------------------

    [Fact]
    public async Task The_file_holds_pickups_exactly_24_hours_either_side_across_manila_midnight()
    {
        Fx.Clock.UtcNow = new DateTime(2026, 11, 7, 16, 30, 0, DateTimeKind.Utc); // Nov 8, 12:30 AM in Manila
        var now = Fx.Clock.UtcNow;
        var lechon = await AddProductAsync("Lechon Belly", 440000);
        await CreateFullAsync("Edge before", now.AddHours(-24), 0, 0, (lechon, 1));
        await CreateFullAsync("Just outside before", now.AddHours(-24).AddMinutes(-1), 0, 0, (lechon, 1));
        await CreateFullAsync("Edge after", now.AddHours(24), 0, 0, (lechon, 1));
        await CreateFullAsync("Just outside after", now.AddHours(24).AddMinutes(1), 0, 0, (lechon, 1));
        await CreateFullAsync("Now", now, 0, 0, (lechon, 1), (lechon, 2.5));

        Assert.True(await _csv.SaveAsync());

        var text = Text();
        Assert.Contains("Edge before", text);
        Assert.Contains("Edge after", text);
        Assert.DoesNotContain("Just outside", text);
        Assert.Equal(4, _log.LastSave!.Rows);
        Assert.Equal(now, _log.LastSave.SavedAtUtc);
        Assert.Equal(BookingsCsv.FileName, _files.LastName);
        // Nov 7, 12:30 AM … Nov 9, 12:30 AM local: the window crosses two Manila midnights.
        Assert.Equal("Pickups Nov 7, 12:30 AM – Nov 9, 12:30 AM", BookingsCsv.WindowLabel(now, Calendar));
    }

    // ---- When it saves ------------------------------------------------------------------

    [Fact]
    public async Task It_saves_when_never_saved_then_not_at_59_minutes_then_at_60()
    {
        Assert.True(await _csv.CheckAsync());
        Assert.Equal(1, _files.Writes);

        Fx.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(await _csv.CheckAsync());
        Assert.Equal(1, _files.Writes);

        Fx.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await _csv.CheckAsync());
        Assert.Equal(2, _files.Writes);
    }

    [Fact]
    public void A_last_save_in_the_future_counts_as_due()
    {
        var now = new DateTime(2026, 11, 7, 3, 0, 0, DateTimeKind.Utc);
        Assert.True(BookingsCsv.IsDue(now.AddHours(5), now));
        Assert.True(BookingsCsv.IsDue(null, now));
        Assert.False(BookingsCsv.IsDue(now.AddMinutes(-59), now));
        Assert.True(BookingsCsv.IsDue(now.AddMinutes(-60), now));
    }

    [Fact]
    public async Task A_booking_change_does_not_trigger_a_save()
    {
        await _csv.CheckAsync();
        var lechon = await AddProductAsync("Lechon Belly", 440000);

        var created = await CreateAsync((lechon, 1));
        await Bookings.AdvanceStatusAsync(created.Units[0].BookingId, ItemStatus.Pending);
        await Orders.RecordPaymentAsync(created.OrderKey, 1000, await BookingsPinAsync());

        Assert.Equal(1, _files.Writes);
        Assert.False(await _csv.CheckAsync()); // still within the hour
    }

    [Fact]
    public async Task A_failing_file_store_never_throws_and_never_fails_a_booking_save()
    {
        _files.Fail = true;
        var lechon = await AddProductAsync("Lechon Belly", 440000);
        var changed = 0;
        _csv.Changed += () => changed++;

        Assert.False(await _csv.SaveAsync());
        Assert.True(_csv.LastSaveFailed);
        Assert.Null(_log.LastSave);
        Assert.Equal(1, changed);

        var created = await CreateAsync((lechon, 1)); // unaffected
        Assert.Single(created.Units);

        _files.Fail = false;
        Assert.True(await _csv.CheckAsync()); // never saved, so still due; the retry works
        Assert.False(_csv.LastSaveFailed);
    }

    [Fact]
    public async Task Nothing_is_saved_while_locked_and_the_next_check_after_unlock_catches_up()
    {
        Lock.Lock();
        Assert.False(await _csv.CheckAsync());
        Assert.False(await _csv.SaveAsync());
        Assert.Equal(0, _files.Writes);

        await Lock.UnlockAsync(Pin);
        Assert.True(await _csv.CheckAsync());
    }

    [Fact]
    public async Task An_unsupported_platform_never_saves()
    {
        _files.Supported = false;

        Assert.False(_csv.IsSupported);
        Assert.False(await _csv.CheckAsync());
        Assert.Equal(0, _files.Writes);
    }

    private sealed class FakeCsvFiles : ICsvFileStore
    {
        public bool Supported { get; set; } = true;
        public bool Fail { get; set; }
        public int Writes { get; private set; }
        public byte[]? Last { get; private set; }
        public string? LastName { get; private set; }

        public bool IsSupported => Supported;

        public Task WriteAsync(string fileName, byte[] content, CancellationToken ct = default)
        {
            if (Fail) throw new IOException("MediaStore said no");
            Writes++;
            Last = content;
            LastName = fileName;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCsvLog : ICsvBackupLog
    {
        public CsvSaveInfo? LastSave { get; private set; }

        public void Record(CsvSaveInfo save) => LastSave = save;
    }
}
