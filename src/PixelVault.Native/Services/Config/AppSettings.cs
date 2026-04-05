namespace PixelVaultNative
{
    /// <summary>Values persisted in PixelVault.settings.ini (and runtime defaults before merge).</summary>
    public sealed class AppSettings
    {
        public string SourceRootsSerialized = string.Empty;
        public string DestinationRoot = string.Empty;
        public string LibraryRoot = string.Empty;

        /// <summary>Snapshot path used to detect library-folder changes: each folder gets its own index SQLite under the app cache.</summary>
        public string LibraryIndexAnchor = string.Empty;
        public string ExifToolPath = string.Empty;
        public string FfmpegPath = string.Empty;
        public string SteamGridDbApiToken = string.Empty;
        public int LibraryFolderTileSize = 300;
        public string LibraryFolderSortMode = "platform";
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

        public static AppSettings Clone(AppSettings s)
        {
            if (s == null) return new AppSettings();
            return new AppSettings
            {
                SourceRootsSerialized = s.SourceRootsSerialized ?? string.Empty,
                DestinationRoot = s.DestinationRoot ?? string.Empty,
                LibraryRoot = s.LibraryRoot ?? string.Empty,
                LibraryIndexAnchor = s.LibraryIndexAnchor ?? string.Empty,
                ExifToolPath = s.ExifToolPath ?? string.Empty,
                FfmpegPath = s.FfmpegPath ?? string.Empty,
                SteamGridDbApiToken = s.SteamGridDbApiToken ?? string.Empty,
                LibraryFolderTileSize = s.LibraryFolderTileSize,
                LibraryFolderSortMode = s.LibraryFolderSortMode ?? "platform",
                LibraryGroupingMode = s.LibraryGroupingMode ?? "all",
                LibraryBrowserSearchText = s.LibraryBrowserSearchText ?? string.Empty,
                LibraryBrowserLastViewKey = s.LibraryBrowserLastViewKey ?? string.Empty,
                LibraryBrowserFolderScroll = s.LibraryBrowserFolderScroll,
                LibraryBrowserDetailScroll = s.LibraryBrowserDetailScroll,
                LibraryBrowserFolderPaneWidth = s.LibraryBrowserFolderPaneWidth,
                ManualMetadataRecentTitleLabels = s.ManualMetadataRecentTitleLabels ?? string.Empty,
                TroubleshootingLoggingEnabled = s.TroubleshootingLoggingEnabled,
                TroubleshootingLogRedactPaths = s.TroubleshootingLogRedactPaths,
                LibraryDoubleClickSetsFolderCover = s.LibraryDoubleClickSetsFolderCover
            };
        }
    }
}
