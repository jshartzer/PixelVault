using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PixelVaultNative
{
    /// <summary>
    /// Application port for <see cref="LibraryScanner"/> — persistence, folder cache, game-index merge, and metadata policy without referencing WPF types.
    /// </summary>
    /// <remarks>
    /// <para><b>PV-PLN-EXT-002 A.4:</b> Keep new callbacks grouped by concern; prefer adding methods here over growing <c>MainWindow</c> private helpers that only the scanner calls.</para>
    /// <para><b>Implementation:</b> nested <c>LibraryScanHost</c> in <see cref="MainWindow"/> (<c>UI/Library/MainWindow.LibraryScannerBridge.cs</c>) forwards to <c>MainWindow</c> partials.</para>
    /// <list type="bullet">
    /// <item><description><b>Concurrency / sync:</b> <see cref="LibraryMaintenanceSync"/>, <see cref="LibraryFolderCacheRwLock"/>.</description></item>
    /// <item><description><b>Prereqs:</b> <see cref="EnsureLibraryRootExists"/>, <see cref="EnsureExifTool"/>.</description></item>
    /// <item><description><b>Photo index (SQLite-backed):</b> load/save metadata index, stamps, revisions, resolved entries, re-resolve rules.</description></item>
    /// <item><description><b>Folder cache:</b> stamps, load/save folder list, index-only refresh, cache clears.</description></item>
    /// <item><description><b>Game index:</b> rows load/save, assignment, sync/prune, Steam App ID resolution.</description></item>
    /// <item><description><b>Tags / platform / manual metadata bridge:</b> tag cache, console label, manual metadata helpers used during scan merge.</description></item>
    /// <item><description><b>Telemetry:</b> <see cref="LogLibraryScan"/>, <see cref="LogPerformanceSample"/>.</description></item>
    /// </list>
    /// </remarks>
    internal interface ILibraryScanHost
    {
        object LibraryMaintenanceSync { get; }

        ReaderWriterLockSlim LibraryFolderCacheRwLock { get; }

        void EnsureLibraryRootExists(string root);
        void EnsureExifTool();

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false);
        void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index);

        List<GameIndexEditorRow> LoadSavedGameIndexRows(string root);

        bool IsLibraryMediaFile(string path);

        string BuildLibraryMetadataStamp(string file);

        LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(
            string root,
            string file,
            string stamp,
            EmbeddedMetadataSnapshot snapshot,
            LibraryMetadataIndexEntry existingEntry,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows);

        /// <summary>True when a library rescan should rebuild a seemingly-complete row (wrong platform tags for non-Steam shortcut filenames).</summary>
        bool IndexEntryShouldReResolveForNonSteamShortcutMislabel(string root, string file, LibraryMetadataIndexEntry entry);

        /// <summary>True when a complete row is labeled Steam but neither the filename parse nor the assigned game index row supplies a Steam App ID.</summary>
        bool IndexEntryShouldReResolveSteamPlatformWithoutAppId(string root, string file, LibraryMetadataIndexEntry entry, List<GameIndexEditorRow> gameRows);

        void SetCachedFileTagsForLibraryScan(string file, string[] tags, long stampTicks);

        long MetadataCacheStamp(string file);

        string[] ParseTagText(string text);

        int GetLibraryScanWorkerCount(int batchCount, string pathHint);

        void LogLibraryScan(string message);

        void ClearLibraryFolderCache(string root);

        string BuildLibraryFolderInventoryStamp(string root);

        string BuildLibraryFolderStructuralStamp(string root);

        string GetLibraryMetadataIndexRevision(string root);

        bool TryGetIndexOnlyFolderCacheRefresh(string root, string currentFullStamp, out List<string> mediaFilePathsOneLevelUnderRoot);

        List<LibraryFolderInfo> LoadLibraryFolderCache(string root, string stamp);

        void SaveLibraryFolderCache(string root, string stamp, List<LibraryFolderInfo> folders);

        bool ApplySavedGameIndexRows(string root, List<LibraryFolderInfo> folders);

        bool PopulateMissingLibraryFolderSortKeys(List<LibraryFolderInfo> folders);

        void LogPerformanceSample(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds);

        void RemoveCachedFileTagEntries(IEnumerable<string> files);

        void RemoveCachedImageEntries(IEnumerable<string> files);

        void RemoveCachedFolderListings(IEnumerable<string> folderPaths);

        string[] BuildManualMetadataTagsForIndexUpsert(ManualMetadataItem item);

        string DetermineConsoleLabelFromTags(IEnumerable<string> tags);

        bool ManualMetadataChangesGroupingIdentity(ManualMetadataItem item);

        GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId);

        /// <summary>When <see cref="ManualMetadataItem.GameId"/> changes (e.g. photo index save), move files under canonical folders (LIBST Step 7).</summary>
        int RehomeLibraryCapturesTowardCanonicalFolders(string root, IEnumerable<string> filePaths);

        long ToCaptureUtcTicks(DateTime captureTime);

        string NormalizeGameId(string value);

        string NormalizeConsoleLabel(string value);

        long ResolveLibraryMetadataCaptureUtcTicks(string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry);

        void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows);

        GameIndexEditorRow EnsureGameIndexRowForAssignment(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId);

        string GuessGameIndexNameForFile(string file);

        bool IsLibraryImageFile(string path);

        DateTime ResolveIndexedLibraryDate(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index);

        long ResolveLibraryFileRecentSortUtcTicks(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index);

        string DetermineFolderPlatformForFiles(List<string> files, Dictionary<string, LibraryMetadataIndexEntry> index);

        GameIndexEditorRow FindSavedGameIndexRowById(IEnumerable<GameIndexEditorRow> rows, string gameId);

        string ResolveGameIdForIndexedFile(
            string root,
            string file,
            string platformLabel,
            IEnumerable<string> tags,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows,
            string preferredGameId);

        bool SyncGameIndexRowsFromLibraryFolders(List<GameIndexEditorRow> rows, List<LibraryFolderInfo> folders);

        bool PruneObsoleteMultipleTagsRows(List<GameIndexEditorRow> rows);

        string ResolveLibraryFolderSteamAppId(string platformLabel, IEnumerable<string> files);
    }
}
