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
                GetLogBox = () => logBox
            };
        }

        void ShowSettingsWindow()
        {
            SettingsShell.ShowMainSettingsDialog();
        }

        void ShowPathSettingsWindow()
        {
            SettingsShell.ShowPathSettingsDialog();
        }
    }
}
