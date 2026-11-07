namespace RoastIT.App.Services;

/// <summary>
/// The only keys stored in MAUI Preferences (CLAUDE.md "Preferences keys").
/// Security data (PIN, recovery code, lockout) never goes here; it lives in SQLite <c>app_lock</c>.
/// </summary>
public static class PreferenceKeys
{
    public const string Theme = "theme";                   // light | dark | system
    public const string BookingColumns = "bookingColumns"; // JSON
    public const string BookingListView = "bookingListView"; // detailed | compact
    public const string LastExportAt = "lastExportAt";     // ISO timestamp
    public const string LastCsvAt = "lastCsvAt";           // ISO timestamp
    public const string CsvUri = "csvUri";                 // MediaStore URI of the CSV file
}
