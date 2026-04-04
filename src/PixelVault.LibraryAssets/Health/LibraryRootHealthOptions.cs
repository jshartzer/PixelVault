namespace PixelVault.LibraryAssets.Health;

public sealed class LibraryRootHealthOptions
{
    /// <summary>When set, enumeration / probe must complete within this time.</summary>
    public TimeSpan? EnumerationTimeout { get; init; }

    /// <summary>
    /// If non-empty, each name must match a top-level child directory under the library root (case-insensitive on Windows).
    /// </summary>
    public IReadOnlyList<string>? ExpectedTopLevelFolderNames { get; init; }

    /// <summary>
    /// When <see cref="LibraryRootHealthContext.LastHealthyObservedFileCount"/> is set, observed count must be &gt;= this value.
    /// </summary>
    public int? MinimumAbsoluteFileCount { get; init; }

    /// <summary>
    /// When set with a known last count, observed count must be &gt;= <c>floor(last * MinimumFractionOfLastHealthyCount)</c>.
    /// Example: 0.25 means accept if at least 25% of prior scan.
    /// </summary>
    public double? MinimumFractionOfLastHealthyCount { get; init; }
}
