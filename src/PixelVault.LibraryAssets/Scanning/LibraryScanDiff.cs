namespace PixelVault.LibraryAssets.Scanning;

public sealed class LibraryScanDiff
{
    public string LibraryRoot { get; init; } = string.Empty;

    public DateTime ComputedUtc { get; init; }

    public IReadOnlyList<ScanDiffEntry> Entries { get; init; } = Array.Empty<ScanDiffEntry>();
}
