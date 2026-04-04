namespace PixelVault.LibraryAssets.Scanning;

/// <summary>One file seen on disk during a scan (path + optional fingerprint).</summary>
public readonly struct DiskAssetObservation
{
    public DiskAssetObservation(string absolutePath, string? contentFingerprint)
    {
        AbsolutePath = absolutePath ?? string.Empty;
        ContentFingerprint = contentFingerprint;
    }

    public string AbsolutePath { get; }
    public string? ContentFingerprint { get; }
}
