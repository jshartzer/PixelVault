namespace PixelVault.LibraryAssets.Health;

public sealed class LibraryRootHealthResult
{
    public bool IsHealthy { get; init; }

    /// <summary>Machine-oriented code when <see cref="IsHealthy"/> is false, e.g. <see cref="LibraryRootHealthFailureCode.Offline"/>.</summary>
    public string? FailureCode { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    /// <summary>Files observed during the health probe (may be capped); 0 if not enumerated.</summary>
    public int ObservedFileCount { get; init; }
}
