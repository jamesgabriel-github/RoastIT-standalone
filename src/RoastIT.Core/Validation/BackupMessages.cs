namespace RoastIT.Core.Validation;

/// <summary>User-facing texts for Settings → Data (settings spec §4).</summary>
public static class BackupMessages
{
    public const string NotRoastline = "This file isn't a RoastIT backup. Choose a file that ends in .roastline.";

    public const string Unreadable = "Couldn't read that file. Try choosing it again.";

    public static string NewerVersion(string? appVersion) =>
        $"This backup was made by a newer version of RoastIT ({(string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion)}). Update the app first, then import it.";

    public const string ExportFailed = "Couldn't create the backup file. Try again.";

    public const string ImportFailed = "Couldn't import that backup. Your data wasn't changed.";

    public const string Imported = "Data imported. Your PIN stays the same. Opening Bookings…";

    public static string BackupSaved(string fileName) => $"Backup saved: {fileName}";

    /// <summary>The bold first sentence of the existing-data warning (D25).</summary>
    public static string ExistingBookingsLead(int count) =>
        count == 1 ? "This phone already has 1 booking." : $"This phone already has {count} bookings.";

    public static string ExistingBookingsRest(int count) =>
        count == 1
            ? "Importing replaces it with the bookings in this backup. Export a backup first if you want to keep it."
            : "Importing replaces them with the bookings in this backup. Export a backup first if you want to keep them.";
}
