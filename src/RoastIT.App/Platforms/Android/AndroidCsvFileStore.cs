using Android.Content;
using Android.Provider;
using Microsoft.Extensions.Logging;
using RoastIT.App.Services;
using RoastIT.Core.Abstractions;
using RoastIT.Core.Rules;
using AndroidUri = Android.Net.Uri;

namespace RoastIT.App;

/// <summary>
/// Writes <c>Documents/Roastline/roastline-bookings-48h.csv</c> through MediaStore (plan §10.6). Android 10+ needs no storage
/// permission for this. The row's URI is remembered in Preferences (<c>csvUri</c>); when it's gone (the owner deleted the
/// file) the app's own row is looked up by name and folder, or a new one is inserted. After a reinstall the old file
/// belongs to the old install, so Android creates "roastline-bookings-48h (1).csv" instead (a known limit).
/// </summary>
public sealed class AndroidCsvFileStore(PreferencesStore preferences, ExternalActivityGuard guard, ILogger<AndroidCsvFileStore> logger)
    : ICsvFileStore, ICsvFileShare
{
    private const string MimeType = "text/csv";
    private static readonly string RelativePath = BookingsCsv.Folder + "/";

    public bool IsSupported => OperatingSystem.IsAndroidVersionAtLeast(29);

    public bool CanShare => IsSupported && preferences.CsvUri is not null;

    public Task WriteAsync(string fileName, byte[] content, CancellationToken ct = default) =>
        Task.Run(() => Write(fileName, content), ct);

    private void Write(string fileName, byte[] content)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) throw new PlatformNotSupportedException("Needs Android 10 or later.");
        var resolver = Platform.AppContext.ContentResolver ?? throw new IOException("No content resolver.");

        if (preferences.CsvUri is { } saved && TryWrite(resolver, AndroidUri.Parse(saved)!, content)) return;

        var collection = MediaStore.Files.GetContentUri(MediaStore.VolumeExternalPrimary)
            ?? throw new IOException("No MediaStore collection.");
        var uri = Find(resolver, collection, fileName);
        if (uri is null || !TryWrite(resolver, uri, content))
        {
            uri = Insert(resolver, collection, fileName);
            if (!TryWrite(resolver, uri, content)) throw new IOException("MediaStore created the CSV file but it can't be written.");
        }
        preferences.CsvUri = uri.ToString();
    }

    private bool TryWrite(ContentResolver resolver, AndroidUri uri, byte[] content)
    {
        try
        {
            using var stream = resolver.OpenOutputStream(uri, "wt"); // "wt" truncates; plain "w" may not on every version
            if (stream is null) return false;
            stream.Write(content, 0, content.Length);
            stream.Flush();
            return true;
        }
        catch (Exception ex) when (ex is Java.IO.FileNotFoundException or Java.Lang.SecurityException or Java.Lang.IllegalArgumentException
                                       or Java.Lang.IllegalStateException or Java.Lang.UnsupportedOperationException)
        {
            logger.LogInformation("Remembered CSV row is not writable any more ({Type}); looking it up again", ex.GetType().Name);
            return false;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static AndroidUri? Find(ContentResolver resolver, AndroidUri collection, string fileName)
    {
        string[] projection = [IBaseColumns.Id];
        var selection = $"{MediaStore.IMediaColumns.RelativePath}=? AND {MediaStore.IMediaColumns.DisplayName}=?";
        using var cursor = resolver.Query(collection, projection, selection, [RelativePath, fileName], null);
        if (cursor is null || !cursor.MoveToFirst()) return null;
        return ContentUris.WithAppendedId(collection, cursor.GetLong(0));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static AndroidUri Insert(ContentResolver resolver, AndroidUri collection, string fileName)
    {
        using var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, MimeType);
        values.Put(MediaStore.IMediaColumns.RelativePath, BookingsCsv.Folder);
        return resolver.Insert(collection, values) ?? throw new IOException("MediaStore refused to create the CSV file.");
    }

    public Task<bool> ShareAsync()
    {
        if (preferences.CsvUri is not { } saved || Platform.CurrentActivity is not { } activity) return Task.FromResult(false);
        var uri = AndroidUri.Parse(saved)!;

        // Open it in a spreadsheet or viewer; the chooser lets the owner pick (or share it on).
        var view = new Intent(Intent.ActionView);
        view.SetDataAndType(uri, MimeType);
        view.AddFlags(ActivityFlags.GrantReadUriPermission);
        var chooser = Intent.CreateChooser(view, "Open bookings CSV")!;
        chooser.AddFlags(ActivityFlags.GrantReadUriPermission);

        guard.Expect();
        try
        {
            activity.StartActivity(chooser);
            return Task.FromResult(true);
        }
        catch (ActivityNotFoundException)
        {
            guard.Cancel();
            return Task.FromResult(false);
        }
    }
}
