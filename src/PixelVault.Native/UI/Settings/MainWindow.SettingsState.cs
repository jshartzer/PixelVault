using System;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-EXT-002 A.2: maps between <see cref="MainWindow"/> fields and <see cref="AppSettings"/>.
    /// All reads/writes of <c>PixelVault.settings.ini</c> go through <see cref="ISettingsService"/> from <see cref="MainWindow.SettingsPersistence"/>.
    /// </summary>
    public sealed partial class MainWindow
    {
        AppSettings CaptureAppSettings()
        {
            return new AppSettings
            {
                SourceRootsSerialized = sourceRoot ?? string.Empty,
                DestinationRoot = destinationRoot ?? string.Empty,
                LibraryRoot = libraryRoot ?? string.Empty,
                StarredExportFolder = starredExportFolder ?? string.Empty,
                LibraryIndexAnchor = libraryIndexAnchor ?? string.Empty,
                ExifToolPath = exifToolPath ?? string.Empty,
                FfmpegPath = ffmpegPath ?? string.Empty,
                ImportSearchSubfoldersForRename = importSearchSubfoldersForRename,
                SteamGridDbApiToken = steamGridDbApiToken ?? string.Empty,
                SteamWebApiKey = steamWebApiKey ?? string.Empty,
                RetroAchievementsApiKey = retroAchievementsApiKey ?? string.Empty,
                SteamUserId64 = steamUserId64 ?? string.Empty,
                RetroAchievementsUsername = retroAchievementsUsername ?? string.Empty,
                LibraryFolderTileSize = libraryFolderTileSize,
                LibraryPhotoTileSize = libraryPhotoTileSize,
                LibraryFolderGridColumnCount = libraryFolderGridColumnCount,
                LibraryFolderFillPaneWidth = libraryFolderFillPaneWidth,
                LibraryPhotoGridColumnCount = libraryPhotoGridColumnCount,
                LibraryPhotoRailFolderTileSize = libraryPhotoRailFolderTileSize,
                LibraryPhotoRailFolderSortMode = libraryPhotoRailFolderSortMode ?? "alpha",
                LibraryPhotoRailFolderFilterMode = libraryPhotoRailFolderFilterMode ?? "all",
                LibraryPhotoRailFolderGridColumnCount = libraryPhotoRailFolderGridColumnCount,
                LibraryFolderSortMode = libraryFolderSortMode ?? "alpha",
                LibraryFolderFilterMode = libraryFolderFilterMode ?? "all",
                LibraryGroupingMode = libraryGroupingMode ?? "all",
                LibraryBrowserSearchText = _libraryBrowserPersistedSearch ?? string.Empty,
                LibraryBrowserLastViewKey = _libraryBrowserPersistedLastViewKey ?? string.Empty,
                LibraryBrowserFolderScroll = Math.Max(0, _libraryBrowserPersistedFolderScroll),
                LibraryBrowserDetailScroll = Math.Max(0, _libraryBrowserPersistedDetailScroll),
                LibraryBrowserFolderPaneWidth = Math.Max(0, _libraryBrowserPersistedFolderPaneWidth),
                ManualMetadataRecentTitleLabels = _manualMetadataRecentTitleLabelsSerialized ?? string.Empty,
                TroubleshootingLoggingEnabled = troubleshootingLoggingEnabled,
                TroubleshootingLogRedactPaths = troubleshootingLogRedactPaths,
                LibraryDoubleClickSetsFolderCover = libraryDoubleClickSetsFolderCover,
                LibraryRefreshHeroBannerCacheOnNextLibraryOpen = libraryRefreshHeroBannerCacheOnNextLibraryOpen,
                BackgroundAutoIntakeEnabled = backgroundAutoIntakeEnabled,
                BackgroundAutoIntakeQuietSeconds = backgroundAutoIntakeQuietSeconds,
                BackgroundAutoIntakeToastsEnabled = backgroundAutoIntakeToastsEnabled,
                BackgroundAutoIntakeShowSummary = backgroundAutoIntakeShowSummary,
                BackgroundAutoIntakeVerboseLogging = backgroundAutoIntakeVerboseLogging,
                SystemTrayMinimizeEnabled = systemTrayMinimizeEnabled,
                SystemTrayPromptOnCloseEnabled = systemTrayPromptOnCloseEnabled
            };
        }

        void ApplyAppSettings(AppSettings s)
        {
            if (s == null) return;
            sourceRoot = s.SourceRootsSerialized ?? string.Empty;
            destinationRoot = s.DestinationRoot ?? string.Empty;
            libraryRoot = s.LibraryRoot ?? string.Empty;
            starredExportFolder = s.StarredExportFolder ?? string.Empty;
            libraryIndexAnchor = s.LibraryIndexAnchor ?? string.Empty;
            exifToolPath = s.ExifToolPath ?? string.Empty;
            ffmpegPath = s.FfmpegPath ?? string.Empty;
            importSearchSubfoldersForRename = s.ImportSearchSubfoldersForRename;
            steamGridDbApiToken = s.SteamGridDbApiToken ?? string.Empty;
            steamWebApiKey = s.SteamWebApiKey ?? string.Empty;
            retroAchievementsApiKey = s.RetroAchievementsApiKey ?? string.Empty;
            steamUserId64 = s.SteamUserId64 ?? string.Empty;
            retroAchievementsUsername = s.RetroAchievementsUsername ?? string.Empty;
            libraryFolderTileSize = s.LibraryFolderTileSize;
            libraryPhotoTileSize = s.LibraryPhotoTileSize;
            libraryFolderGridColumnCount = s.LibraryFolderGridColumnCount;
            libraryFolderFillPaneWidth = s.LibraryFolderFillPaneWidth;
            libraryPhotoGridColumnCount = s.LibraryPhotoGridColumnCount;
            libraryPhotoRailFolderTileSize = s.LibraryPhotoRailFolderTileSize;
            libraryPhotoRailFolderSortMode = s.LibraryPhotoRailFolderSortMode ?? "alpha";
            libraryPhotoRailFolderFilterMode = s.LibraryPhotoRailFolderFilterMode ?? "all";
            libraryPhotoRailFolderGridColumnCount = s.LibraryPhotoRailFolderGridColumnCount;
            libraryFolderSortMode = s.LibraryFolderSortMode ?? "alpha";
            libraryFolderFilterMode = s.LibraryFolderFilterMode ?? "all";
            libraryGroupingMode = s.LibraryGroupingMode ?? "all";
            _libraryBrowserPersistedSearch = s.LibraryBrowserSearchText ?? string.Empty;
            _libraryBrowserPersistedLastViewKey = s.LibraryBrowserLastViewKey ?? string.Empty;
            _libraryBrowserPersistedFolderScroll = Math.Max(0, s.LibraryBrowserFolderScroll);
            _libraryBrowserPersistedDetailScroll = Math.Max(0, s.LibraryBrowserDetailScroll);
            _libraryBrowserPersistedFolderPaneWidth = Math.Max(0, s.LibraryBrowserFolderPaneWidth);
            _manualMetadataRecentTitleLabelsSerialized = s.ManualMetadataRecentTitleLabels ?? string.Empty;
            troubleshootingLoggingEnabled = s.TroubleshootingLoggingEnabled;
            troubleshootingLogRedactPaths = s.TroubleshootingLogRedactPaths;
            libraryDoubleClickSetsFolderCover = s.LibraryDoubleClickSetsFolderCover;
            libraryRefreshHeroBannerCacheOnNextLibraryOpen = s.LibraryRefreshHeroBannerCacheOnNextLibraryOpen;
            backgroundAutoIntakeEnabled = s.BackgroundAutoIntakeEnabled;
            backgroundAutoIntakeQuietSeconds = SettingsService.NormalizeBackgroundAutoIntakeQuietSeconds(s.BackgroundAutoIntakeQuietSeconds);
            backgroundAutoIntakeToastsEnabled = s.BackgroundAutoIntakeToastsEnabled;
            backgroundAutoIntakeShowSummary = s.BackgroundAutoIntakeShowSummary;
            backgroundAutoIntakeVerboseLogging = s.BackgroundAutoIntakeVerboseLogging;
            systemTrayMinimizeEnabled = s.SystemTrayMinimizeEnabled;
            systemTrayPromptOnCloseEnabled = s.SystemTrayPromptOnCloseEnabled;
        }
    }
}
