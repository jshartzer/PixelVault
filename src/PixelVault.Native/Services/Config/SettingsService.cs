using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed class SettingsService : ISettingsService
    {
        public static string SerializeSourceRoots(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return string.Join(";", raw
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public static int NormalizeLibraryFolderTileSize(int value)
        {
            if (value < 140) return 140;
            if (value > 340) return 340;
            return value;
        }

        public static string NormalizeLibraryFolderSortMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "recent" || normalized == "recently added" || normalized == "recently-added") return "recent";
            if (normalized == "photos" || normalized == "most photos" || normalized == "photo count") return "photos";
            return "platform";
        }

        public static string NormalizeLibraryGroupingMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "console" || normalized == "by console" || normalized == "platform" || normalized == "by platform") return "console";
            return "all";
        }

        public static string FindSteamGridDbApiTokenInEnvironment()
        {
            foreach (var key in new[] { "PIXELVAULT_STEAMGRIDDB_TOKEN", "STEAMGRIDDB_API_KEY", "STEAMGRIDDB_TOKEN" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        internal static string FindBundledToolPath(string appRoot, string executableName)
        {
            if (string.IsNullOrWhiteSpace(appRoot) || string.IsNullOrWhiteSpace(executableName)) return string.Empty;
            try
            {
                var current = new DirectoryInfo(Path.GetFullPath(appRoot));
                while (current != null)
                {
                    var candidate = Path.Combine(current.FullName, "tools", executableName);
                    if (File.Exists(candidate)) return candidate;
                    current = current.Parent;
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        public AppSettings LoadFromIni(
            string path,
            AppSettings initialState,
            string appRoot,
            Func<string> findFfmpegOnPath,
            Func<string> readSteamGridDbTokenFromEnvironment)
        {
            var s = AppSettings.Clone(initialState);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return s;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains("=")) continue;
                var index = line.IndexOf('=');
                var key = line.Substring(0, index);
                var value = line.Substring(index + 1);
                if (key == "source") s.SourceRootsSerialized = SerializeSourceRoots(value);
                else if (key == "destination") s.DestinationRoot = value ?? string.Empty;
                else if (key == "library") s.LibraryRoot = value ?? string.Empty;
                else if (key == "library_index_anchor") s.LibraryIndexAnchor = value ?? string.Empty;
                else if (key == "exiftool" && !string.IsNullOrWhiteSpace(value)) s.ExifToolPath = value;
                else if (key == "ffmpeg" && !string.IsNullOrWhiteSpace(value)) s.FfmpegPath = value;
                else if (key == "steamgriddb_token") s.SteamGridDbApiToken = value ?? string.Empty;
                else if (key == "library_folder_tile_size")
                {
                    if (int.TryParse(value, out var parsedSize)) s.LibraryFolderTileSize = NormalizeLibraryFolderTileSize(parsedSize);
                }
                else if (key == "library_folder_sort_mode") s.LibraryFolderSortMode = NormalizeLibraryFolderSortMode(value);
                else if (key == "library_grouping_mode") s.LibraryGroupingMode = NormalizeLibraryGroupingMode(value);
                else if (key == "library_browser_search") s.LibraryBrowserSearchText = value ?? string.Empty;
                else if (key == "library_browser_last_view_key") s.LibraryBrowserLastViewKey = value ?? string.Empty;
                else if (key == "library_browser_folder_scroll")
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fy)) s.LibraryBrowserFolderScroll = Math.Max(0, fy);
                }
                else if (key == "library_browser_detail_scroll")
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dy)) s.LibraryBrowserDetailScroll = Math.Max(0, dy);
                }
                else if (key == "manual_metadata_recent_titles") s.ManualMetadataRecentTitleLabels = value ?? string.Empty;
                else if (key == "troubleshooting_logging_enabled")
                {
                    var normalizedValue = (value ?? string.Empty).Trim();
                    s.TroubleshootingLoggingEnabled =
                        string.Equals(normalizedValue, "1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "yes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "on", StringComparison.OrdinalIgnoreCase);
                }
                else if (key == "troubleshooting_log_redact_paths")
                {
                    var normalizedValue = (value ?? string.Empty).Trim();
                    s.TroubleshootingLogRedactPaths =
                        string.Equals(normalizedValue, "1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "yes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "on", StringComparison.OrdinalIgnoreCase);
                }
            }

            var bundledExifTool = FindBundledToolPath(appRoot, "exiftool.exe");
            if ((string.IsNullOrWhiteSpace(s.ExifToolPath) || !File.Exists(s.ExifToolPath))
                && !string.IsNullOrWhiteSpace(bundledExifTool))
            {
                s.ExifToolPath = bundledExifTool;
            }

            var bundledFfmpeg = FindBundledToolPath(appRoot, "ffmpeg.exe");
            if (string.IsNullOrWhiteSpace(s.FfmpegPath) || !File.Exists(s.FfmpegPath))
            {
                s.FfmpegPath = !string.IsNullOrWhiteSpace(bundledFfmpeg)
                    ? bundledFfmpeg
                    : (findFfmpegOnPath != null ? findFfmpegOnPath() : string.Empty) ?? string.Empty;
            }

            var envSteamGridDbToken = readSteamGridDbTokenFromEnvironment != null ? readSteamGridDbTokenFromEnvironment() : string.Empty;
            if (!string.IsNullOrWhiteSpace(envSteamGridDbToken)) s.SteamGridDbApiToken = envSteamGridDbToken;

            return s;
        }

        public void SaveToIni(string path, AppSettings state)
        {
            if (string.IsNullOrWhiteSpace(path) || state == null) return;

            var oldLib = TryReadIniValue(path, "library");
            var oldAnchor = TryReadIniValue(path, "library_index_anchor");
            var newLib = state.LibraryRoot ?? string.Empty;

            string anchorOut;
            if (!string.IsNullOrWhiteSpace(newLib)
                && !string.IsNullOrWhiteSpace(oldAnchor)
                && string.Equals(newLib, oldAnchor, StringComparison.OrdinalIgnoreCase))
            {
                anchorOut = newLib;
            }
            else if (!string.IsNullOrWhiteSpace(oldLib) && !string.Equals(oldLib, newLib, StringComparison.OrdinalIgnoreCase))
            {
                anchorOut = oldLib;
            }
            else if (!string.IsNullOrWhiteSpace(oldAnchor))
            {
                anchorOut = oldAnchor;
            }
            else
            {
                anchorOut = string.IsNullOrWhiteSpace(newLib) ? string.Empty : newLib;
            }

            File.WriteAllLines(path, new[]
            {
                "source=" + SerializeSourceRoots(state.SourceRootsSerialized),
                "destination=" + (state.DestinationRoot ?? string.Empty),
                "library=" + (state.LibraryRoot ?? string.Empty),
                "library_index_anchor=" + anchorOut,
                "exiftool=" + (state.ExifToolPath ?? string.Empty),
                "ffmpeg=" + (state.FfmpegPath ?? string.Empty),
                "steamgriddb_token=" + (state.SteamGridDbApiToken ?? string.Empty),
                "library_folder_tile_size=" + NormalizeLibraryFolderTileSize(state.LibraryFolderTileSize),
                "library_folder_sort_mode=" + NormalizeLibraryFolderSortMode(state.LibraryFolderSortMode),
                "library_grouping_mode=" + NormalizeLibraryGroupingMode(state.LibraryGroupingMode),
                "library_browser_search=" + (state.LibraryBrowserSearchText ?? string.Empty).Replace("\r", " ").Replace("\n", " "),
                "library_browser_last_view_key=" + (state.LibraryBrowserLastViewKey ?? string.Empty).Replace("\r", " ").Replace("\n", " "),
                "library_browser_folder_scroll=" + Math.Max(0, state.LibraryBrowserFolderScroll).ToString(CultureInfo.InvariantCulture),
                "library_browser_detail_scroll=" + Math.Max(0, state.LibraryBrowserDetailScroll).ToString(CultureInfo.InvariantCulture),
                "manual_metadata_recent_titles=" + (state.ManualMetadataRecentTitleLabels ?? string.Empty).Replace("\r", " ").Replace("\n", " "),
                "troubleshooting_logging_enabled=" + (state.TroubleshootingLoggingEnabled ? "1" : "0"),
                "troubleshooting_log_redact_paths=" + (state.TroubleshootingLogRedactPaths ? "1" : "0")
            });
        }

        public bool PersistResolvedToolPaths(string path, string exifToolPath, string ffmpegPath)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            var lines = File.ReadAllLines(path).ToList();
            var changed = false;
            changed |= PersistResolvedToolPath(lines, "exiftool", exifToolPath);
            changed |= PersistResolvedToolPath(lines, "ffmpeg", ffmpegPath);
            if (changed) File.WriteAllLines(path, lines);
            return changed;
        }

        static string TryReadIniValue(string path, string wantedKey)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(wantedKey) || !File.Exists(path)) return string.Empty;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains('=')) continue;
                    var i = line.IndexOf('=');
                    var key = line.Substring(0, i);
                    if (!string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase)) continue;
                    return line.Substring(i + 1) ?? string.Empty;
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        static bool PersistResolvedToolPath(List<string> lines, string key, string resolvedPath)
        {
            if (lines == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath)) return false;
            var current = TryReadIniValue(lines, key);
            if (PathValuesEqual(current, resolvedPath)) return false;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current)) return false;
            return UpsertIniValue(lines, key, resolvedPath);
        }

        static bool UpsertIniValue(List<string> lines, string wantedKey, string value)
        {
            if (lines == null || string.IsNullOrWhiteSpace(wantedKey)) return false;
            var newLine = wantedKey + "=" + (value ?? string.Empty);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || !line.Contains('=')) continue;
                var index = line.IndexOf('=');
                var key = line.Substring(0, index);
                if (!string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(line, newLine, StringComparison.Ordinal)) return false;
                lines[i] = newLine;
                return true;
            }

            lines.Add(newLine);
            return true;
        }

        static string TryReadIniValue(IEnumerable<string> lines, string wantedKey)
        {
            if (lines == null || string.IsNullOrWhiteSpace(wantedKey)) return string.Empty;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains('=')) continue;
                var index = line.IndexOf('=');
                var key = line.Substring(0, index);
                if (!string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase)) continue;
                return line.Substring(index + 1) ?? string.Empty;
            }
            return string.Empty;
        }

        static bool PathValuesEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
