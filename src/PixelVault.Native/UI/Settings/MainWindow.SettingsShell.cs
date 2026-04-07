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
                ShowPhotographyGallery = ShowPhotographyGallery,
                SourceRootsSummary = SourceRootsSummary,
                GetDestinationRoot = () => destinationRoot,
                GetLibraryRoot = () => libraryRoot,
                GetStarredExportFolder = () => starredExportFolder ?? string.Empty,
                GetLibraryWorkspaceRoot = () => libraryWorkspace.LibraryRoot,
                GetSavedCoversRoot = () => savedCoversRoot,
                GetExifToolPath = () => exifToolPath,
                GetFfmpegPath = () => ffmpegPath,
                GetSteamGridDbApiToken = () => steamGridDbApiToken,
                HasSteamGridDbApiToken = HasSteamGridDbApiToken,
                GetTroubleshootingLoggingEnabled = () => troubleshootingLoggingEnabled,
                SetTroubleshootingLoggingEnabled = v => troubleshootingLoggingEnabled = v,
                GetTroubleshootingLogRedactPaths = () => troubleshootingLogRedactPaths,
                SetTroubleshootingLogRedactPaths = v => troubleshootingLogRedactPaths = v,
                GetLibraryDoubleClickSetsFolderCover = () => libraryDoubleClickSetsFolderCover,
                SetLibraryDoubleClickSetsFolderCover = v => libraryDoubleClickSetsFolderCover = v,
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
                SetSteamGridDbApiToken = v => steamGridDbApiToken = v,
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
                GetActiveLibraryIndexDatabasePath = () => string.IsNullOrWhiteSpace(libraryRoot) ? string.Empty : IndexDatabasePath(libraryRoot),
                GetDiagnosticsSessionId = () => _diagnosticsSessionId,
                PromptFetchCoversForLibrary = PromptFetchCoversForLibraryFromSettings
            };
        }

        void PromptFetchCoversForLibraryFromSettings(Window owner)
        {
            var run = activeLibraryFullCoverRefresh;
            var o = owner ?? this;
            if (run == null)
            {
                MessageBox.Show(
                    o,
                    "Open the Library window first to refresh covers for the whole library.",
                    "PixelVault",
                    MessageBoxButton.OK,
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
