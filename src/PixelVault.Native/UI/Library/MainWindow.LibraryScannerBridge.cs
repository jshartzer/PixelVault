using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        sealed class LibraryScanHost : ILibraryScanHost
        {
            readonly MainWindow window;

            public LibraryScanHost(MainWindow window)
            {
                this.window = window ?? throw new ArgumentNullException(nameof(window));
            }

            public object LibraryMaintenanceSync => window.libraryMaintenanceSync;

            public void EnsureLibraryRootExists(string root) => EnsureDir(root, "Library folder");

            public void EnsureExifTool() => window.EnsureExifTool();

            public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false) =>
                window.LoadLibraryMetadataIndex(root, forceDiskReload);

            public void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index) => window.SaveLibraryMetadataIndex(root, index);

            public List<GameIndexEditorRow> LoadSavedGameIndexRows(string root) => window.GetSavedGameIndexRowsForRoot(root);

            public bool IsLibraryMediaFile(string path) => IsMedia(path);

            public string BuildLibraryMetadataStamp(string file) => window.BuildLibraryMetadataStamp(file);

            public LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(
                string root,
                string file,
                string stamp,
                EmbeddedMetadataSnapshot snapshot,
                LibraryMetadataIndexEntry existingEntry,
                Dictionary<string, LibraryMetadataIndexEntry> index,
                List<GameIndexEditorRow> gameRows) =>
                window.BuildResolvedLibraryMetadataIndexEntry(root, file, stamp, snapshot, existingEntry, index, gameRows);

            public void SetCachedFileTagsForLibraryScan(string file, string[] tags, long stampTicks) => window.SetCachedFileTags(file, tags, stampTicks);

            public long MetadataCacheStamp(string file) => window.MetadataCacheStamp(file);

            public string[] ParseTagText(string text) => MainWindow.ParseTagText(text);

            public int GetLibraryScanWorkerCount(int batchCount, string pathHint) => window.GetLibraryScanWorkerCount(batchCount, pathHint);

            public void LogLibraryScan(string message) => window.Log(message);

            public void ClearLibraryFolderCache(string root) => window.ClearLibraryFolderCache(root);

            public string BuildLibraryFolderInventoryStamp(string root) => window.BuildLibraryFolderInventoryStamp(root);

            public List<LibraryFolderInfo> LoadLibraryFolderCache(string root, string stamp) => window.LoadLibraryFolderCache(root, stamp);

            public void SaveLibraryFolderCache(string root, string stamp, List<LibraryFolderInfo> folders) => window.SaveLibraryFolderCache(root, stamp, folders);

            public bool ApplySavedGameIndexRows(string root, List<LibraryFolderInfo> folders) => window.ApplySavedGameIndexRows(root, folders);

            public bool PopulateMissingLibraryFolderSortKeys(List<LibraryFolderInfo> folders) => window.PopulateMissingLibraryFolderSortKeys(folders);

            public void LogPerformanceSample(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds) =>
                window.LogPerformanceSample(area, stopwatch, detail, thresholdMilliseconds);

            public void RemoveCachedFileTagEntries(IEnumerable<string> files) => window.RemoveCachedFileTagEntries(files);

            public void RemoveCachedImageEntries(IEnumerable<string> files) => window.RemoveCachedImageEntries(files);

            public void RemoveCachedFolderListings(IEnumerable<string> folderPaths) => window.RemoveCachedFolderListings(folderPaths);

            public string[] BuildManualMetadataTagsForIndexUpsert(ManualMetadataItem item) =>
                window.BuildMetadataTagSet(null, window.BuildManualMetadataExtraTags(item), item.AddPhotographyTag);

            public string DetermineConsoleLabelFromTags(IEnumerable<string> tags) => MainWindow.DetermineConsoleLabelFromTags(tags);

            public bool ManualMetadataChangesGroupingIdentity(ManualMetadataItem item) => window.ManualMetadataChangesGroupingIdentity(item);

            public GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(
                IEnumerable<GameIndexEditorRow> rows,
                string name,
                string platformLabel,
                string preferredGameId) =>
                window.ResolveExistingGameIndexRowForAssignment(rows, name, platformLabel, preferredGameId);

            public long ToCaptureUtcTicks(DateTime captureTime) => window.ToCaptureUtcTicks(captureTime);

            public string NormalizeGameId(string value) => window.NormalizeGameId(value);

            public string NormalizeConsoleLabel(string value) => MainWindow.NormalizeConsoleLabel(value);

            public long ResolveLibraryMetadataCaptureUtcTicks(string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry) =>
                window.ResolveLibraryMetadataCaptureUtcTicks(file, stamp, snapshot, existingEntry);

            public void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows) => window.SaveSavedGameIndexRows(root, rows);

            public GameIndexEditorRow EnsureGameIndexRowForAssignment(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId) =>
                window.EnsureGameIndexRowForAssignment(rows, name, platformLabel, preferredGameId);

            public string GuessGameIndexNameForFile(string file) => window.GuessGameIndexNameForFile(file);

            public bool IsLibraryImageFile(string path) => IsImage(path);

            public DateTime ResolveIndexedLibraryDate(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index) =>
                window.ResolveIndexedLibraryDate(root, file, index);

            public string DetermineFolderPlatformForFiles(List<string> files, Dictionary<string, LibraryMetadataIndexEntry> index) =>
                window.DetermineFolderPlatform(files, index, null);

            public GameIndexEditorRow FindSavedGameIndexRowById(IEnumerable<GameIndexEditorRow> rows, string gameId) =>
                window.FindSavedGameIndexRowById(rows, gameId);

            public string ResolveGameIdForIndexedFile(
                string root,
                string file,
                string platformLabel,
                IEnumerable<string> tags,
                Dictionary<string, LibraryMetadataIndexEntry> index,
                List<GameIndexEditorRow> gameRows,
                string preferredGameId) =>
                window.ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, preferredGameId);

            public bool SyncGameIndexRowsFromLibraryFolders(List<GameIndexEditorRow> rows, List<LibraryFolderInfo> folders) =>
                window.SyncGameIndexRowsFromLibraryFolders(rows, folders);

            public bool PruneObsoleteMultipleTagsRows(List<GameIndexEditorRow> rows) => window.PruneObsoleteMultipleTagsRows(rows);

            public string ResolveLibraryFolderSteamAppId(string platformLabel, IEnumerable<string> files) =>
                window.ResolveLibraryFolderSteamAppId(platformLabel, files);
        }
    }
}
