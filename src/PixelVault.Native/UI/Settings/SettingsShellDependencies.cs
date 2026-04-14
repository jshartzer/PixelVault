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
        /// <summary>Preview/apply merging captures into shared storage folders (storage groups). Owner is used for modal dialogs.</summary>
        public Action<Window> OpenLibraryStorageMergeTool { get; set; }
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

        public Func<bool> GetBackgroundAutoIntakeEnabled { get; set; }
        public Action<bool> SetBackgroundAutoIntakeEnabled { get; set; }
        public Func<int> GetBackgroundAutoIntakeQuietSeconds { get; set; }
        public Action<int> SetBackgroundAutoIntakeQuietSeconds { get; set; }
        public Func<bool> GetBackgroundAutoIntakeToastsEnabled { get; set; }
        public Action<bool> SetBackgroundAutoIntakeToastsEnabled { get; set; }
        public Func<bool> GetBackgroundAutoIntakeShowSummary { get; set; }
        public Action<bool> SetBackgroundAutoIntakeShowSummary { get; set; }
        public Func<bool> GetBackgroundAutoIntakeVerboseLogging { get; set; }
        public Action<bool> SetBackgroundAutoIntakeVerboseLogging { get; set; }
        public Func<bool> GetSystemTrayMinimizeEnabled { get; set; }
        public Action<bool> SetSystemTrayMinimizeEnabled { get; set; }
        public Func<bool> GetSystemTrayPromptOnCloseEnabled { get; set; }
        public Action<bool> SetSystemTrayPromptOnCloseEnabled { get; set; }

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

        /// <summary>Optional; game index rows + photo-index file paths vs canonical placement (LIBST Step 6).</summary>
        public Func<LibraryStoragePlacementHealthSnapshot> GetLibraryStoragePlacementHealth { get; set; }

        /// <summary>Move photo-index captures that sit outside the canonical folder for their GameId. Returns files moved, or -1 on failure.</summary>
        public Func<int> PlacementMoveMisplacedCapturesToCanonical { get; set; }

        /// <summary>Clear GameId on photo-index entries whose GameId has no game index row. Returns rows updated, or -1 on failure.</summary>
        public Func<int> PlacementClearOrphanPhotoGameIds { get; set; }

        /// <summary>Move files on each game index row into canonical folders and refresh paths. Returns true if completed.</summary>
        public Func<bool> PlacementTryAlignGameIndexFoldersToCanonical { get; set; }
    }

    /// <summary>Row-level and per-file (photo index) placement diagnostics for the active library.</summary>
    public sealed class LibraryStoragePlacementHealthSnapshot
    {
        /// <summary>When false, library root is unset or missing — UI hides the storage placement card.</summary>
        public bool IsApplicable { get; set; }
        public string RowSummary { get; set; }
        public string IndexedFilesSummary { get; set; }
        public bool RowNeedsAttention { get; set; }
        public bool IndexedFilesNeedAttention { get; set; }

        /// <summary>Folder path on the row vs canonical target (Game Index → Target storage folder). Empty when none.</summary>
        public IReadOnlyList<LibraryStoragePlacementGameRowMismatch> GameRowMismatches { get; set; }

        /// <summary>Misplaced captures and orphan GameId references. Empty when none.</summary>
        public IReadOnlyList<LibraryStoragePlacementIndexedFileIssue> IndexedFileIssues { get; set; }

        /// <summary>Total mismatch count (may exceed <see cref="GameRowMismatches"/> length when capped).</summary>
        public int GameRowMismatchTotalCount { get; set; }

        /// <summary>Total indexed-file issues recorded (may exceed list length when capped).</summary>
        public int IndexedFileIssueTotalCount { get; set; }

        /// <summary>Assigned captures outside the canonical folder (subset of indexed-file issues).</summary>
        public int IndexedFileMisplacedTotalCount { get; set; }

        /// <summary>Assigned captures whose GameId is missing from the game index.</summary>
        public int IndexedFileOrphanTotalCount { get; set; }
    }

    /// <summary>One game index row whose cached folder path differs from canonical placement.</summary>
    public sealed class LibraryStoragePlacementGameRowMismatch
    {
        public string GameId { get; set; }
        public string Name { get; set; }
        public string CachedFolderPath { get; set; }
        public string CanonicalFolderPath { get; set; }
    }

    /// <summary>One photo-index entry that fails placement rules.</summary>
    public sealed class LibraryStoragePlacementIndexedFileIssue
    {
        /// <summary><c>Misplaced</c> or <c>OrphanGameId</c>.</summary>
        public string IssueKind { get; set; }
        public string FilePath { get; set; }
        public string GameId { get; set; }
        /// <summary>Canonical game folder for misplaced files; empty for orphan GameId.</summary>
        public string CanonicalFolderPath { get; set; }

        /// <summary>Short label for grids and copy/export.</summary>
        public string IssueKindDisplay
        {
            get
            {
                if (string.Equals(IssueKind, "Misplaced", StringComparison.OrdinalIgnoreCase)) return "Outside canonical folder";
                if (string.Equals(IssueKind, "OrphanGameId", StringComparison.OrdinalIgnoreCase)) return "GameId missing from game index";
                return IssueKind ?? "";
            }
        }
    }
}
