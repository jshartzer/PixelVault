namespace PixelVault.LibraryAssets.Models;

/// <summary>Lifecycle for a canonical library asset. <see cref="Missing"/> is a soft state (file not seen this scan / recently), not a hard delete.</summary>
public enum AssetLifecycle
{
    /// <summary>File is expected to be present; shown in UI as normal when on disk.</summary>
    Active = 0,

    /// <summary>Previously active; not observed on the last scan. Metadata is retained until confirmation or policy prune.</summary>
    Missing = 1,

    /// <summary>User or policy confirmed removal; may be purged from canonical store or archived.</summary>
    DeletedConfirmed = 2
}
