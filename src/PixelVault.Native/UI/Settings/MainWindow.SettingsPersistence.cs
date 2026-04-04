using System;

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
                ExifToolPath = exifToolPath ?? string.Empty,
                FfmpegPath = ffmpegPath ?? string.Empty,
                SteamGridDbApiToken = steamGridDbApiToken ?? string.Empty,
                LibraryFolderTileSize = libraryFolderTileSize,
                LibraryFolderSortMode = libraryFolderSortMode ?? "platform",
                LibraryGroupingMode = libraryGroupingMode ?? "all",
                LibraryBrowserSearchText = _libraryBrowserPersistedSearch ?? string.Empty,
                LibraryBrowserLastViewKey = _libraryBrowserPersistedLastViewKey ?? string.Empty,
                LibraryBrowserFolderScroll = Math.Max(0, _libraryBrowserPersistedFolderScroll),
                LibraryBrowserDetailScroll = Math.Max(0, _libraryBrowserPersistedDetailScroll),
                TroubleshootingLoggingEnabled = troubleshootingLoggingEnabled,
                TroubleshootingLogRedactPaths = troubleshootingLogRedactPaths
            };
        }

        void ApplyAppSettings(AppSettings s)
        {
            if (s == null) return;
            sourceRoot = s.SourceRootsSerialized ?? string.Empty;
            destinationRoot = s.DestinationRoot ?? string.Empty;
            libraryRoot = s.LibraryRoot ?? string.Empty;
            exifToolPath = s.ExifToolPath ?? string.Empty;
            ffmpegPath = s.FfmpegPath ?? string.Empty;
            steamGridDbApiToken = s.SteamGridDbApiToken ?? string.Empty;
            libraryFolderTileSize = s.LibraryFolderTileSize;
            libraryFolderSortMode = s.LibraryFolderSortMode ?? "platform";
            libraryGroupingMode = s.LibraryGroupingMode ?? "all";
            _libraryBrowserPersistedSearch = s.LibraryBrowserSearchText ?? string.Empty;
            _libraryBrowserPersistedLastViewKey = s.LibraryBrowserLastViewKey ?? string.Empty;
            _libraryBrowserPersistedFolderScroll = Math.Max(0, s.LibraryBrowserFolderScroll);
            _libraryBrowserPersistedDetailScroll = Math.Max(0, s.LibraryBrowserDetailScroll);
            troubleshootingLoggingEnabled = s.TroubleshootingLoggingEnabled;
            troubleshootingLogRedactPaths = s.TroubleshootingLogRedactPaths;
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
        }

        void SaveSettings()
        {
            settingsService.SaveToIni(settingsPath, CaptureAppSettings());
        }
    }
}
