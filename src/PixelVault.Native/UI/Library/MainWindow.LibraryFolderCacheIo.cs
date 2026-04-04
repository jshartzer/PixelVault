using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
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
            var parsed = ParseLibraryFolderCacheLines(root, lines);
            if (LibraryFolderCacheLooksIncomplete(root, parsed))
            {
                Log("Library folder cache snapshot looked incomplete for " + root + ". Ignoring cached folder list.");
                return null;
            }
            return parsed;
        }

        List<LibraryFolderInfo> LoadLibraryFolderCacheSnapshot(string root)
        {
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            var parsed = ParseLibraryFolderCacheLines(root, lines);
            if (LibraryFolderCacheLooksIncomplete(root, parsed))
            {
                Log("Library folder cache snapshot looked incomplete for " + root + ". Skipping startup prefill.");
                return null;
            }
            return parsed;
        }

        List<LibraryFolderInfo> ParseLibraryFolderCacheLines(string root, string[] lines)
        {
            var aliasMap = BuildSavedGameIdAliasMapFromFile(root);
            var list = new List<LibraryFolderInfo>();
            foreach (var line in lines.Skip(2))
            {
                var parts = line.Split('\t');
                if (parts.Length < 5) continue;
                if (parts.Length >= 9)
                {
                    list.Add(new LibraryFolderInfo
                    {
                        GameId = !string.IsNullOrWhiteSpace(NormalizeGameId(parts[0])) && aliasMap.ContainsKey(NormalizeGameId(parts[0])) ? aliasMap[NormalizeGameId(parts[0])] : parts[0],
                        FolderPath = parts[1],
                        Name = parts[2],
                        FileCount = ParseInt(parts[3]),
                        PreviewImagePath = parts[4],
                        PlatformLabel = parts[5],
                        FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : new string[0],
                        NewestCaptureUtcTicks = parts.Length > 9 ? ParseLong(parts[9]) : 0,
                        SteamAppId = parts.Length > 7 ? parts[7] : string.Empty,
                        SteamGridDbId = parts.Length > 8 ? parts[8] : string.Empty
                    });
                }
                else if (parts.Length >= 8)
                {
                    list.Add(new LibraryFolderInfo
                    {
                        GameId = !string.IsNullOrWhiteSpace(NormalizeGameId(parts[0])) && aliasMap.ContainsKey(NormalizeGameId(parts[0])) ? aliasMap[NormalizeGameId(parts[0])] : parts[0],
                        FolderPath = parts[1],
                        Name = parts[2],
                        FileCount = ParseInt(parts[3]),
                        PreviewImagePath = parts[4],
                        PlatformLabel = parts[5],
                        FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : new string[0],
                        NewestCaptureUtcTicks = 0,
                        SteamAppId = parts.Length > 7 ? parts[7] : string.Empty,
                        SteamGridDbId = string.Empty
                    });
                }
                else
                {
                    list.Add(new LibraryFolderInfo
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
                        SteamAppId = parts.Length > 6 ? parts[6] : string.Empty,
                        SteamGridDbId = string.Empty
                    });
                }
            }
            return list;
        }

        void SaveLibraryFolderCache(string root, string stamp, List<LibraryFolderInfo> folders)
        {
            var path = LibraryFolderCachePath(root);
            var lines = new List<string>();
            lines.Add(root);
            lines.Add(stamp);
            foreach (var folder in folders)
            {
                lines.Add(string.Join("\t", new[]
                {
                    NormalizeGameId(folder.GameId),
                    folder.FolderPath ?? string.Empty,
                    folder.Name ?? string.Empty,
                    folder.FileCount.ToString(),
                    folder.PreviewImagePath ?? string.Empty,
                    folder.PlatformLabel ?? string.Empty,
                    string.Join("|", (folder.FilePaths ?? new string[0]).Where(File.Exists)),
                    folder.SteamAppId ?? string.Empty,
                    folder.SteamGridDbId ?? string.Empty,
                    folder.NewestCaptureUtcTicks > 0 ? folder.NewestCaptureUtcTicks.ToString() : string.Empty
                }));
            }
            File.WriteAllLines(path, lines.ToArray());
        }
    }
}
