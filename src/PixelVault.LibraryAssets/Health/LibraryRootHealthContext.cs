namespace PixelVault.LibraryAssets.Health;

/// <summary>Optional context from the last known-good scan (for sanity checks).</summary>
public sealed class LibraryRootHealthContext
{
    /// <summary>Total media files enumerated in the last healthy scan, if known.</summary>
    public int? LastHealthyObservedFileCount { get; init; }
}
