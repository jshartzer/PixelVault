using System;
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

        public static string FindSteamGridDbApiTokenInEnvironment()
        {
            foreach (var key in new[] { "PIXELVAULT_STEAMGRIDDB_TOKEN", "STEAMGRIDDB_API_KEY", "STEAMGRIDDB_TOKEN" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
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
                else if (key == "exiftool" && !string.IsNullOrWhiteSpace(value)) s.ExifToolPath = value;
                else if (key == "ffmpeg" && !string.IsNullOrWhiteSpace(value)) s.FfmpegPath = value;
                else if (key == "steamgriddb_token") s.SteamGridDbApiToken = value ?? string.Empty;
                else if (key == "library_folder_tile_size")
                {
                    if (int.TryParse(value, out var parsedSize)) s.LibraryFolderTileSize = NormalizeLibraryFolderTileSize(parsedSize);
                }
                else if (key == "library_folder_sort_mode") s.LibraryFolderSortMode = NormalizeLibraryFolderSortMode(value);
            }

            var bundledExifTool = Path.Combine(appRoot ?? string.Empty, "tools", "exiftool.exe");
            if (!File.Exists(s.ExifToolPath) && File.Exists(bundledExifTool)) s.ExifToolPath = bundledExifTool;

            var bundledFfmpeg = Path.Combine(appRoot ?? string.Empty, "tools", "ffmpeg.exe");
            if (string.IsNullOrWhiteSpace(s.FfmpegPath) || !File.Exists(s.FfmpegPath))
            {
                s.FfmpegPath = File.Exists(bundledFfmpeg)
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
            File.WriteAllLines(path, new[]
            {
                "source=" + SerializeSourceRoots(state.SourceRootsSerialized),
                "destination=" + (state.DestinationRoot ?? string.Empty),
                "library=" + (state.LibraryRoot ?? string.Empty),
                "exiftool=" + (state.ExifToolPath ?? string.Empty),
                "ffmpeg=" + (state.FfmpegPath ?? string.Empty),
                "steamgriddb_token=" + (state.SteamGridDbApiToken ?? string.Empty),
                "library_folder_tile_size=" + NormalizeLibraryFolderTileSize(state.LibraryFolderTileSize),
                "library_folder_sort_mode=" + NormalizeLibraryFolderSortMode(state.LibraryFolderSortMode)
            });
        }
    }
}
