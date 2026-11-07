using Android.App;
using Android.Content;
using Android.Provider;
using Microsoft.Extensions.Logging;
using RoastIT.App.Services;
using AndroidUri = Android.Net.Uri;

namespace RoastIT.App;

/// <summary>
/// "Save to a folder…" for Export (D33): <c>ACTION_CREATE_DOCUMENT</c>, the system "save as" screen. The owner picks the
/// folder and may rename the file; the app is handed a URI for that one new file and copies the backup into it. No storage
/// permission is needed and nothing is remembered afterwards. <see cref="MainActivity"/> forwards the screen's result here.
/// </summary>
public sealed class AndroidBackupFileSaver(ILogger<AndroidBackupFileSaver> logger) : IBackupFileSaver
{
    private const int RequestCode = 41207;
    private static TaskCompletionSource<AndroidUri?>? s_pending;

    public bool IsSupported => true;

    public async Task<string?> SaveAsync(string suggestedName, string localPath)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No foreground activity.");
        var pending = new TaskCompletionSource<AndroidUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref s_pending, pending, null) is not null)
            throw new InvalidOperationException("A save screen is already open.");

        try
        {
            var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("application/octet-stream");
            intent.PutExtra(Intent.ExtraTitle, suggestedName);
            activity.StartActivityForResult(intent, RequestCode);

            var uri = await pending.Task;
            if (uri is null) return null;

            var resolver = Platform.AppContext.ContentResolver ?? throw new IOException("No content resolver.");
            try
            {
                await Task.Run(() =>
                {
                    using var source = File.OpenRead(localPath);
                    using var target = resolver.OpenOutputStream(uri, "wt") ?? throw new IOException("Can't open the chosen file.");
                    source.CopyTo(target);
                    target.Flush();
                });
            }
            catch
            {
                TryDelete(resolver, uri); // don't leave an empty or half-written file behind
                throw;
            }
            return DisplayName(resolver, uri) ?? suggestedName;
        }
        finally
        {
            Interlocked.Exchange(ref s_pending, null);
        }
    }

    /// <summary>Called by <see cref="MainActivity"/> for every activity result; only ours is used.</summary>
    internal static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode) return;
        Volatile.Read(ref s_pending)?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
    }

    private string? DisplayName(ContentResolver resolver, AndroidUri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var column = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (column >= 0) return cursor.GetString(column);
            }
        }
        catch (Exception ex)
        {
            logger.LogInformation("Couldn't read the saved file's name ({Type})", ex.GetType().Name);
        }
        return null;
    }

    private void TryDelete(ContentResolver resolver, AndroidUri uri)
    {
        try
        {
            DocumentsContract.DeleteDocument(resolver, uri);
        }
        catch (Exception ex)
        {
            logger.LogInformation("Couldn't remove the unfinished backup file ({Type})", ex.GetType().Name);
        }
    }
}
