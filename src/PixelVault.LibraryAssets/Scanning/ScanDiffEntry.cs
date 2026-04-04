using PixelVault.LibraryAssets.Models;

namespace PixelVault.LibraryAssets.Scanning;

public sealed class ScanDiffEntry
{
    public ScanDiffKind Kind { get; init; }

    /// <summary>For <see cref="ScanDiffKind.Updated"/>, <see cref="ScanDiffKind.Missing"/>, <see cref="ScanDiffKind.ConfirmedDeleted"/>.</summary>
    public Guid? AssetId { get; init; }

    public string AbsolutePath { get; init; } = string.Empty;

    /// <summary>Fingerprint observed on disk (Added / Updated).</summary>
    public string? NewFingerprint { get; init; }

    /// <summary>Previous fingerprint from store (Updated).</summary>
    public string? PreviousFingerprint { get; init; }
}
