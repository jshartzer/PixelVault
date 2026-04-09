using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// Central place for computing library storage folder labels/paths (PV-PLN-LIBST-001 Step 3).
    /// </summary>
    internal static class LibraryPlacementService
    {
        internal static string BuildCanonicalStorageFolderPath(
            string libraryRoot,
            GameIndexEditorRow row,
            IReadOnlyList<GameIndexEditorRow> allRows,
            Func<string, string, string> normalizeGameIndexName,
            Func<string, string> getSafeGameFolderName,
            Func<string, string> normalizeConsoleLabel,
            IReadOnlyDictionary<string, int> titleCounts)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot)) return string.Empty;
            var leaf = BuildCanonicalStorageFolderName(
                row,
                allRows,
                normalizeGameIndexName,
                getSafeGameFolderName,
                normalizeConsoleLabel,
                titleCounts);
            return string.IsNullOrWhiteSpace(leaf) ? string.Empty : Path.Combine(libraryRoot, leaf);
        }

        /// <summary>
        /// Folder name only (single path segment). Rows sharing a non-empty <see cref="GameIndexEditorRow.StorageGroupId"/>
        /// use one shared label (no per-platform suffix). Empty storage group keeps legacy title-count disambiguation.
        /// </summary>
        internal static string BuildCanonicalStorageFolderName(
            GameIndexEditorRow row,
            IReadOnlyList<GameIndexEditorRow> allRows,
            Func<string, string, string> normalizeGameIndexName,
            Func<string, string> getSafeGameFolderName,
            Func<string, string> normalizeConsoleLabel,
            IReadOnlyDictionary<string, int> titleCounts)
        {
            if (normalizeGameIndexName == null) throw new ArgumentNullException(nameof(normalizeGameIndexName));
            if (getSafeGameFolderName == null) throw new ArgumentNullException(nameof(getSafeGameFolderName));
            if (normalizeConsoleLabel == null) throw new ArgumentNullException(nameof(normalizeConsoleLabel));

            if (row == null) return getSafeGameFolderName("Unknown Game");

            var rows = (allRows ?? Array.Empty<GameIndexEditorRow>()).Where(r => r != null).ToList();
            var sg = (row.StorageGroupId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(sg))
            {
                var members = rows
                    .Where(r => string.Equals((r.StorageGroupId ?? string.Empty).Trim(), sg, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => (r.GameId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                GameIndexEditorRow rep = null;
                foreach (var r in members)
                {
                    var n = normalizeGameIndexName(r.Name ?? string.Empty, r.FolderPath);
                    if (!string.IsNullOrWhiteSpace(n))
                    {
                        rep = r;
                        break;
                    }
                }

                if (rep == null) rep = members.FirstOrDefault() ?? row;
                var normalizedName = normalizeGameIndexName(rep.Name ?? string.Empty, rep.FolderPath);
                if (string.IsNullOrWhiteSpace(normalizedName)) normalizedName = "Unknown Game";
                return getSafeGameFolderName(normalizedName);
            }

            var normalized = normalizeGameIndexName(row.Name ?? string.Empty, row.FolderPath);
            if (string.IsNullOrWhiteSpace(normalized)) normalized = "Unknown Game";
            var safeName = getSafeGameFolderName(normalized);
            var key = normalizeGameIndexName(normalized, null);
            var count = 0;
            if (titleCounts != null && !string.IsNullOrWhiteSpace(key) && titleCounts.TryGetValue(key, out var c))
                count = c;
            if (count > 1)
            {
                var platform = normalizeConsoleLabel(row.PlatformLabel ?? string.Empty);
                return getSafeGameFolderName(safeName + " - " + platform);
            }
            return safeName;
        }
    }
}
