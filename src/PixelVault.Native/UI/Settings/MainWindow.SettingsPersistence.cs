using System;
using System.IO;
using System.Windows;

namespace PixelVaultNative
{
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
                ExifToolPath = exifToolPath ?? string.Empty,
                FfmpegPath = ffmpegPath ?? string.Empty,
                SteamGridDbApiToken = steamGridDbApiToken ?? string.Empty,
                SteamWebApiKey = steamWebApiKey ?? string.Empty,
                RetroAchievementsApiKey = retroAchievementsApiKey ?? string.Empty,
                LibraryFolderTileSize = libraryFolderTileSize,
                LibraryPhotoTileSize = libraryPhotoTileSize,
                LibraryFolderGridColumnCount = libraryFolderGridColumnCount,
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
                LibraryDoubleClickSetsFolderCover = libraryDoubleClickSetsFolderCover
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
            steamGridDbApiToken = s.SteamGridDbApiToken ?? string.Empty;
            steamWebApiKey = s.SteamWebApiKey ?? string.Empty;
            retroAchievementsApiKey = s.RetroAchievementsApiKey ?? string.Empty;
            libraryFolderTileSize = s.LibraryFolderTileSize;
            libraryPhotoTileSize = s.LibraryPhotoTileSize;
            libraryFolderGridColumnCount = s.LibraryFolderGridColumnCount;
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
        }

        void LoadSettings()
        {
            var merged = settingsService.LoadFromIni(
                settingsPath,
                CaptureAppSettings(),
                appRoot,
                () => FindExecutableOnPath("ffmpeg.exe") ?? string.Empty,
                SettingsService.FindSteamGridDbApiTokenInEnvironment);
            ApplyAppSettings(merged);
            NotifyIfLibraryIndexScopeChanged();
        }

        /// <summary>Library index DB file names are derived from the library path; changing folders swaps to a different SQLite without migrating.</summary>
        void NotifyIfLibraryIndexScopeChanged()
        {
            if (string.IsNullOrWhiteSpace(libraryIndexAnchor)) return;
            if (PathsEqualForLibraryRoot(libraryRoot, libraryIndexAnchor)) return;
            var msg =
                "The library folder in settings is different from the folder your index snapshot is tied to."
                + Environment.NewLine + Environment.NewLine
                + "PixelVault stores the game index, photo index, and related caches **per library path** (separate pixelvault-index-*.sqlite files under your PixelVault data cache). "
                + "Pointing at another folder can look like an empty library until you point back at the original folder."
                + Environment.NewLine + Environment.NewLine
                + "Index snapshot path: " + libraryIndexAnchor + Environment.NewLine
                + "Current library path: " + (libraryRoot ?? string.Empty);
            Log("Library folder path differs from library_index_anchor — per-path index scope. " + libraryIndexAnchor + " vs " + (libraryRoot ?? string.Empty));
            try
            {
                TryLibraryToast(msg, MessageBoxImage.Information);
            }
            catch
            {
            }
        }

        static bool PathsEqualForLibraryRoot(string a, string b)
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

        void SaveSettings()
        {
            settingsService.SaveToIni(settingsPath, CaptureAppSettings());
        }
    }
}
