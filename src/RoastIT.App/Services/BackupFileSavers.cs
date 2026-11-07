namespace RoastIT.App.Services;

/// <summary>
/// Lets the owner choose where a backup file is saved on the phone ("Save to a folder…", D33). On Android this is the
/// system "save as" screen (Storage Access Framework). The app only gets access to the one file the owner picks.
/// </summary>
public interface IBackupFileSaver
{
    /// <summary>False where there is no such screen (Windows for now): the Export sheet hides the button.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Opens the system "save as" screen suggesting <paramref name="suggestedName"/>, then copies the file at
    /// <paramref name="localPath"/> to the place the owner chose. Returns the saved file's name (the owner may have renamed
    /// it), or null when the owner backed out. Throws when the copy fails.
    /// </summary>
    Task<string?> SaveAsync(string suggestedName, string localPath);
}

/// <summary>Windows: no "save as" screen wired up yet; Export uses the share sheet only.</summary>
public sealed class UnsupportedBackupFileSaver : IBackupFileSaver
{
    public bool IsSupported => false;

    public Task<string?> SaveAsync(string suggestedName, string localPath) =>
        throw new PlatformNotSupportedException("Saving to a folder is Android only for now.");
}
