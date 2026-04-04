namespace PixelVault.LibraryAssets.Models;

/// <summary>Canonical, persistent record for one file (or future asset) under a library root.</summary>
public sealed class LibraryAssetRecord
{
    public Guid AssetId { get; init; }

    /// <summary>Library root path (application should normalize, e.g. <c>Path.GetFullPath</c>).</summary>
    public string LibraryRoot { get; init; } = string.Empty;

    /// <summary>Absolute path to the file at last successful observation.</summary>
    public string AbsolutePath { get; init; } = string.Empty;

    /// <summary>Opaque content stamp (e.g. hash, size+mtime) used to detect <see cref="Scanning.ScanDiffKind.Updated"/>.</summary>
    public string? ContentFingerprint { get; init; }

    public AssetLifecycle Lifecycle { get; init; }

    public DateTime? LastSeenUtc { get; init; }

    /// <summary>Set when transitioning to <see cref="AssetLifecycle.Missing"/>.</summary>
    public DateTime? MissingSinceUtc { get; init; }
}
