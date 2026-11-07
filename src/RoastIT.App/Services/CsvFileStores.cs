using RoastIT.Core.Abstractions;

namespace RoastIT.App.Services;

/// <summary>Opens the saved CSV file in another app (the Settings card's Share/Open button).</summary>
public interface ICsvFileShare
{
    /// <summary>True once a file has been written on this install.</summary>
    bool CanShare { get; }

    /// <summary>Opens the system chooser for the file. Returns false when there's nothing to open.</summary>
    Task<bool> ShareAsync();
}

/// <summary>Windows (and Android before 10): no automatic CSV yet (D30).</summary>
public sealed class UnsupportedCsvFileStore : ICsvFileStore, ICsvFileShare
{
    public bool IsSupported => false;

    public bool CanShare => false;

    public Task WriteAsync(string fileName, byte[] content, CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("The automatic CSV backup is Android only.");

    public Task<bool> ShareAsync() => Task.FromResult(false);
}
