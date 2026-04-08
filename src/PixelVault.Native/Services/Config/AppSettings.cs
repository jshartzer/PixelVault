namespace PixelVaultNative
{
    /// <summary>Values persisted in PixelVault.settings.ini (and runtime defaults before merge).</summary>
    public sealed class AppSettings
    {
        public string SourceRootsSerialized = string.Empty;
        public string DestinationRoot = string.Empty;
        public string LibraryRoot = string.Empty;
        /// <summary>Optional folder for on-demand “Export Starred” copies (library metadata index). Mirrors paths relative to the library root; overwrites existing files.</summary>
        public string StarredExportFolder = string.Empty;

        /// <summary>Snapshot path used to detect library-folder changes: each folder gets its own index SQLite under the app cache.</summary>
        public string LibraryIndexAnchor = string.Empty;
        public string ExifToolPath = string.Empty;
        public string FfmpegPath = string.Empty;
        public string SteamGridDbApiToken = string.Empty;
        /// <summary>Valve Steam Web API key (optional; for future Steam Web API features).</summary>
        public string SteamWebApiKey = string.Empty;
        /// <summary>RetroAchievements.org API key (optional; for future integration).</summary>
        public string RetroAchievementsApiKey = string.Empty;
        /// <summary>Your SteamID64 for Steam Web API calls that need a profile (e.g. achievement unlocks). Optional.</summary>
        public string SteamUserId64 = string.Empty;
        /// <summary>RetroAchievements site username for per-game unlock progress. Optional.</summary>
        public string RetroAchievementsUsername = string.Empty;
        public int LibraryFolderTileSize = 300;
        /// <summary>Preferred capture tile width (px) for non-timeline library detail grids; clamped to the viewport (<c>PV-PLN-LIBWS-001</c> Step 6).</summary>
        public int LibraryPhotoTileSize = 340;
        /// <summary>0 = auto folder columns; 1–12 = fixed column count (clamped to viewport).</summary>
        public int LibraryFolderGridColumnCount;
        /// <summary>0 = auto capture columns; 1–8 = fixed (non-timeline).</summary>
        public int LibraryPhotoGridColumnCount;
        /// <summary>Captures (Photo) workspace slim rail: cover tile size, independent from main folder grid.</summary>
        public int LibraryPhotoRailFolderTileSize = 200;
        public string LibraryPhotoRailFolderSortMode = "alpha";
        public string LibraryPhotoRailFolderFilterMode = "all";
        /// <summary>0 = auto (max 2 cols); 1–2 = fixed for the rail.</summary>
        public int LibraryPhotoRailFolderGridColumnCount;
        public string LibraryFolderSortMode = "alpha";
        public string LibraryFolderFilterMode = "all";
        public string LibraryGroupingMode = "all";
        /// <summary>Committed library search box text (persists across sessions).</summary>
        public string LibraryBrowserSearchText = string.Empty;
        /// <summary><see cref="LibraryBrowserFolderView.ViewKey"/> of last selected folder.</summary>
        public string LibraryBrowserLastViewKey = string.Empty;
        public double LibraryBrowserFolderScroll;
        public double LibraryBrowserDetailScroll;
        /// <summary>Library folder pane width in pixels when using a fixed split; 0 = default ~⅓ / ~⅔ star layout.</summary>
        public double LibraryBrowserFolderPaneWidth;
        /// <summary>Pipe-separated recent game title choice labels for manual metadata ComboBox (most recent first).</summary>
        public string ManualMetadataRecentTitleLabels = string.Empty;
        public bool TroubleshootingLoggingEnabled;
        /// <summary>When true, troubleshooting log encodes folder paths as <c>.../LastSegment</c> to limit disclosure.</summary>
        public bool TroubleshootingLogRedactPaths;
        /// <summary>When true, double-click a still image in the library detail grid (or use its context menu) to set that file as the folder custom cover.</summary>
        public bool LibraryDoubleClickSetsFolderCover;
        /// <summary>When true, the next successful library folder load clears auto-cached hero banner files (<c>hero-*</c>) for loaded titles so captures view can re-download with the current SteamGridDB/Steam pipeline. Custom banners are untouched.</summary>
        public bool LibraryRefreshHeroBannerCacheOnNextLibraryOpen;

        public static AppSettings Clone(AppSettings s)
        {
            if (s == null) return new AppSettings();
            return new AppSettings
            {
                SourceRootsSerialized = s.SourceRootsSerialized ?? string.Empty,
                DestinationRoot = s.DestinationRoot ?? string.Empty,
                LibraryRoot = s.LibraryRoot ?? string.Empty,
                StarredExportFolder = s.StarredExportFolder ?? string.Empty,
                LibraryIndexAnchor = s.LibraryIndexAnchor ?? string.Empty,
                ExifToolPath = s.ExifToolPath ?? string.Empty,
                FfmpegPath = s.FfmpegPath ?? string.Empty,
                SteamGridDbApiToken = s.SteamGridDbApiToken ?? string.Empty,
                SteamWebApiKey = s.SteamWebApiKey ?? string.Empty,
                RetroAchievementsApiKey = s.RetroAchievementsApiKey ?? string.Empty,
                SteamUserId64 = s.SteamUserId64 ?? string.Empty,
                RetroAchievementsUsername = s.RetroAchievementsUsername ?? string.Empty,
                LibraryFolderTileSize = s.LibraryFolderTileSize,
                LibraryPhotoTileSize = s.LibraryPhotoTileSize,
                LibraryFolderGridColumnCount = s.LibraryFolderGridColumnCount,
                LibraryPhotoGridColumnCount = s.LibraryPhotoGridColumnCount,
                LibraryPhotoRailFolderTileSize = s.LibraryPhotoRailFolderTileSize,
                LibraryPhotoRailFolderSortMode = s.LibraryPhotoRailFolderSortMode ?? "alpha",
                LibraryPhotoRailFolderFilterMode = s.LibraryPhotoRailFolderFilterMode ?? "all",
                LibraryPhotoRailFolderGridColumnCount = s.LibraryPhotoRailFolderGridColumnCount,
                LibraryFolderSortMode = s.LibraryFolderSortMode ?? "alpha",
                LibraryFolderFilterMode = s.LibraryFolderFilterMode ?? "all",
                LibraryGroupingMode = s.LibraryGroupingMode ?? "all",
                LibraryBrowserSearchText = s.LibraryBrowserSearchText ?? string.Empty,
                LibraryBrowserLastViewKey = s.LibraryBrowserLastViewKey ?? string.Empty,
                LibraryBrowserFolderScroll = s.LibraryBrowserFolderScroll,
                LibraryBrowserDetailScroll = s.LibraryBrowserDetailScroll,
                LibraryBrowserFolderPaneWidth = s.LibraryBrowserFolderPaneWidth,
                ManualMetadataRecentTitleLabels = s.ManualMetadataRecentTitleLabels ?? string.Empty,
                TroubleshootingLoggingEnabled = s.TroubleshootingLoggingEnabled,
                TroubleshootingLogRedactPaths = s.TroubleshootingLogRedactPaths,
                LibraryDoubleClickSetsFolderCover = s.LibraryDoubleClickSetsFolderCover,
                LibraryRefreshHeroBannerCacheOnNextLibraryOpen = s.LibraryRefreshHeroBannerCacheOnNextLibraryOpen
            };
        }
    }
}
