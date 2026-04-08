using System;
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

        /// <summary>Largest photo-size preset in the library menu; Ctrl+scroll can go up to <see cref="LibraryPhotoTileScrollHardMax"/> (250% of this).</summary>
        public const int LibraryPhotoTileMenuLargestPreset = 560;

        /// <summary>Max capture tile width when using Ctrl+scroll — 150% above the largest menu preset (total 250% of <see cref="LibraryPhotoTileMenuLargestPreset"/>).</summary>
        public static int LibraryPhotoTileScrollHardMax => (int)Math.Round(LibraryPhotoTileMenuLargestPreset * 2.5d);

        public static int NormalizeLibraryFolderTileSize(int value)
        {
            if (value < 48) return 48;
            if (value > 1000) return 1000;
            return value;
        }

        /// <summary>Clamps library capture tile width; upper bound allows zoom past menu presets via Ctrl+scroll.</summary>
        public static int NormalizeLibraryPhotoTileSize(int value)
        {
            if (value < 160) return 160;
            var max = LibraryPhotoTileScrollHardMax;
            if (value > max) return max;
            return value;
        }

        /// <summary>0 = auto column count from viewport and tile size; 1–12 = fixed columns (clamped to what fits).</summary>
        public static int NormalizeLibraryFolderGridColumnCount(int value)
        {
            if (value < 0) return 0;
            if (value > 12) return 12;
            return value;
        }

        /// <summary>0 = auto; 1–8 = fixed columns for non-timeline capture grids.</summary>
        public static int NormalizeLibraryPhotoGridColumnCount(int value)
        {
            if (value < 0) return 0;
            if (value > 8) return 8;
            return value;
        }

        /// <summary>Photo workspace left rail: 0 = auto (at most 2 columns), 1 or 2 = fixed.</summary>
        public static int NormalizeLibraryPhotoRailFolderGridColumnCount(int value)
        {
            if (value < 0) return 0;
            if (value > 2) return 2;
            return value;
        }

        public static string NormalizeLibraryFolderSortMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "alpha" || normalized == "alphabetical" || normalized == "alphabetic" || normalized == "name" || normalized == "platform") return "alpha";
            if (normalized == "captured" || normalized == "date captured" || normalized == "capture date" || normalized == "date-captured" || normalized == "newest") return "captured";
            if (normalized == "recent" || normalized == "recently added" || normalized == "recently-added" || normalized == "added" || normalized == "date added" || normalized == "date-added") return "added";
            if (normalized == "photos" || normalized == "most photos" || normalized == "photo count") return "photos";
            return "alpha";
        }

        public static string NormalizeLibraryFolderFilterMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "completed" || normalized == "100" || normalized == "100%" || normalized == "100 percent" || normalized == "100 percent achievements") return "completed";
            if (normalized == "crossplatform" || normalized == "cross-platform" || normalized == "cross platform" || normalized == "multiple platforms") return "crossplatform";
            if (normalized == "large" || normalized == "25+" || normalized == "25 plus" || normalized == "25+ captures" || normalized == "large collection") return "large";
            if (normalized == "missingid" || normalized == "missing_id" || normalized == "missing id" || normalized == "missing ids" || normalized == "needs id" || normalized == "needssteam" || normalized == "needs_steam" || normalized == "steam no app" || normalized == "missing steam appid" || normalized == "missing steam app id" || normalized == "steam missing appid" || normalized == "needssteamgrid" || normalized == "needs_steam_grid" || normalized == "missing steamgriddb" || normalized == "missing steam grid" || normalized == "steam missing grid" || normalized == "steam griddb" || normalized == "missinggameid" || normalized == "missing_game_id" || normalized == "no game id" || normalized == "no gameid" || normalized == "needs game id") return "missingid";
            if (normalized == "nocover" || normalized == "no_cover" || normalized == "no cover" || normalized == "missing cover" || normalized == "without cover") return "nocover";
            return "all";
        }

        public static string NormalizeLibraryGroupingMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "timeline" || normalized == "photo timeline" || normalized == "capture timeline") return "timeline";
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

        public static string FindSteamWebApiKeyInEnvironment()
        {
            foreach (var key in new[] { "PIXELVAULT_STEAM_WEB_API_KEY", "STEAM_WEB_API_KEY", "STEAM_API_KEY" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        public static string FindRetroAchievementsApiKeyInEnvironment()
        {
            foreach (var key in new[] { "PIXELVAULT_RETROACHIEVEMENTS_API_KEY", "RETROACHIEVEMENTS_API_KEY", "RA_API_KEY" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        public static string FindSteamUserId64InEnvironment()
        {
            foreach (var key in new[] { "PIXELVAULT_STEAM_USER_ID", "STEAM_USER_ID_64", "STEAMID64" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        public static string FindRetroAchievementsUsernameInEnvironment()
        {
            foreach (var key in new[] { "PIXELVAULT_RETROACHIEVEMENTS_USERNAME", "RETROACHIEVEMENTS_USERNAME", "RA_USERNAME" })
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
                else if (key == "starred_export_folder") s.StarredExportFolder = value ?? string.Empty;
                else if (key == "library_index_anchor") s.LibraryIndexAnchor = value ?? string.Empty;
                else if (key == "exiftool" && !string.IsNullOrWhiteSpace(value)) s.ExifToolPath = value;
                else if (key == "ffmpeg" && !string.IsNullOrWhiteSpace(value)) s.FfmpegPath = value;
                else if (key == "steamgriddb_token") s.SteamGridDbApiToken = value ?? string.Empty;
                else if (key == "steam_web_api_key") s.SteamWebApiKey = value ?? string.Empty;
                else if (key == "retroachievements_api_key") s.RetroAchievementsApiKey = value ?? string.Empty;
                else if (key == "steam_user_id_64") s.SteamUserId64 = value ?? string.Empty;
                else if (key == "retroachievements_username") s.RetroAchievementsUsername = value ?? string.Empty;
                else if (key == "library_folder_tile_size")
                {
                    if (int.TryParse(value, out var parsedSize)) s.LibraryFolderTileSize = NormalizeLibraryFolderTileSize(parsedSize);
                }
                else if (key == "library_photo_tile_size")
                {
                    if (int.TryParse(value, out var photoSize)) s.LibraryPhotoTileSize = NormalizeLibraryPhotoTileSize(photoSize);
                }
                else if (key == "library_folder_grid_columns")
                {
                    if (int.TryParse(value, out var fc)) s.LibraryFolderGridColumnCount = NormalizeLibraryFolderGridColumnCount(fc);
                }
                else if (key == "library_photo_grid_columns")
                {
                    if (int.TryParse(value, out var pc)) s.LibraryPhotoGridColumnCount = NormalizeLibraryPhotoGridColumnCount(pc);
                }
                else if (key == "library_photo_rail_folder_tile_size")
                {
                    if (int.TryParse(value, out var rts)) s.LibraryPhotoRailFolderTileSize = NormalizeLibraryFolderTileSize(rts);
                }
                else if (key == "library_photo_rail_folder_sort_mode")
                    s.LibraryPhotoRailFolderSortMode = NormalizeLibraryFolderSortMode(value);
                else if (key == "library_photo_rail_folder_filter_mode")
                    s.LibraryPhotoRailFolderFilterMode = NormalizeLibraryFolderFilterMode(value);
                else if (key == "library_photo_rail_folder_grid_columns")
                {
                    if (int.TryParse(value, out var rgc)) s.LibraryPhotoRailFolderGridColumnCount = NormalizeLibraryPhotoRailFolderGridColumnCount(rgc);
                }
                else if (key == "library_folder_sort_mode") s.LibraryFolderSortMode = NormalizeLibraryFolderSortMode(value);
                else if (key == "library_grouping_mode") s.LibraryGroupingMode = NormalizeLibraryGroupingMode(value);
                else if (key == "library_folder_filter_mode") s.LibraryFolderFilterMode = NormalizeLibraryFolderFilterMode(value);
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
                else if (key == "library_browser_folder_pane_width")
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pw)) s.LibraryBrowserFolderPaneWidth = Math.Max(0, pw);
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
                else if (key == "library_double_click_set_folder_cover")
                {
                    var normalizedValue = (value ?? string.Empty).Trim();
                    s.LibraryDoubleClickSetsFolderCover =
                        string.Equals(normalizedValue, "1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "yes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "on", StringComparison.OrdinalIgnoreCase);
                }
                else if (key == "library_refresh_hero_banner_cache_on_next_open")
                {
                    var normalizedValue = (value ?? string.Empty).Trim();
                    s.LibraryRefreshHeroBannerCacheOnNextLibraryOpen =
                        string.Equals(normalizedValue, "1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "true", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "yes", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(normalizedValue, "on", StringComparison.OrdinalIgnoreCase);
                }
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

            var envSteamWeb = FindSteamWebApiKeyInEnvironment();
            if (!string.IsNullOrWhiteSpace(envSteamWeb)) s.SteamWebApiKey = envSteamWeb;
            var envRa = FindRetroAchievementsApiKeyInEnvironment();
            if (!string.IsNullOrWhiteSpace(envRa)) s.RetroAchievementsApiKey = envRa;
            var envSid = FindSteamUserId64InEnvironment();
            if (!string.IsNullOrWhiteSpace(envSid)) s.SteamUserId64 = envSid.Trim();
            var envRaUser = FindRetroAchievementsUsernameInEnvironment();
            if (!string.IsNullOrWhiteSpace(envRaUser)) s.RetroAchievementsUsername = envRaUser.Trim();

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
                "starred_export_folder=" + (state.StarredExportFolder ?? string.Empty),
                "library_index_anchor=" + anchorOut,
                "exiftool=" + (state.ExifToolPath ?? string.Empty),
                "ffmpeg=" + (state.FfmpegPath ?? string.Empty),
                "steamgriddb_token=" + (state.SteamGridDbApiToken ?? string.Empty),
                "steam_web_api_key=" + (state.SteamWebApiKey ?? string.Empty),
                "retroachievements_api_key=" + (state.RetroAchievementsApiKey ?? string.Empty),
                "steam_user_id_64=" + (state.SteamUserId64 ?? string.Empty),
                "retroachievements_username=" + (state.RetroAchievementsUsername ?? string.Empty),
                "library_folder_tile_size=" + NormalizeLibraryFolderTileSize(state.LibraryFolderTileSize),
                "library_photo_tile_size=" + NormalizeLibraryPhotoTileSize(state.LibraryPhotoTileSize),
                "library_folder_grid_columns=" + NormalizeLibraryFolderGridColumnCount(state.LibraryFolderGridColumnCount),
                "library_photo_grid_columns=" + NormalizeLibraryPhotoGridColumnCount(state.LibraryPhotoGridColumnCount),
                "library_photo_rail_folder_tile_size=" + NormalizeLibraryFolderTileSize(state.LibraryPhotoRailFolderTileSize),
                "library_photo_rail_folder_sort_mode=" + NormalizeLibraryFolderSortMode(state.LibraryPhotoRailFolderSortMode),
                "library_photo_rail_folder_filter_mode=" + NormalizeLibraryFolderFilterMode(state.LibraryPhotoRailFolderFilterMode),
                "library_photo_rail_folder_grid_columns=" + NormalizeLibraryPhotoRailFolderGridColumnCount(state.LibraryPhotoRailFolderGridColumnCount),
                "library_folder_sort_mode=" + NormalizeLibraryFolderSortMode(state.LibraryFolderSortMode),
                "library_grouping_mode=" + NormalizeLibraryGroupingMode(state.LibraryGroupingMode),
                "library_folder_filter_mode=" + NormalizeLibraryFolderFilterMode(state.LibraryFolderFilterMode),
                "library_browser_search=" + (state.LibraryBrowserSearchText ?? string.Empty).Replace("\r", " ").Replace("\n", " "),
                "library_browser_last_view_key=" + (state.LibraryBrowserLastViewKey ?? string.Empty).Replace("\r", " ").Replace("\n", " "),
                "library_browser_folder_scroll=" + Math.Max(0, state.LibraryBrowserFolderScroll).ToString(CultureInfo.InvariantCulture),
                "library_browser_detail_scroll=" + Math.Max(0, state.LibraryBrowserDetailScroll).ToString(CultureInfo.InvariantCulture),
                "library_browser_folder_pane_width=" + Math.Max(0, state.LibraryBrowserFolderPaneWidth).ToString(CultureInfo.InvariantCulture),
                "manual_metadata_recent_titles=" + (state.ManualMetadataRecentTitleLabels ?? string.Empty).Replace("\r", " ").Replace("\n", " "),
                "troubleshooting_logging_enabled=" + (state.TroubleshootingLoggingEnabled ? "1" : "0"),
                "troubleshooting_log_redact_paths=" + (state.TroubleshootingLogRedactPaths ? "1" : "0"),
                "library_double_click_set_folder_cover=" + (state.LibraryDoubleClickSetsFolderCover ? "1" : "0"),
                "library_refresh_hero_banner_cache_on_next_open=" + (state.LibraryRefreshHeroBannerCacheOnNextLibraryOpen ? "1" : "0")
            });
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
    }
}
