namespace PixelVault.LibraryAssets.Scanning;

public enum ScanDiffKind
{
    Added,
    Updated,
    Missing,

    /// <summary>Explicit removal — supplied by user action or retention policy, not inferred from “path absent once”.</summary>
    ConfirmedDeleted
}
