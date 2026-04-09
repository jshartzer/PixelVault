using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Returns true when the cached and current full stamps match in folder count and name-hash and differ only in the max child-directory UTC tick field (activity under existing game folders).</summary>
        internal static bool LibraryFolderCacheInventoryStampDiffersOnlyInDirectoryTicks(string cachedFullStamp, string currentFullStamp)
        {
            if (string.IsNullOrEmpty(cachedFullStamp) || string.IsNullOrEmpty(currentFullStamp)) return false;
            if (string.Equals(cachedFullStamp, currentFullStamp, StringComparison.Ordinal)) return false;
            var c = cachedFullStamp.Split('|');
            var n = currentFullStamp.Split('|');
            if (c.Length != 3 || n.Length != 3) return false;
            if (!string.Equals(c[0], n[0], StringComparison.Ordinal)) return false;
            if (!string.Equals(c[2], n[2], StringComparison.Ordinal)) return false;
            if (string.Equals(c[1], n[1], StringComparison.Ordinal)) return false;
            return true;
        }

        internal static bool IsLibraryFolderCacheMetadataRevisionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.IndexOf('\t') >= 0) return false;
            var parts = line.Split('|');
            return parts.Length == 2
                && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        /// <summary>True when the file sits in <c>libraryRoot\GameFolder\file</c> (same rule as top-level-only enumeration in <see cref="LibraryScanner.LoadLibraryFolders"/>).</summary>
        internal static bool IsLibraryMediaFileDirectlyUnderGameFolder(string libraryRoot, string filePath)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || string.IsNullOrWhiteSpace(filePath)) return false;
            var rootNorm = libraryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(parent)) return false;
            var grandparent = Path.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(grandparent)) return false;
            return string.Equals(grandparent, rootNorm, StringComparison.OrdinalIgnoreCase);
        }

        internal string GetLibraryMetadataIndexRevision(string root)
        {
            try
            {
                var path = LibraryMetadataIndexPath(root);
                if (!File.Exists(path)) return "missing|0";
                var info = new FileInfo(path);
                return info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            }
            catch
            {
                return "err|0";
            }
        }

        /// <summary>When folder-cache line 3 matches the metadata index revision and only directory mtimes drifted, rebuild file list from the persisted index (no per-game folder file sweep). New files on disk that are not yet indexed will not appear until a library scan.</summary>
        internal bool TryGetIndexOnlyFolderCacheRefresh(string root, string currentFullStamp, out List<string> mediaFilePathsOneLevelUnderRoot)
        {
            mediaFilePathsOneLevelUnderRoot = null;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(currentFullStamp)) return false;
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return false;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch
            {
                return false;
            }

            if (lines.Length < 3) return false;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return false;
            if (!LibraryFolderCacheInventoryStampDiffersOnlyInDirectoryTicks(lines[1], currentFullStamp)) return false;
            var expectedRev = GetLibraryMetadataIndexRevision(root);
            if (!string.Equals(lines[2], expectedRev, StringComparison.Ordinal)) return false;

            var files = new List<string>();
            foreach (var kv in LoadLibraryMetadataIndexViaSessionWhenActive(root, true))
            {
                var p = kv.Key;
                if (string.IsNullOrWhiteSpace(p) || !IsMedia(p) || !File.Exists(p)) continue;
                if (!IsLibraryMediaFileDirectlyUnderGameFolder(root, p)) continue;
                files.Add(p);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            mediaFilePathsOneLevelUnderRoot = files;
            return true;
        }

        internal static bool ParseLibraryFolderCacheBoolean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var normalized = value.Trim();
            return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
        }

        internal static LibraryFolderInfo ParseLibraryFolderCacheRecordLine(string root, string line, IReadOnlyDictionary<string, string> aliasMap)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var parts = line.Split('\t');
            if (parts.Length < 5) return null;

            string ResolveAliasedGameId(string value)
            {
                var normalizedGameId = CleanTag(value);
                if (string.IsNullOrWhiteSpace(normalizedGameId) || aliasMap == null || !aliasMap.ContainsKey(normalizedGameId)) return value ?? string.Empty;
                return aliasMap[normalizedGameId];
            }

            if (parts.Length >= 9)
            {
                return new LibraryFolderInfo
                {
                    GameId = ResolveAliasedGameId(parts[0]),
                    FolderPath = parts[1],
                    Name = parts[2],
                    FileCount = ParseInt(parts[3]),
                    PreviewImagePath = parts[4],
                    PlatformLabel = parts[5],
                    FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                        ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : new string[0],
                    NewestCaptureUtcTicks = parts.Length > 9 ? ParseLong(parts[9]) : 0,
                    NewestRecentSortUtcTicks = parts.Length > 10 ? ParseLong(parts[10]) : 0,
                    IsCompleted100Percent = parts.Length > 11 && ParseLibraryFolderCacheBoolean(parts[11]),
                    CompletedUtcTicks = parts.Length > 12 ? ParseLong(parts[12]) : 0,
                    SteamAppId = parts.Length > 7 ? parts[7] : string.Empty,
                    SteamGridDbId = parts.Length > 8 ? parts[8] : string.Empty,
                    RetroAchievementsGameId = parts.Length > 13 ? parts[13] : string.Empty,
                    NonSteamId = parts.Length > 14 ? parts[14] : string.Empty,
                    PendingGameAssignment = parts.Length > 15 && ParseLibraryFolderCacheBoolean(parts[15])
                };
            }

            if (parts.Length >= 8)
            {
                return new LibraryFolderInfo
                {
                    GameId = ResolveAliasedGameId(parts[0]),
                    FolderPath = parts[1],
                    Name = parts[2],
                    FileCount = ParseInt(parts[3]),
                    PreviewImagePath = parts[4],
                    PlatformLabel = parts[5],
                    FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                        ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                        : new string[0],
                    NewestCaptureUtcTicks = 0,
                    NewestRecentSortUtcTicks = 0,
                    IsCompleted100Percent = false,
                    CompletedUtcTicks = 0,
                    SteamAppId = parts.Length > 7 ? parts[7] : string.Empty,
                    SteamGridDbId = string.Empty
                };
            }

            return new LibraryFolderInfo
            {
                GameId = string.Empty,
                FolderPath = parts[0],
                Name = parts[1],
                FileCount = ParseInt(parts[2]),
                PreviewImagePath = parts[3],
                PlatformLabel = parts[4],
                FilePaths = parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5])
                    ? parts[5].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : new string[0],
                NewestCaptureUtcTicks = 0,
                NewestRecentSortUtcTicks = 0,
                IsCompleted100Percent = false,
                CompletedUtcTicks = 0,
                SteamAppId = parts.Length > 6 ? parts[6] : string.Empty,
                SteamGridDbId = string.Empty
            };
        }

        internal static string SerializeLibraryFolderCacheRecordLine(LibraryFolderInfo folder)
        {
            return string.Join("\t", new[]
            {
                CleanTag(folder == null ? string.Empty : folder.GameId),
                folder == null ? string.Empty : (folder.FolderPath ?? string.Empty),
                folder == null ? string.Empty : (folder.Name ?? string.Empty),
                (folder == null ? 0 : folder.FileCount).ToString(),
                folder == null ? string.Empty : (folder.PreviewImagePath ?? string.Empty),
                folder == null ? string.Empty : (folder.PlatformLabel ?? string.Empty),
                string.Join("|", (folder == null ? new string[0] : (folder.FilePaths ?? new string[0])).Where(File.Exists)),
                folder == null ? string.Empty : (folder.SteamAppId ?? string.Empty),
                folder == null ? string.Empty : (folder.SteamGridDbId ?? string.Empty),
                folder != null && folder.NewestCaptureUtcTicks > 0 ? folder.NewestCaptureUtcTicks.ToString() : string.Empty,
                folder != null && folder.NewestRecentSortUtcTicks > 0 ? folder.NewestRecentSortUtcTicks.ToString() : string.Empty,
                folder != null && folder.IsCompleted100Percent ? "1" : "0",
                folder != null && folder.CompletedUtcTicks > 0 ? folder.CompletedUtcTicks.ToString() : string.Empty,
                folder == null ? string.Empty : CleanTag(folder.RetroAchievementsGameId ?? string.Empty),
                folder == null ? string.Empty : CleanTag(folder.NonSteamId ?? string.Empty),
                folder != null && folder.PendingGameAssignment ? "1" : string.Empty
            });
        }

        void ClearLibraryFolderCache(string root)
        {
            var path = LibraryFolderCachePath(root);
            if (File.Exists(path)) File.Delete(path);
        }

        bool HasLibraryFolderCacheSnapshot(string root)
        {
            return LoadLibraryFolderCacheSnapshot(root) != null;
        }

        bool LibraryFolderCacheLooksIncomplete(string root, List<LibraryFolderInfo> folders)
        {
            if (string.IsNullOrWhiteSpace(root) || folders == null || folders.Count != 1) return false;
            try
            {
                return Directory.Exists(root) && Directory.EnumerateDirectories(root).Skip(1).Any();
            }
            catch
            {
                return false;
            }
        }

        List<LibraryFolderInfo> LoadLibraryFolderCache(string root, string stamp)
        {
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(lines[1], stamp, StringComparison.Ordinal)) return null;
            if (lines.Length >= 3 && !string.Equals(lines[2], GetLibraryMetadataIndexRevision(root), StringComparison.Ordinal)) return null;
            var parsed = ParseLibraryFolderCacheLines(root, lines);
            if (LibraryFolderCacheLooksIncomplete(root, parsed))
            {
                Log("Library folder cache snapshot looked incomplete for " + root + ". Ignoring cached folder list.");
                return null;
            }
            ApplySavedGameIndexRows(root, parsed);
            return parsed;
        }

        List<LibraryFolderInfo> LoadLibraryFolderCacheSnapshot(string root)
        {
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            if (lines.Length >= 3 && !string.Equals(lines[2], GetLibraryMetadataIndexRevision(root), StringComparison.Ordinal)) return null;
            var parsed = ParseLibraryFolderCacheLines(root, lines);
            if (LibraryFolderCacheLooksIncomplete(root, parsed))
            {
                Log("Library folder cache snapshot looked incomplete for " + root + ". Skipping startup prefill.");
                return null;
            }
            ApplySavedGameIndexRows(root, parsed);
            return parsed;
        }

        List<LibraryFolderInfo> ParseLibraryFolderCacheLines(string root, string[] lines)
        {
            var aliasMap = BuildSavedGameIdAliasMapFromFile(root);
            var list = new List<LibraryFolderInfo>();
            var dataStart = 2;
            if (lines.Length > 2 && IsLibraryFolderCacheMetadataRevisionLine(lines[2]))
                dataStart = 3;
            foreach (var line in lines.Skip(dataStart))
            {
                var parsed = ParseLibraryFolderCacheRecordLine(root, line, aliasMap);
                if (parsed != null) list.Add(parsed);
            }
            return list;
        }

        void SaveLibraryFolderCache(string root, string stamp, List<LibraryFolderInfo> folders)
        {
            var path = LibraryFolderCachePath(root);
            var lines = new List<string>();
            lines.Add(root);
            lines.Add(stamp);
            lines.Add(GetLibraryMetadataIndexRevision(root));
            foreach (var folder in folders)
            {
                lines.Add(SerializeLibraryFolderCacheRecordLine(folder));
            }
            File.WriteAllLines(path, lines.ToArray());
        }
    }
}
