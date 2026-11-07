using System.Globalization;
using RoastIT.Core.Abstractions;

namespace RoastIT.Core.Rules;

/// <summary>Backup file names and the Settings texts around them (plan §10.5, settings spec §4).</summary>
public static class BackupFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string Extension = ".roastline";

    /// <summary>"roastline-backup-20261105-2232.roastline", from the business-local time of the export.</summary>
    public static string FileName(DateTime local) => $"roastline-backup-{local.ToString("yyyyMMdd-HHmm", Inv)}{Extension}";

    /// <summary>"Nov 5, 2026, 10:32 PM".</summary>
    public static string When(DateTime local) => local.ToString("MMM d, yyyy, h:mm tt", Inv);

    /// <summary>"14 orders · 20 bookings · 11 payments · 5 products".</summary>
    public static string Contents(BackupSummary s) =>
        $"{Count(s.Orders, "order")} · {Count(s.Bookings, "booking")} · {Count(s.Payments, "payment")} · {Count(s.Products, "product")}";

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n.ToString("N0", Inv)} {noun}s";
}

/// <summary>The "no recent backup" reminder above Export / Import (D14, settings spec §4.1).</summary>
public static class BackupReminder
{
    public static readonly TimeSpan Threshold = TimeSpan.FromDays(7);

    /// <summary>Shown when there has never been an export, or the last one is more than 7 days (exact time) ago.</summary>
    public static bool IsDue(DateTime? lastExportUtc, DateTime nowUtc) =>
        lastExportUtc is not { } last || nowUtc - last > Threshold;

    public static string Text(DateTime? lastExportUtc, DateTime nowUtc)
    {
        var lead = lastExportUtc is { } last
            ? $"Your last backup was {(int)Math.Floor((nowUtc - last).TotalDays)} days ago."
            : "No backup yet.";
        return $"{lead} All data lives only on this phone. If it's lost or the app is removed, an exported backup is the only way to get it back.";
    }
}
