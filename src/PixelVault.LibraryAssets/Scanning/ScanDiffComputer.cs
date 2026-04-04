using PixelVault.LibraryAssets.Models;

namespace PixelVault.LibraryAssets.Scanning;

/// <summary>Builds a <see cref="LibraryScanDiff"/> from disk observations vs canonical <see cref="LibraryAssetRecord"/>s.</summary>
public static class ScanDiffComputer
{
    static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Computes Added, Updated, and Missing edges.
    /// Does not emit <see cref="ScanDiffKind.ConfirmedDeleted"/> — that is explicit elsewhere.
    /// Only <see cref="AssetLifecycle.Active"/> assets participate as “expected on disk”.
    /// <see cref="AssetLifecycle.Missing"/> assets that reappear are reported as <see cref="ScanDiffKind.Updated"/> (reactivation).
    /// </summary>
    public static LibraryScanDiff Compute(string libraryRoot, IEnumerable<DiskAssetObservation> diskObservations, IEnumerable<LibraryAssetRecord> canonicalAssets)
    {
        var root = string.IsNullOrWhiteSpace(libraryRoot) ? string.Empty : Path.GetFullPath(libraryRoot.Trim());
        var diskMap = new Dictionary<string, string?>(PathComparer);
        foreach (var obs in diskObservations ?? Enumerable.Empty<DiskAssetObservation>())
        {
            if (string.IsNullOrWhiteSpace(obs.AbsolutePath)) continue;
            var full = Path.GetFullPath(obs.AbsolutePath.Trim());
            diskMap[full] = obs.ContentFingerprint;
        }

        var entries = new List<ScanDiffEntry>();
        var assetByPath = new Dictionary<string, LibraryAssetRecord>(PathComparer);
        foreach (var asset in canonicalAssets ?? Enumerable.Empty<LibraryAssetRecord>())
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.AbsolutePath)) continue;
            if (!PathComparer.Equals(NormalizeRoot(asset.LibraryRoot), root)) continue;
            var path = Path.GetFullPath(asset.AbsolutePath.Trim());
            if (asset.Lifecycle == AssetLifecycle.DeletedConfirmed) continue;

            if (asset.Lifecycle == AssetLifecycle.Active)
            {
                assetByPath[path] = asset;
            }
            else if (asset.Lifecycle == AssetLifecycle.Missing)
            {
                // Allow one Missing row per path; last wins.
                assetByPath[path] = asset;
            }
        }

        foreach (var kv in diskMap)
        {
            var path = kv.Key;
            var fp = kv.Value;
            if (!assetByPath.TryGetValue(path, out var asset))
            {
                entries.Add(new ScanDiffEntry { Kind = ScanDiffKind.Added, AbsolutePath = path, NewFingerprint = fp });
                continue;
            }

            if (asset.Lifecycle == AssetLifecycle.Missing)
            {
                entries.Add(new ScanDiffEntry
                {
                    Kind = ScanDiffKind.Updated,
                    AssetId = asset.AssetId,
                    AbsolutePath = path,
                    NewFingerprint = fp,
                    PreviousFingerprint = asset.ContentFingerprint
                });
                continue;
            }

            if (!string.Equals(asset.ContentFingerprint ?? string.Empty, fp ?? string.Empty, StringComparison.Ordinal))
            {
                entries.Add(new ScanDiffEntry
                {
                    Kind = ScanDiffKind.Updated,
                    AssetId = asset.AssetId,
                    AbsolutePath = path,
                    NewFingerprint = fp,
                    PreviousFingerprint = asset.ContentFingerprint
                });
            }
        }

        foreach (var kv in assetByPath)
        {
            var path = kv.Key;
            var asset = kv.Value;
            if (asset.Lifecycle != AssetLifecycle.Active) continue;
            if (diskMap.ContainsKey(path)) continue;
            entries.Add(new ScanDiffEntry
            {
                Kind = ScanDiffKind.Missing,
                AssetId = asset.AssetId,
                AbsolutePath = path,
                PreviousFingerprint = asset.ContentFingerprint
            });
        }

        return new LibraryScanDiff
        {
            LibraryRoot = root,
            ComputedUtc = DateTime.UtcNow,
            Entries = entries
        };
    }

    static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return string.Empty;
        try { return Path.GetFullPath(root.Trim()); }
        catch { return root.Trim(); }
    }
}
