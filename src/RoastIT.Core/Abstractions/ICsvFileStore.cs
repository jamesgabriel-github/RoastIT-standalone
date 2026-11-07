namespace RoastIT.Core.Abstractions;

/// <summary>
/// Where the automatic CSV backup is written (plan §10.6). Android: <c>Documents/Roastline/</c> through MediaStore, the
/// same file overwritten each time. Other platforms (and Android before 10) don't support it yet (D30).
/// </summary>
public interface ICsvFileStore
{
    bool IsSupported { get; }

    /// <summary>Creates the file, or truncates and rewrites it. Throws when the write fails.</summary>
    Task WriteAsync(string fileName, byte[] content, CancellationToken ct = default);
}

/// <summary>When the CSV was last saved and how many bookings it held (kept in Preferences: <c>lastCsvAt</c>, <c>lastCsvCount</c>).</summary>
public sealed record CsvSaveInfo(DateTime SavedAtUtc, int Rows);

public interface ICsvBackupLog
{
    CsvSaveInfo? LastSave { get; }

    void Record(CsvSaveInfo save);
}
