using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>
/// Integration-style test: <see cref="LibraryScanner.SavePhotoIndexEditorRows"/> + re-home hook (LIBST Step 8).
/// Uses a fake <see cref="ILibraryScanHost"/> and real disk + <see cref="LibraryPlacementService"/>.
/// </summary>
public sealed class PhotoIndexSaveRehomeIntegrationTests
{
    static readonly Func<string, string, string> Norm = (n, _) => (n ?? string.Empty).Trim();
    static readonly Func<string, string> Safe = n => string.IsNullOrWhiteSpace(n) ? "Unknown Game" : n.Trim();
    static readonly Func<string, string> Plat = p => (p ?? string.Empty).Trim();

    static string Nid(string id) => (id ?? string.Empty).Trim();

    static Dictionary<string, int> TitleCounts(IReadOnlyList<GameIndexEditorRow> rows) =>
        (rows ?? Array.Empty<GameIndexEditorRow>())
            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => Norm(r.Name, r.FolderPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    static GameIndexEditorRow? FindById(List<GameIndexEditorRow> rows, string gameId)
    {
        var wanted = Nid(gameId);
        if (string.IsNullOrWhiteSpace(wanted)) return null;
        return rows.FirstOrDefault(r => r != null && string.Equals(Nid(r.GameId), wanted, StringComparison.OrdinalIgnoreCase));
    }

    static string UniqueFile(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Mirrors organize/re-home: move files under canonical folder for current index GameId; updates index paths.</summary>
    internal static int MoveTowardCanonicalForIntegrationTest(
        string libraryRoot,
        IEnumerable<string> filePaths,
        List<GameIndexEditorRow> gameRows,
        Dictionary<string, LibraryMetadataIndexEntry> indexByPath,
        IFileSystemService fs)
    {
        var readOnlyRows = (IReadOnlyList<GameIndexEditorRow>)gameRows;
        var counts = TitleCounts(readOnlyRows);
        var moved = 0;
        foreach (var path in (filePaths ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (!fs.FileExists(path)) continue;
            if (!indexByPath.TryGetValue(path, out var entry) || entry == null) continue;
            var gameRow = FindById(gameRows, entry.GameId);
            string folderLeaf;
            if (gameRow != null)
            {
                folderLeaf = LibraryPlacementService.BuildCanonicalStorageFolderName(
                    gameRow,
                    readOnlyRows,
                    Norm,
                    Safe,
                    Plat,
                    counts);
            }
            else
            {
                folderLeaf = Safe(Path.GetFileNameWithoutExtension(path));
            }
            var targetDirectory = Path.Combine(libraryRoot, folderLeaf);
            if (!fs.DirectoryExists(targetDirectory)) fs.CreateDirectory(targetDirectory);
            var currentDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            if (LibraryPlacementService.IsCaptureAlreadyUnderCanonicalOrganizeTarget(
                    currentDirectory,
                    targetDirectory,
                    gameRow != null))
                continue;

            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(path));
            if (fs.FileExists(targetFile)) targetFile = UniqueFile(targetFile);
            if (string.Equals(path, targetFile, StringComparison.OrdinalIgnoreCase)) continue;
            fs.MoveFile(path, targetFile);
            moved++;
            indexByPath.Remove(path);
            indexByPath[targetFile] = new LibraryMetadataIndexEntry
            {
                FilePath = targetFile,
                Stamp = entry.Stamp ?? string.Empty,
                GameId = entry.GameId ?? string.Empty,
                ConsoleLabel = entry.ConsoleLabel ?? string.Empty,
                TagText = entry.TagText ?? string.Empty,
                CaptureUtcTicks = entry.CaptureUtcTicks,
                Starred = entry.Starred,
                IndexAddedUtcTicks = entry.IndexAddedUtcTicks,
                RetroAchievementsGameId = entry.RetroAchievementsGameId ?? string.Empty
            };
        }
        return moved;
    }

    sealed class PhotoSaveTestHost : ILibraryScanHost
    {
        readonly IFileSystemService _fs;
        Dictionary<string, LibraryMetadataIndexEntry> _index;
        List<GameIndexEditorRow> _gameRows;
        readonly object _sync = new object();
        readonly ReaderWriterLockSlim _rw = new ReaderWriterLockSlim();

        public PhotoSaveTestHost(
            IFileSystemService fs,
            Dictionary<string, LibraryMetadataIndexEntry> initialIndex,
            List<GameIndexEditorRow> initialRows)
        {
            _fs = fs;
            _index = new Dictionary<string, LibraryMetadataIndexEntry>(initialIndex, StringComparer.OrdinalIgnoreCase);
            _gameRows = initialRows.Select(r => CloneRow(r)!).ToList();
        }

        static GameIndexEditorRow? CloneRow(GameIndexEditorRow? r)
        {
            if (r == null) return null;
            return new GameIndexEditorRow
            {
                GameId = r.GameId ?? string.Empty,
                Name = r.Name ?? string.Empty,
                PlatformLabel = r.PlatformLabel ?? string.Empty,
                SteamAppId = r.SteamAppId ?? string.Empty,
                NonSteamId = r.NonSteamId ?? string.Empty,
                SteamGridDbId = r.SteamGridDbId ?? string.Empty,
                RetroAchievementsGameId = r.RetroAchievementsGameId ?? string.Empty,
                FileCount = r.FileCount,
                FolderPath = r.FolderPath ?? string.Empty,
                PreviewImagePath = r.PreviewImagePath ?? string.Empty,
                FilePaths = r.FilePaths == null ? Array.Empty<string>() : (string[])r.FilePaths.Clone(),
                StorageGroupId = r.StorageGroupId ?? string.Empty,
                IndexAddedUtcTicks = r.IndexAddedUtcTicks
            };
        }

        public object LibraryMaintenanceSync => _sync;
        public ReaderWriterLockSlim LibraryFolderCacheRwLock => _rw;

        public void EnsureLibraryRootExists(string root) => _fs.CreateDirectory(root);
        public void EnsureExifTool() { }

        public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false) =>
            new Dictionary<string, LibraryMetadataIndexEntry>(_index, StringComparer.OrdinalIgnoreCase);

        public void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index) =>
            _index = new Dictionary<string, LibraryMetadataIndexEntry>(
                (index ?? new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase))
                .Where(p => p.Value != null),
                StringComparer.OrdinalIgnoreCase);

        public List<GameIndexEditorRow> LoadSavedGameIndexRows(string root) => _gameRows.Select(r => CloneRow(r)!).ToList();

        public bool IsLibraryMediaFile(string path)
        {
            var e = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            return e is ".png" or ".jpg" or ".jpeg" or ".webp";
        }

        public string BuildLibraryMetadataStamp(string file) =>
            _fs.FileExists(file) ? _fs.GetLastWriteTime(file).Ticks.ToString() : "0";

        public LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(
            string root, string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry,
            Dictionary<string, LibraryMetadataIndexEntry> index, List<GameIndexEditorRow> gameRows) =>
            throw new NotSupportedException();

        public void SetCachedFileTagsForLibraryScan(string file, string[] tags, long stampTicks) { }

        public long MetadataCacheStamp(string file) => 0;
        public string[] ParseTagText(string text) => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

        public int GetLibraryScanWorkerCount(int batchCount, string pathHint) => 1;
        public void LogLibraryScan(string message) { }
        public void ClearLibraryFolderCache(string root) { }
        public string BuildLibraryFolderInventoryStamp(string root) => "test";
        public string BuildLibraryFolderStructuralStamp(string root) => "test";
        public string GetLibraryMetadataIndexRevision(string root) => "0";
        public bool TryGetIndexOnlyFolderCacheRefresh(string root, string currentFullStamp, out List<string> mediaFilePathsOneLevelUnderRoot)
        {
            mediaFilePathsOneLevelUnderRoot = new List<string>();
            return false;
        }

        public List<LibraryFolderInfo> LoadLibraryFolderCache(string root, string stamp) => new List<LibraryFolderInfo>();

        public void SaveLibraryFolderCache(string root, string stamp, List<LibraryFolderInfo> folders) { }

        public bool ApplySavedGameIndexRows(string root, List<LibraryFolderInfo> folders) => false;

        public bool PopulateMissingLibraryFolderSortKeys(List<LibraryFolderInfo> folders) => false;

        public void LogPerformanceSample(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds) { }

        public void RemoveCachedFileTagEntries(IEnumerable<string> files) { }
        public void RemoveCachedImageEntries(IEnumerable<string> files) { }
        public void RemoveCachedFolderListings(IEnumerable<string> folderPaths) { }

        public string[] BuildManualMetadataTagsForIndexUpsert(ManualMetadataItem item) => Array.Empty<string>();

        public string DetermineConsoleLabelFromTags(IEnumerable<string> tags)
        {
            var list = tags == null ? new List<string>() : tags.ToList();
            if (list.Any(t => string.Equals(t, "Steam", StringComparison.OrdinalIgnoreCase))) return "Steam";
            return "Other";
        }

        public bool ManualMetadataChangesGroupingIdentity(ManualMetadataItem item) => false;

        public GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(
            IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId) =>
            throw new NotSupportedException();

        public int RehomeLibraryCapturesTowardCanonicalFolders(string root, IEnumerable<string> filePaths)
        {
            var moved = MoveTowardCanonicalForIntegrationTest(root, filePaths, _gameRows, _index, _fs);
            if (moved > 0)
                SaveLibraryMetadataIndex(root, new Dictionary<string, LibraryMetadataIndexEntry>(_index, StringComparer.OrdinalIgnoreCase));
            return moved;
        }

        public long ToCaptureUtcTicks(DateTime captureTime) => captureTime.Ticks;

        public string NormalizeGameId(string value) => Nid(value);

        public string NormalizeConsoleLabel(string value) => Plat(value);

        public long ResolveLibraryMetadataCaptureUtcTicks(string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry) =>
            existingEntry?.CaptureUtcTicks ?? 0L;

        public void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            _gameRows = (rows ?? Array.Empty<GameIndexEditorRow>()).Where(r => r != null).Select(r => CloneRow(r)!).ToList();
        }

        public GameIndexEditorRow EnsureGameIndexRowForAssignment(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId)
        {
            var id = Nid(preferredGameId);
            var found = rows.FirstOrDefault(r => r != null && string.Equals(Nid(r.GameId), id, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            var created = new GameIndexEditorRow
            {
                GameId = string.IsNullOrWhiteSpace(id) ? "G00001" : id,
                Name = Norm(name, string.Empty),
                PlatformLabel = Plat(platformLabel),
                FilePaths = Array.Empty<string>()
            };
            rows.Add(created);
            return created;
        }

        public string GuessGameIndexNameForFile(string file) => Path.GetFileNameWithoutExtension(file ?? string.Empty) ?? string.Empty;

        public bool IsLibraryImageFile(string path)
        {
            var e = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            return e is ".png" or ".jpg" or ".jpeg";
        }

        public DateTime ResolveIndexedLibraryDate(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index) =>
            DateTime.MinValue;

        public long ResolveLibraryFileRecentSortUtcTicks(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index) => 0L;

        public string DetermineFolderPlatformForFiles(List<string> files, Dictionary<string, LibraryMetadataIndexEntry> index) => "Steam";

        public GameIndexEditorRow FindSavedGameIndexRowById(IEnumerable<GameIndexEditorRow> rows, string gameId) => FindById(rows.ToList(), gameId)!;

        public string ResolveGameIdForIndexedFile(
            string root, string file, string platformLabel, IEnumerable<string> tags, Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows, string preferredGameId) =>
            throw new NotSupportedException();

        public bool SyncGameIndexRowsFromLibraryFolders(List<GameIndexEditorRow> rows, List<LibraryFolderInfo> folders) => false;

        public bool PruneObsoleteMultipleTagsRows(List<GameIndexEditorRow> rows) => false;

        public string ResolveLibraryFolderSteamAppId(string platformLabel, IEnumerable<string> files) => string.Empty;
    }

    internal sealed class NoOpMetadataService : IMetadataService
    {
        public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, bool preserveFileTimes, string comment, bool addPhotographyTag) =>
            Array.Empty<string>();

        public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag) =>
            Array.Empty<string>();

        public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag, bool writeDateMetadata, bool writeCommentMetadata, bool writeTagMetadata) =>
            Array.Empty<string>();

        public string[] ReadEmbeddedKeywordTagsDirect(string file, CancellationToken cancellationToken = default) => Array.Empty<string>();
        public string ReadEmbeddedCommentDirect(string file, CancellationToken cancellationToken = default) => string.Empty;
        public DateTime? ReadEmbeddedCaptureDateDirect(string file, CancellationToken cancellationToken = default) => null;
        public Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files, CancellationToken cancellationToken = default) => new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files, CancellationToken cancellationToken = default) => new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);

        public Task<Dictionary<string, string[]>> ReadEmbeddedKeywordTagsBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        public Task<Dictionary<string, EmbeddedMetadataSnapshot>> ReadEmbeddedMetadataBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase));

        public int? ReadEmbeddedRatingDirect(string file, CancellationToken cancellationToken = default) => null;
        public string[] BuildStarRatingExifArgs(string file, bool starred) => Array.Empty<string>();
        public void EnsureExifTool() { }
        public void RunExifToolBatch(IReadOnlyList<ExifWriteRequest> requests) { }

        public ExifWriteBatchResult RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string>? progress = null, CancellationToken cancellationToken = default) =>
            new ExifWriteBatchResult();
    }

    [Fact]
    public void SavePhotoIndex_ChangingGameId_RehomesFileUnderCanonicalFolder()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-photo-rehome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lib);
        var wrongDir = Path.Combine(lib, "wrong_place");
        Directory.CreateDirectory(wrongDir);
        var filePath = Path.Combine(wrongDir, "capture.png");
        File.WriteAllBytes(filePath, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Hades",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = wrongDir
            },
            new()
            {
                GameId = "G00002",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = string.Empty
            }
        };

        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath] = new LibraryMetadataIndexEntry
            {
                FilePath = filePath,
                Stamp = "a",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = 0,
                Starred = false,
                IndexAddedUtcTicks = DateTime.UtcNow.Ticks,
                RetroAchievementsGameId = string.Empty
            }
        };

        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows);
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => { /* skip heavy cache rebuild */ });

        var editorRows = new List<PhotoIndexEditorRow>
        {
            new()
            {
                FilePath = filePath,
                Stamp = "b",
                GameId = "G00002",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                Starred = false,
                IndexAddedUtcTicks = DateTime.UtcNow.Ticks,
                RetroAchievementsGameId = string.Empty
            }
        };

        try
        {
            scanner.SavePhotoIndexEditorRows(lib, editorRows);
            var expectedDir = Path.Combine(lib, Safe("Portal"));
            var expectedFile = Path.Combine(expectedDir, "capture.png");
            Assert.True(Directory.Exists(expectedDir));
            Assert.True(File.Exists(expectedFile));
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, recursive: true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }
}
