using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    /// <summary>Callbacks and context for <see cref="SettingsShellHost"/> so settings modal UI lives outside <see cref="MainWindow"/> methods.</summary>
    public sealed class SettingsShellDependencies
    {
        public Window OwnerWindow { get; set; }
        public string AppVersion { get; set; }
        public string ChangelogPath { get; set; }
        public string LogsRoot { get; set; }

        public Func<string, SolidColorBrush> Brush { get; set; }
        public Func<string, RoutedEventHandler, string, Brush, Button> Btn { get; set; }

        public Action<string> OpenFolder { get; set; }
        public Action OpenSavedCoversFolder { get; set; }
        public Action OpenGameIndexEditor { get; set; }
        public Action OpenPhotoIndexEditor { get; set; }
        public Action OpenFilenameConventionEditor { get; set; }
        public Action<Window> ShowPhotographyGallery { get; set; }
        /// <summary>Library full cover refresh with confirmation; pass the active settings (or other) dialog as owner for modal dialogs.</summary>
        public Action<Window> PromptFetchCoversForLibrary { get; set; }

        public Func<string> SourceRootsSummary { get; set; }
        public Func<string> GetDestinationRoot { get; set; }
        public Func<string> GetLibraryRoot { get; set; }
        public Func<string> GetStarredExportFolder { get; set; }
        public Func<string> GetLibraryWorkspaceRoot { get; set; }
        public Func<string> GetSavedCoversRoot { get; set; }
        public Func<string> GetExifToolPath { get; set; }
        public Func<string> GetFfmpegPath { get; set; }
        public Func<string> GetSteamGridDbApiToken { get; set; }
        public Func<bool> HasSteamGridDbApiToken { get; set; }
        public Func<string> GetSteamWebApiKey { get; set; }
        public Func<bool> HasSteamWebApiKey { get; set; }
        public Func<string> GetRetroAchievementsApiKey { get; set; }
        public Func<bool> HasRetroAchievementsApiKey { get; set; }

        public Func<bool> GetTroubleshootingLoggingEnabled { get; set; }
        public Action<bool> SetTroubleshootingLoggingEnabled { get; set; }
        public Func<bool> GetTroubleshootingLogRedactPaths { get; set; }
        public Action<bool> SetTroubleshootingLogRedactPaths { get; set; }
        public Func<bool> GetLibraryDoubleClickSetsFolderCover { get; set; }
        public Action<bool> SetLibraryDoubleClickSetsFolderCover { get; set; }
        public Func<bool> GetLibraryRefreshHeroBannerCacheOnNextLibraryOpen { get; set; }
        public Action<bool> SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen { get; set; }

        public Action SaveSettings { get; set; }
        public Action<string> Log { get; set; }
        public Action<string, string> LogTroubleshooting { get; set; }
        public Func<string> LogFilePath { get; set; }
        public Func<string> TroubleshootingLogFilePath { get; set; }

        public Func<string, string> PickFolder { get; set; }
        public Func<string, string, string, string> PickFile { get; set; }
        public Func<string, string> SerializeSourceRoots { get; set; }
        public Func<string> SourceRootsEditorText { get; set; }
        public Func<string> PrimarySourceRoot { get; set; }
        public Func<string, string, string> AppendSourceRoot { get; set; }

        public Action<string> SetSourceRoot { get; set; }
        public Action<string> SetDestinationRoot { get; set; }
        public Action<string> SetLibraryRoot { get; set; }
        public Action<string> SetStarredExportFolder { get; set; }
        public Action<string> SetExifToolPath { get; set; }
        public Action<string> SetFfmpegPath { get; set; }
        public Action<string> SetSteamGridDbApiToken { get; set; }
        public Action<string> SetSteamWebApiKey { get; set; }
        public Action<string> SetRetroAchievementsApiKey { get; set; }
        public Func<string> GetSteamUserId64 { get; set; }
        public Action<string> SetSteamUserId64 { get; set; }
        public Func<string> GetRetroAchievementsUsername { get; set; }
        public Action<string> SetRetroAchievementsUsername { get; set; }
        public Action ClearFailedFfmpegPosterKeys { get; set; }

        public Action RefreshMainUi { get; set; }
        public Action SyncIncludeGameCaptureKeywordsMirror { get; set; }
        public Action LoadLogView { get; set; }

        public Action<TextBlock> SetStatusLine { get; set; }
        public Action<TextBox> SetLogBox { get; set; }
        public Func<TextBlock> GetStatusLine { get; set; }
        public Func<TextBox> GetLogBox { get; set; }

        /// <summary>Set after <see cref="SettingsShellHost"/> construction to open the path modal (avoids circular init).</summary>
        public Action OpenPathSettingsDialog { get; set; }

        /// <summary>Configured intake source folder paths (may be empty or missing on disk).</summary>
        public Func<IReadOnlyList<string>> GetConfiguredSourceRoots { get; set; }

        /// <summary>PixelVault data cache root (indexes, thumbnails).</summary>
        public Func<string> GetCacheRoot { get; set; }

        /// <summary>SQLite path for the active library root, or empty if no library.</summary>
        public Func<string> GetActiveLibraryIndexDatabasePath { get; set; }

        /// <summary>Fixed troubleshooting session id for this app instance.</summary>
        public Func<string> GetDiagnosticsSessionId { get; set; }
    }
}
