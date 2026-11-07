using System.Globalization;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Rules;
using Microsoft.Extensions.Logging;

namespace RoastIT.App.Services;

/// <summary>
/// Typed, failure-tolerant wrapper over MAUI <see cref="IPreferences"/>. A read that fails returns
/// the default, so a broken preference can never stop the app from starting.
/// </summary>
public sealed class PreferencesStore(IPreferences preferences, ILogger<PreferencesStore> logger)
{
    public string? GetString(string key)
    {
        try
        {
            var value = preferences.Get(key, string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read preference {Key}", key);
            return null;
        }
    }

    public void SetString(string key, string value)
    {
        try
        {
            preferences.Set(key, value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save preference {Key}", key);
        }
    }

    public void Remove(string key)
    {
        try
        {
            preferences.Remove(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove preference {Key}", key);
        }
    }

    public ThemeMode Theme
    {
        get => ThemeModes.Parse(GetString(PreferenceKeys.Theme));
        set => SetString(PreferenceKeys.Theme, value.ToKey());
    }

    /// <summary>"detailed" (default) or "compact" (bookings spec §6.1).</summary>
    public bool CompactBookingList
    {
        get => GetString(PreferenceKeys.BookingListView) == "compact";
        set => SetString(PreferenceKeys.BookingListView, value ? "compact" : "detailed");
    }

    /// <summary>Which fields the booking cards show; all on when nothing is saved. Edited in Settings → Columns to show.</summary>
    public BookingColumns BookingColumns
    {
        get => BookingColumns.Parse(GetString(PreferenceKeys.BookingColumns));
        set => SetString(PreferenceKeys.BookingColumns, value.ToJson());
    }

    /// <summary>When the last backup file was made (drives the 7-day reminder). Null = never.</summary>
    public DateTime? LastExportAt
    {
        get => GetUtc(PreferenceKeys.LastExportAt);
        set => SetUtc(PreferenceKeys.LastExportAt, value);
    }

    /// <summary>The last automatic CSV save: when, and how many bookings it held.</summary>
    public CsvSaveInfo? LastCsvSave
    {
        get
        {
            if (GetUtc(PreferenceKeys.LastCsvAt) is not { } at) return null;
            var rows = int.TryParse(GetString(PreferenceKeys.LastCsvCount), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
            return new CsvSaveInfo(at, rows);
        }
        set
        {
            SetUtc(PreferenceKeys.LastCsvAt, value?.SavedAtUtc);
            if (value is null) Remove(PreferenceKeys.LastCsvCount);
            else SetString(PreferenceKeys.LastCsvCount, value.Rows.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The MediaStore row of the CSV file, so later saves skip the lookup.</summary>
    public string? CsvUri
    {
        get => GetString(PreferenceKeys.CsvUri);
        set { if (value is null) Remove(PreferenceKeys.CsvUri); else SetString(PreferenceKeys.CsvUri, value); }
    }

    private DateTime? GetUtc(string key) =>
        DateTime.TryParse(GetString(key), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

    private void SetUtc(string key, DateTime? utc)
    {
        if (utc is { } at) SetString(key, at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        else Remove(key);
    }
}

/// <summary>The CSV service's record of its last save, kept in Preferences.</summary>
public sealed class PreferencesCsvBackupLog(PreferencesStore preferences) : ICsvBackupLog
{
    public CsvSaveInfo? LastSave => preferences.LastCsvSave;

    public void Record(CsvSaveInfo save) => preferences.LastCsvSave = save;
}
