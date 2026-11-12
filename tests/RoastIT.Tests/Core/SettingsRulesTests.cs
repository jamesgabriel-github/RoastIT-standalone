using RoastIT.Core.Abstractions;
using RoastIT.Core.Rules;
using RoastIT.Core.Validation;

namespace RoastIT.Tests.Core;

/// <summary>Settings texts and rules (settings spec §4, §7).</summary>
public class SettingsRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_backup_file_name_uses_business_local_time()
    {
        Assert.Equal("roastline-backup-20261105-2232.roastline", BackupFormat.FileName(new DateTime(2026, 11, 5, 22, 32, 0)));
        Assert.Equal("Nov 5, 2026, 10:32 PM", BackupFormat.When(new DateTime(2026, 11, 5, 22, 32, 0)));
    }

    [Fact]
    public void The_contents_line_counts_with_singulars()
    {
        Assert.Equal("14 orders · 20 bookings · 11 payments · 5 products", BackupFormat.Contents(new BackupSummary(null, null, 4, 14, 20, 11, 5)));
        Assert.Equal("1 order · 1 booking · 0 payments · 1 product", BackupFormat.Contents(new BackupSummary(null, null, 4, 1, 1, 0, 1)));
        Assert.Equal("1,200 orders · 2,000 bookings · 0 payments · 5 products", BackupFormat.Contents(new BackupSummary(null, null, 4, 1200, 2000, 0, 5)));
    }

    [Fact]
    public void The_backup_reminder_shows_after_more_than_seven_days_or_when_never_exported()
    {
        Assert.True(BackupReminder.IsDue(null, Now));
        Assert.False(BackupReminder.IsDue(Now.AddDays(-7), Now));
        Assert.True(BackupReminder.IsDue(Now.AddDays(-7).AddMinutes(-1), Now));
        Assert.False(BackupReminder.IsDue(Now.AddHours(-1), Now));

        Assert.Equal("No backup yet. All data lives only on this phone. If it's lost or the app is removed, an exported backup is the only way to get it back.",
            BackupReminder.Text(null, Now));
        Assert.StartsWith("Your last backup was 9 days ago. All data", BackupReminder.Text(Now.AddDays(-9.5), Now));
    }

    [Fact]
    public void The_existing_bookings_warning_has_a_singular_form()
    {
        Assert.Equal("This phone already has 1 booking.", BackupMessages.ExistingBookingsLead(1));
        Assert.Equal("Importing replaces it with the bookings in this backup. Export a backup first if you want to keep it.", BackupMessages.ExistingBookingsRest(1));
        Assert.Equal("This phone already has 20 bookings.", BackupMessages.ExistingBookingsLead(20));
        Assert.Equal("Importing replaces them with the bookings in this backup. Export a backup first if you want to keep them.", BackupMessages.ExistingBookingsRest(20));
    }

    [Fact]
    public void Column_switches_and_their_caption()
    {
        Assert.Equal(["Customer", "Product", "Cooking time", "Pickup", "Status", "Total amount", "Amount paid"], BookingColumnFields.All.Select(f => f.Label));
        var cols = BookingColumns.All;
        Assert.Equal("Every column is shown.", BookingColumnFields.HiddenCaption(cols));

        var status = BookingColumnFields.All.Single(f => f.Key == "status");
        cols = status.Set(cols, false);
        Assert.False(cols.Status);
        Assert.Equal("1 column hidden on the Bookings list.", BookingColumnFields.HiddenCaption(cols));

        cols = BookingColumnFields.All.Single(f => f.Key == "paid").Set(cols, false);
        Assert.Equal("2 columns hidden on the Bookings list.", BookingColumnFields.HiddenCaption(cols));
        Assert.Equal(cols, BookingColumns.Parse(cols.ToJson())); // what Settings saves is what Bookings reads
        Assert.Contains("\"status\":false", cols.ToJson());
    }
}
