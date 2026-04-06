using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PixelVaultNative
{
    /// <summary>Callbacks into the app for library metadata scanning (Phase 4 split — keeps scanner free of direct UI/index field access).</summary>
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
