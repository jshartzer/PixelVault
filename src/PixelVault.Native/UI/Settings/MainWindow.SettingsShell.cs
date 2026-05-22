using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        SettingsShellHost _settingsShellHost;

        SettingsShellHost SettingsShell
        {
            get
            {
                if (_settingsShellHost == null)
                {
                    var deps = BuildSettingsShellDependencies();
                    _settingsShellHost = new SettingsShellHost(deps);
                    deps.OpenPathSettingsDialog = () => _settingsShellHost.ShowPathSettingsDialog();
                }
                return _settingsShellHost;
            }
        }

        SettingsShellDependencies BuildSettingsShellDependencies()
        {
            return new SettingsShellDependencies
            {
                OwnerWindow = this,
                AppVersion = AppVersion,
                ChangelogPath = changelogPath,
                LogsRoot = logsRoot,
                Brush = Brush,
                Btn = Btn,
                OpenFolder = OpenFolder,
                OpenSavedCoversFolder = OpenSavedCoversFolder,
                OpenGameIndexEditor = OpenGameIndexEditor,
                OpenPhotoIndexEditor = OpenPhotoIndexEditor,
                OpenFilenameConventionEditor = OpenFilenameConventionEditor,
                OpenLibraryStorageMergeTool = owner => OpenLibraryStorageMergeTool(owner ?? this),
                ShowPhotographyGallery = ShowPhotographyGallery,
                ExportStarredLibraryCaptures = owner => ExportStarredLibraryCapturesToFolder(owner ?? this),
                SourceRootsSummary = SourceRootsSummary,
                GetDestinationRoot = () => destinationRoot,
                GetLibraryRoot = () => libraryRoot,
                GetStarredExportFolder = () => starredExportFolder ?? string.Empty,
                GetLibraryWorkspaceRoot = () => libraryWorkspace.LibraryRoot,
                GetSavedCoversRoot = () => savedCoversRoot,
                GetExifToolPath = () => exifToolPath,
                GetFfmpegPath = () => ffmpegPath,
                GetImportSearchSubfoldersForRename = () => importSearchSubfoldersForRename,
                GetSteamGridDbApiToken = () => steamGridDbApiToken,
                HasSteamGridDbApiToken = HasSteamGridDbApiToken,
                GetSteamWebApiKey = () => steamWebApiKey ?? string.Empty,
                HasSteamWebApiKey = HasSteamWebApiKey,
                GetRetroAchievementsApiKey = () => retroAchievementsApiKey ?? string.Empty,
                HasRetroAchievementsApiKey = HasRetroAchievementsApiKey,
                GetIgdbTwitchClientId = () => igdbTwitchClientId ?? string.Empty,
                GetIgdbTwitchClientSecret = () => igdbTwitchClientSecret ?? string.Empty,
                HasIgdbCredentials = HasIgdbCredentials,
                ProbeIgdbFieldsAsync = ProbeIgdbFieldsAsync,
                RefreshIgdbMetadataAsync = RefreshIgdbMetadataCacheAsync,
                GetTroubleshootingLoggingEnabled = () => troubleshootingLoggingEnabled,
                SetTroubleshootingLoggingEnabled = v => troubleshootingLoggingEnabled = v,
                GetTroubleshootingLogRedactPaths = () => troubleshootingLogRedactPaths,
                SetTroubleshootingLogRedactPaths = v => troubleshootingLogRedactPaths = v,
                GetLibraryDoubleClickSetsFolderCover = () => libraryDoubleClickSetsFolderCover,
                SetLibraryDoubleClickSetsFolderCover = v => libraryDoubleClickSetsFolderCover = v,
                GetLibraryRefreshHeroBannerCacheOnNextLibraryOpen = () => libraryRefreshHeroBannerCacheOnNextLibraryOpen,
                SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen = v => libraryRefreshHeroBannerCacheOnNextLibraryOpen = v,
                GetBackgroundAutoIntakeEnabled = () => backgroundAutoIntakeEnabled,
                SetBackgroundAutoIntakeEnabled = v => backgroundAutoIntakeEnabled = v,
                GetBackgroundAutoIntakeQuietSeconds = () => backgroundAutoIntakeQuietSeconds,
                SetBackgroundAutoIntakeQuietSeconds = v => backgroundAutoIntakeQuietSeconds = SettingsService.NormalizeBackgroundAutoIntakeQuietSeconds(v),
                GetBackgroundAutoIntakeToastsEnabled = () => backgroundAutoIntakeToastsEnabled,
                SetBackgroundAutoIntakeToastsEnabled = v => backgroundAutoIntakeToastsEnabled = v,
                GetBackgroundAutoIntakeShowSummary = () => backgroundAutoIntakeShowSummary,
                SetBackgroundAutoIntakeShowSummary = v => backgroundAutoIntakeShowSummary = v,
                GetBackgroundAutoIntakeVerboseLogging = () => backgroundAutoIntakeVerboseLogging,
                SetBackgroundAutoIntakeVerboseLogging = v => backgroundAutoIntakeVerboseLogging = v,
                GetSystemTrayMinimizeEnabled = () => systemTrayMinimizeEnabled,
                SetSystemTrayMinimizeEnabled = v => systemTrayMinimizeEnabled = v,
                GetSystemTrayPromptOnCloseEnabled = () => systemTrayPromptOnCloseEnabled,
                SetSystemTrayPromptOnCloseEnabled = v => systemTrayPromptOnCloseEnabled = v,
                SaveSettings = SaveSettings,
                Log = Log,
                LogTroubleshooting = LogTroubleshooting,
                LogFilePath = LogFilePath,
                TroubleshootingLogFilePath = TroubleshootingLogFilePath,
                PickFolder = PickFolder,
                PickFile = PickFile,
                SerializeSourceRoots = SerializeSourceRoots,
                SourceRootsEditorText = SourceRootsEditorText,
                PrimarySourceRoot = PrimarySourceRoot,
                AppendSourceRoot = AppendSourceRoot,
                SetSourceRoot = v => sourceRoot = v,
                SetDestinationRoot = v => destinationRoot = v,
                SetLibraryRoot = v => libraryRoot = v,
                SetStarredExportFolder = v => starredExportFolder = v ?? string.Empty,
                SetExifToolPath = v => exifToolPath = v,
                SetFfmpegPath = v => ffmpegPath = v,
                SetImportSearchSubfoldersForRename = v => importSearchSubfoldersForRename = v,
                SetSteamGridDbApiToken = v => steamGridDbApiToken = v,
                SetSteamWebApiKey = v => steamWebApiKey = v ?? string.Empty,
                SetRetroAchievementsApiKey = v => retroAchievementsApiKey = v ?? string.Empty,
                SetIgdbTwitchClientId = v => igdbTwitchClientId = v ?? string.Empty,
                SetIgdbTwitchClientSecret = v => igdbTwitchClientSecret = v ?? string.Empty,
                GetSteamUserId64 = () => steamUserId64 ?? string.Empty,
                SetSteamUserId64 = v => steamUserId64 = v ?? string.Empty,
                GetRetroAchievementsUsername = () => retroAchievementsUsername ?? string.Empty,
                SetRetroAchievementsUsername = v => retroAchievementsUsername = v ?? string.Empty,
                ClearFailedFfmpegPosterKeys = () => failedFfmpegPosterKeys.Clear(),
                RefreshMainUi = RefreshMainUi,
                SyncIncludeGameCaptureKeywordsMirror = SyncIncludeGameCaptureKeywordsMirror,
                LoadLogView = LoadLogView,
                SetStatusLine = v => status = v,
                SetLogBox = v => logBox = v,
                GetStatusLine = () => status,
                GetLogBox = () => logBox,
                GetConfiguredSourceRoots = () => GetSourceRoots(),
                GetCacheRoot = () => cacheRoot,
                GetPersistentDataRoot = () => dataRoot,
                GetActiveLibraryIndexDatabasePath = () => string.IsNullOrWhiteSpace(libraryRoot) ? string.Empty : IndexDatabasePath(libraryRoot),
                GetDiagnosticsSessionId = () => _diagnosticsSessionId,
                GetLibraryStoragePlacementHealth = BuildLibraryStoragePlacementHealthSnapshot,
                PlacementMoveMisplacedCapturesToCanonical = () => PlacementMoveMisplacedCapturesToCanonical(),
                PlacementClearOrphanPhotoGameIds = () => PlacementClearOrphanPhotoGameIds(),
                PlacementTryAlignGameIndexFoldersToCanonical = () => PlacementTryAlignGameIndexFoldersToCanonical(),
                PromptFetchCoversForLibrary = PromptFetchCoversForLibraryFromSettings
            };
        }

        void PromptFetchCoversForLibraryFromSettings(Window owner)
        {
            var run = activeLibraryFullCoverRefresh;
            var o = owner ?? this;
            if (run == null)
            {
                TryLibraryToast(
                    "Open the Library window first to refresh covers for the whole library.",
                    MessageBoxImage.Information);
                return;
            }

            var choice = MessageBox.Show(
                o,
                "Refresh cover art for the entire library?",
                "PixelVault",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (choice != MessageBoxResult.OK) return;
            run();
        }

        async Task<string> RefreshIgdbMetadataCacheAsync(string clientId, string clientSecret)
        {
            clientId = string.IsNullOrWhiteSpace(clientId) ? CurrentIgdbTwitchClientId() : clientId.Trim();
            clientSecret = string.IsNullOrWhiteSpace(clientSecret) ? CurrentIgdbTwitchClientSecret() : clientSecret.Trim();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                return "IGDB cache refresh skipped: add Twitch Client ID and Client Secret in Path Settings first.";
            if (string.IsNullOrWhiteSpace(libraryRoot))
                return "IGDB cache refresh skipped: set a Library folder in Path Settings first.";

            var rows = GetSavedGameIndexRowsForRoot(libraryRoot) ?? new List<GameIndexEditorRow>();
            rows = MergeGameIndexRows(rows ?? new List<GameIndexEditorRow>()) ?? new List<GameIndexEditorRow>();
            var candidates = rows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .ToList();
            if (candidates.Count == 0)
                return "IGDB cache refresh skipped: no saved game-index rows were found.";

            var service = new IgdbProbeService(AppVersion);
            var refreshed = 0;
            var unresolved = 0;
            var failed = 0;
            Log("IGDB cache refresh: " + candidates.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " games queued.");
            for (var i = 0; i < candidates.Count; i++)
            {
                var row = candidates[i];
                if (i == 0 || (i + 1) % 5 == 0)
                    Log("IGDB cache refresh: " + (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)
                        + "/" + candidates.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)
                        + " - " + (row.Name ?? string.Empty));
                try
                {
                    var metadata = await service.ResolveGameMetadataAsync(
                        clientId,
                        clientSecret,
                        row.IgdbId,
                        row.Name,
                        CancellationToken.None).ConfigureAwait(true);
                    if (metadata == null)
                    {
                        unresolved++;
                        continue;
                    }
                    ApplyIgdbMetadataToGameIndexRow(row, metadata);
                    refreshed++;
                    if (refreshed == 1 || refreshed % 10 == 0)
                    {
                        SaveGameIndexEditorRows(libraryRoot, rows);
                        Log("IGDB cache refresh: saved " + refreshed.ToString(System.Globalization.CultureInfo.CurrentCulture) + " updated games so far.");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    LogException("Refresh IGDB metadata cache | " + (row.Name ?? string.Empty), ex);
                    Log("IGDB cache refresh: failed " + (row.Name ?? string.Empty) + " - " + ex.Message);
                }
            }

            SaveGameIndexEditorRows(libraryRoot, rows);
            try { _libraryBrowserLiveWorkingSet?.RerenderFolderList?.Invoke(); }
            catch (Exception ex) { LogException("Refresh IGDB metadata cache | rerender", ex); }

            return "IGDB cache refresh complete."
                + Environment.NewLine + "Updated: " + refreshed.ToString(System.Globalization.CultureInfo.CurrentCulture)
                + Environment.NewLine + "No confident match: " + unresolved.ToString(System.Globalization.CultureInfo.CurrentCulture)
                + Environment.NewLine + "Failed: " + failed.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }

        static void ApplyIgdbMetadataToGameIndexRow(GameIndexEditorRow row, IgdbGameMetadata metadata)
        {
            if (row == null || metadata == null) return;
            row.IgdbId = metadata.Id ?? string.Empty;
            row.IgdbSlug = metadata.Slug ?? string.Empty;
            row.IgdbCollectionId = metadata.CollectionId ?? string.Empty;
            row.IgdbCollectionName = metadata.CollectionName ?? string.Empty;
            row.IgdbFranchiseId = metadata.FranchiseId ?? string.Empty;
            row.IgdbFranchiseName = metadata.FranchiseName ?? string.Empty;
            row.IgdbSummary = metadata.Summary ?? string.Empty;
            row.IgdbReleaseDate = metadata.ReleaseDate ?? string.Empty;
            row.IgdbGenres = metadata.Genres ?? string.Empty;
            row.IgdbPlatforms = metadata.Platforms ?? string.Empty;
            row.IgdbDeveloper = metadata.Developer ?? string.Empty;
            row.IgdbPublisher = metadata.Publisher ?? string.Empty;
            row.IgdbCoverImageId = metadata.CoverImageId ?? string.Empty;
            row.IgdbFetchedUtcTicks = DateTime.UtcNow.Ticks;
        }

        internal void ShowSettingsWindow()
        {
            SettingsShell.ShowMainSettingsDialog();
        }

        void ShowPathSettingsWindow()
        {
            SettingsShell.ShowPathSettingsDialog();
        }
    }
}
