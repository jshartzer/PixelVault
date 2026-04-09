using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// Single-file move target derived by placement rules (media path + sidecar handled by caller).
    /// </summary>
    internal readonly struct LibraryFileMovePlan
    {
        public LibraryFileMovePlan(string sourcePath, string targetDirectory, string targetFileName, bool resolvedFromGameIndex)
        {
            SourcePath = sourcePath ?? string.Empty;
            TargetDirectory = targetDirectory ?? string.Empty;
            TargetFileName = targetFileName ?? string.Empty;
            ResolvedFromGameIndex = resolvedFromGameIndex;
        }

        public string SourcePath { get; }
        public string TargetDirectory { get; }
        public string TargetFileName { get; }
        public bool ResolvedFromGameIndex { get; }

        public string TargetPath =>
            string.IsNullOrWhiteSpace(TargetDirectory)
                ? TargetFileName
                : Path.Combine(TargetDirectory, TargetFileName);
    }

    /// <summary>
    /// Central place for computing library storage folder labels/paths (PV-PLN-LIBST-001 Step 3).
    /// </summary>
    internal static class LibraryPlacementService
    {
        internal static LibraryFileMovePlan PlanImportRootSort(
            string sourceFilePath,
            string destinationRoot,
            GameIndexEditorRow resolvedRow,
            IReadOnlyList<GameIndexEditorRow> indexRows,
            Func<string, string> getSafeGameFolderName,
            Func<string, string> getGameNameFromFileName,
            Func<string, string, string> normalizeGameIndexNameWithFolder,
            Func<string, string> normalizeConsoleLabel,
            IReadOnlyDictionary<string, int> titleCounts)
        {
            var fileName = Path.GetFileName(sourceFilePath ?? string.Empty);
            var leaf = ResolveImportSortFolderLeaf(
                resolvedRow,
                indexRows,
                fileName,
                getSafeGameFolderName,
                getGameNameFromFileName,
                normalizeGameIndexNameWithFolder,
                normalizeConsoleLabel,
                titleCounts);
            var dir = string.IsNullOrWhiteSpace(leaf)
                ? (destinationRoot ?? string.Empty)
                : Path.Combine(destinationRoot ?? string.Empty, leaf);
            return new LibraryFileMovePlan(sourceFilePath, dir, fileName, resolvedRow != null);
        }

        internal static string BuildCanonicalStorageFolderPath(
            string libraryRoot,
            GameIndexEditorRow row,
            IReadOnlyList<GameIndexEditorRow> allRows,
            Func<string, string, string> normalizeGameIndexName,
            Func<string, string> getSafeGameFolderName,
            Func<string, string> normalizeConsoleLabel,
            IReadOnlyDictionary<string, int> titleCounts,
            string preferredFolderName = null)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot)) return string.Empty;
            var leaf = BuildCanonicalStorageFolderName(
                row,
                allRows,
                normalizeGameIndexName,
                getSafeGameFolderName,
                normalizeConsoleLabel,
                titleCounts,
                preferredFolderName);
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
            IReadOnlyDictionary<string, int> titleCounts,
            string preferredFolderName = null)
        {
            if (normalizeGameIndexName == null) throw new ArgumentNullException(nameof(normalizeGameIndexName));
            if (getSafeGameFolderName == null) throw new ArgumentNullException(nameof(getSafeGameFolderName));
            if (normalizeConsoleLabel == null) throw new ArgumentNullException(nameof(normalizeConsoleLabel));

            if (row == null) return getSafeGameFolderName("Unknown Game");

            var preferredNormalizedName = normalizeGameIndexName(preferredFolderName ?? string.Empty, row.FolderPath);
            var rows = (allRows ?? Array.Empty<GameIndexEditorRow>()).Where(r => r != null).ToList();
            var sg = (row.StorageGroupId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(sg))
            {
                if (!string.IsNullOrWhiteSpace(preferredNormalizedName))
                    return getSafeGameFolderName(preferredNormalizedName);

                var rep = FindStorageGroupRepresentativeRow(row, rows, normalizeGameIndexName);
                var normalizedName = normalizeGameIndexName(rep.Name ?? string.Empty, rep.FolderPath);
                if (string.IsNullOrWhiteSpace(normalizedName)) normalizedName = "Unknown Game";
                return getSafeGameFolderName(normalizedName);
            }

            var normalized = string.IsNullOrWhiteSpace(preferredNormalizedName)
                ? normalizeGameIndexName(row.Name ?? string.Empty, row.FolderPath)
                : preferredNormalizedName;
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

        internal static GameIndexEditorRow FindStorageGroupRepresentativeRow(
            GameIndexEditorRow row,
            IReadOnlyList<GameIndexEditorRow> allRows,
            Func<string, string, string> normalizeGameIndexName)
        {
            if (normalizeGameIndexName == null) throw new ArgumentNullException(nameof(normalizeGameIndexName));
            if (row == null) return null;

            var sg = (row.StorageGroupId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sg)) return row;

            var members = (allRows ?? Array.Empty<GameIndexEditorRow>())
                .Where(r => r != null && string.Equals((r.StorageGroupId ?? string.Empty).Trim(), sg, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => (r.GameId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var member in members)
            {
                var normalizedName = normalizeGameIndexName(member.Name ?? string.Empty, member.FolderPath);
                if (!string.IsNullOrWhiteSpace(normalizedName))
                    return member;
            }

            return members.FirstOrDefault() ?? row;
        }

        /// <summary>
        /// Map a parsed import filename to a game index row when Steam / non-Steam IDs or title+platform identity match.
        /// </summary>
        internal static GameIndexEditorRow TryResolveGameIndexRowForImportSort(
            FilenameParseResult parse,
            IReadOnlyList<GameIndexEditorRow> rows,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> cleanTag,
            Func<string, string, string> buildGameIndexIdentity)
        {
            if (parse == null || rows == null || rows.Count == 0) return null;
            if (normalizeConsoleLabel == null || cleanTag == null || buildGameIndexIdentity == null) return null;

            var list = rows.Where(r => r != null).ToList();
            var plat = normalizeConsoleLabel(parse.PlatformLabel ?? "Other");

            var steamApp = cleanTag(parse.SteamAppId ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(steamApp) && string.Equals(plat, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var r in list)
                {
                    if (!string.Equals(normalizeConsoleLabel(r.PlatformLabel ?? string.Empty), "Steam", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(cleanTag(r.SteamAppId ?? string.Empty), steamApp, StringComparison.OrdinalIgnoreCase))
                        return r;
                }
            }

            var nonSteam = cleanTag(parse.NonSteamId ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(nonSteam) && string.Equals(plat, "Emulation", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var r in list)
                {
                    if (!string.Equals(normalizeConsoleLabel(r.PlatformLabel ?? string.Empty), "Emulation", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(cleanTag(r.NonSteamId ?? string.Empty), nonSteam, StringComparison.OrdinalIgnoreCase))
                        return r;
                }
            }

            var hint = parse.GameTitleHint ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(hint))
            {
                var wanted = buildGameIndexIdentity(hint, parse.PlatformLabel ?? "Other");
                foreach (var r in list)
                {
                    if (string.Equals(buildGameIndexIdentity(r.Name ?? string.Empty, r.PlatformLabel ?? "Other"), wanted, StringComparison.OrdinalIgnoreCase))
                        return r;
                }
            }

            return null;
        }

        /// <summary>
        /// Folder segment under the import destination (or library organize root): game index placement when <paramref name="resolvedRow"/> is set, else filename stem rules.
        /// </summary>
        internal static string ResolveImportSortFolderLeaf(
            GameIndexEditorRow resolvedRow,
            IReadOnlyList<GameIndexEditorRow> indexRows,
            string fileName,
            Func<string, string> getSafeGameFolderName,
            Func<string, string> getGameNameFromFileName,
            Func<string, string, string> normalizeGameIndexNameWithFolder,
            Func<string, string> normalizeConsoleLabel,
            IReadOnlyDictionary<string, int> titleCounts)
        {
            if (resolvedRow != null
                && getSafeGameFolderName != null
                && normalizeGameIndexNameWithFolder != null
                && normalizeConsoleLabel != null)
            {
                return BuildCanonicalStorageFolderName(
                    resolvedRow,
                    indexRows,
                    normalizeGameIndexNameWithFolder,
                    getSafeGameFolderName,
                    normalizeConsoleLabel,
                    titleCounts);
            }

            if (getSafeGameFolderName == null) return string.Empty;
            var stem = getGameNameFromFileName != null
                ? getGameNameFromFileName(fileName)
                : Path.GetFileNameWithoutExtension(fileName);
            return getSafeGameFolderName(stem);
        }
    }
}
