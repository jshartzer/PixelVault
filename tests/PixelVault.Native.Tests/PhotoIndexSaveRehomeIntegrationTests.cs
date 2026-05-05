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
        List<LibraryFolderInfo> _folderCache;
        bool _hasFolderCacheSnapshot;
        readonly object _sync = new object();
        readonly ReaderWriterLockSlim _rw = new ReaderWriterLockSlim();

        public PhotoSaveTestHost(
            IFileSystemService fs,
            Dictionary<string, LibraryMetadataIndexEntry> initialIndex,
            List<GameIndexEditorRow> initialRows,
            List<LibraryFolderInfo>? initialFolderCache = null)
        {
            _fs = fs;
            _index = new Dictionary<string, LibraryMetadataIndexEntry>(initialIndex, StringComparer.OrdinalIgnoreCase);
            _gameRows = initialRows.Select(r => CloneRow(r)!).ToList();
            _folderCache = CloneFolderCache(initialFolderCache);
            _hasFolderCacheSnapshot = initialFolderCache != null;
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

        static LibraryFolderInfo? CloneFolder(LibraryFolderInfo? folder)
        {
            if (folder == null) return null;
            return new LibraryFolderInfo
            {
                GameId = folder.GameId ?? string.Empty,
                Name = folder.Name ?? string.Empty,
                FolderPath = folder.FolderPath ?? string.Empty,
                FileCount = folder.FileCount,
                PreviewImagePath = folder.PreviewImagePath ?? string.Empty,
                PlatformLabel = folder.PlatformLabel ?? string.Empty,
                FilePaths = folder.FilePaths == null ? Array.Empty<string>() : (string[])folder.FilePaths.Clone(),
                NewestCaptureUtcTicks = folder.NewestCaptureUtcTicks,
                NewestRecentSortUtcTicks = folder.NewestRecentSortUtcTicks,
                SteamAppId = folder.SteamAppId ?? string.Empty,
                NonSteamId = folder.NonSteamId ?? string.Empty,
                SteamGridDbId = folder.SteamGridDbId ?? string.Empty,
                RetroAchievementsGameId = folder.RetroAchievementsGameId ?? string.Empty,
                SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                IsCompleted100Percent = folder.IsCompleted100Percent,
                CompletedUtcTicks = folder.CompletedUtcTicks,
                IsFavorite = folder.IsFavorite,
                IsShowcase = folder.IsShowcase,
                CollectionNotes = folder.CollectionNotes ?? string.Empty,
                PendingGameAssignment = folder.PendingGameAssignment,
                StorageGroupId = folder.StorageGroupId ?? string.Empty
            };
        }

        static List<LibraryFolderInfo> CloneFolderCache(IEnumerable<LibraryFolderInfo>? folders) =>
            (folders ?? Array.Empty<LibraryFolderInfo>()).Where(folder => folder != null).Select(folder => CloneFolder(folder)!).ToList();

        public object LibraryMaintenanceSync => _sync;
        public ReaderWriterLockSlim LibraryFolderCacheRwLock => _rw;
        public List<string>? IndexOnlyRefreshFiles { get; set; }
        public int IndexOnlyRefreshCalls { get; private set; }

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
            BuildResolvedEntryForTest(file, stamp, existingEntry, gameRows);

        static LibraryMetadataIndexEntry BuildResolvedEntryForTest(
            string file,
            string stamp,
            LibraryMetadataIndexEntry? existingEntry,
            List<GameIndexEditorRow> gameRows)
        {
            var fileDirectory = Path.GetDirectoryName(file) ?? string.Empty;
            var folderLeaf = Path.GetFileName(fileDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var matched = (gameRows ?? new List<GameIndexEditorRow>())
                .FirstOrDefault(row => row != null
                    && (LibraryPlacementService.PathsEqualNormalized(row.FolderPath, fileDirectory)
                        || string.Equals(row.Name ?? string.Empty, folderLeaf, StringComparison.OrdinalIgnoreCase)));
            var existingGameId = existingEntry == null ? string.Empty : Nid(existingEntry.GameId);
            var existingGameIdIsValid = !string.IsNullOrWhiteSpace(existingGameId)
                && (gameRows ?? new List<GameIndexEditorRow>())
                    .Any(row => row != null && string.Equals(Nid(row.GameId), existingGameId, StringComparison.OrdinalIgnoreCase));
            var gameId = existingGameIdIsValid ? existingGameId : string.Empty;
            if (string.IsNullOrWhiteSpace(gameId) && matched != null) gameId = Nid(matched.GameId);
            if (string.IsNullOrWhiteSpace(gameId)) gameId = "G00001";
            var console = existingEntry == null ? string.Empty : Plat(existingEntry.ConsoleLabel);
            if (string.IsNullOrWhiteSpace(console) && matched != null) console = Plat(matched.PlatformLabel);
            if (string.IsNullOrWhiteSpace(console)) console = "Steam";
            return new LibraryMetadataIndexEntry
            {
                FilePath = file,
                Stamp = stamp ?? string.Empty,
                GameId = gameId,
                ConsoleLabel = console,
                TagText = console,
                CaptureUtcTicks = existingEntry != null && existingEntry.CaptureUtcTicks > 0 ? existingEntry.CaptureUtcTicks : DateTime.UtcNow.Ticks,
                Starred = existingEntry != null && existingEntry.Starred,
                IndexAddedUtcTicks = existingEntry != null && existingEntry.IndexAddedUtcTicks > 0 ? existingEntry.IndexAddedUtcTicks : DateTime.UtcNow.Ticks,
                RetroAchievementsGameId = existingEntry == null ? string.Empty : (existingEntry.RetroAchievementsGameId ?? string.Empty)
            };
        }

        public bool IndexEntryShouldReResolveForNonSteamShortcutMislabel(string root, string file, LibraryMetadataIndexEntry entry) => false;

        public bool IndexEntryShouldReResolveSteamPlatformWithoutAppId(string root, string file, LibraryMetadataIndexEntry entry, List<GameIndexEditorRow> gameRows) => false;

        public void SetCachedFileTagsForLibraryScan(string file, string[] tags, long stampTicks) { }

        public long MetadataCacheStamp(string file) => 0;
        public string[] ParseTagText(string text) => string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

        public int GetLibraryScanWorkerCount(int batchCount, string pathHint) => 1;
        public void LogLibraryScan(string message) { }
        public void ClearLibraryFolderCache(string root)
        {
            _folderCache = new List<LibraryFolderInfo>();
            _hasFolderCacheSnapshot = false;
        }
        public string BuildLibraryFolderInventoryStamp(string root) => "test";
        public string BuildLibraryFolderStructuralStamp(string root) => "test";
        public string GetLibraryMetadataIndexRevision(string root) => "0";
        public bool TryGetIndexOnlyFolderCacheRefresh(string root, string currentFullStamp, out List<string> mediaFilePathsOneLevelUnderRoot)
        {
            IndexOnlyRefreshCalls++;
            if (IndexOnlyRefreshFiles == null)
            {
                mediaFilePathsOneLevelUnderRoot = new List<string>();
                return false;
            }

            mediaFilePathsOneLevelUnderRoot = IndexOnlyRefreshFiles.ToList();
            return true;
        }

        public List<LibraryFolderInfo> LoadLibraryFolderCache(string root, string stamp) =>
            _hasFolderCacheSnapshot ? CloneFolderCache(_folderCache) : null!;

        public List<LibraryFolderInfo> LoadLibraryFolderCacheSnapshot(string root, bool allowStaleMetadataRevision = false) =>
            _hasFolderCacheSnapshot ? CloneFolderCache(_folderCache) : null!;

        public void SaveLibraryFolderCache(string root, string stamp, List<LibraryFolderInfo> folders)
        {
            _folderCache = CloneFolderCache(folders);
            _hasFolderCacheSnapshot = true;
        }

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

    sealed class BlockingMetadataRepairService : IMetadataService
    {
        readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Started = new ManualResetEventSlim(false);

        public void Release() => _release.Set();

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

        public Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files, CancellationToken cancellationToken = default)
        {
            var list = (files ?? Array.Empty<string>()).Where(file => !string.IsNullOrWhiteSpace(file)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Started.Set();
            Assert.True(_release.Wait(TimeSpan.FromSeconds(5)), "Timed out waiting for test to release background metadata repair.");
            return list.ToDictionary(
                file => file,
                _ => new EmbeddedMetadataSnapshot { CaptureTime = DateTime.UtcNow },
                StringComparer.OrdinalIgnoreCase);
        }

        public Task<Dictionary<string, string[]>> ReadEmbeddedKeywordTagsBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        public Task<Dictionary<string, EmbeddedMetadataSnapshot>> ReadEmbeddedMetadataBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadEmbeddedMetadataBatch(files, cancellationToken));

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

    [Fact]
    public void LoadLibraryFolders_IncludesNestedMediaUnderFirstLevelGameFolder()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-nestedmedia-" + Guid.NewGuid().ToString("N"));
        var gameLeaf = Path.Combine(lib, "Portal");
        var nested = Path.Combine(gameLeaf, "deep");
        Directory.CreateDirectory(nested);
        var filePath = Path.Combine(nested, "shot.png");
        File.WriteAllBytes(filePath, new byte[] { 137, 80 });
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = gameLeaf,
                FilePaths = Array.Empty<string>()
            }
        };
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath] = new LibraryMetadataIndexEntry
            {
                FilePath = filePath,
                Stamp = "1",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = 100,
                Starred = false,
                IndexAddedUtcTicks = DateTime.UtcNow.Ticks,
                RetroAchievementsGameId = string.Empty
            }
        };
        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows);
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => { });
        try
        {
            var folders = scanner.LoadLibraryFolders(lib, host.LoadLibraryMetadataIndex(lib, true));
            var folder = Assert.Single(folders, x => string.Equals(x.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, folder.FileCount);
            Assert.Contains(filePath, folder.FilePaths);
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void LoadLibraryFolders_ExcludesHdrFallbackParkingFolder()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-hdrfallback-library-" + Guid.NewGuid().ToString("N"));
        var gameLeaf = Path.Combine(lib, "Portal");
        var fallbackLeaf = Path.Combine(lib, "HDR Duplicates", "Portal");
        Directory.CreateDirectory(gameLeaf);
        Directory.CreateDirectory(fallbackLeaf);
        var libraryFile = Path.Combine(gameLeaf, "shot.png");
        var fallbackFile = Path.Combine(fallbackLeaf, "shot.png");
        File.WriteAllBytes(libraryFile, new byte[] { 137, 80 });
        File.WriteAllBytes(fallbackFile, new byte[] { 137, 80 });
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = gameLeaf,
                FilePaths = Array.Empty<string>()
            }
        };
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [libraryFile] = new LibraryMetadataIndexEntry
            {
                FilePath = libraryFile,
                Stamp = "1",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = 100,
                Starred = false,
                IndexAddedUtcTicks = DateTime.UtcNow.Ticks,
                RetroAchievementsGameId = string.Empty
            },
            [fallbackFile] = new LibraryMetadataIndexEntry
            {
                FilePath = fallbackFile,
                Stamp = "1",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = 200,
                Starred = false,
                IndexAddedUtcTicks = DateTime.UtcNow.Ticks,
                RetroAchievementsGameId = string.Empty
            }
        };
        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows);
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => { });
        try
        {
            var folders = scanner.LoadLibraryFolders(lib, host.LoadLibraryMetadataIndex(lib, true));
            var folder = Assert.Single(folders, x => string.Equals(x.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, folder.FileCount);
            Assert.Contains(libraryFile, folder.FilePaths);
            Assert.DoesNotContain(fallbackFile, folder.FilePaths);
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void TryUpdateLibraryFolderCacheForTouchedPaths_UpdatesOnlyTouchedGameRows()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-incremental-folder-cache-" + Guid.NewGuid().ToString("N"));
        var hadesDir = Path.Combine(lib, "Hades");
        var portalDir = Path.Combine(lib, "Portal");
        Directory.CreateDirectory(hadesDir);
        Directory.CreateDirectory(portalDir);
        var hadesOld = Path.Combine(hadesDir, "old.png");
        var hadesNew = Path.Combine(hadesDir, "new.png");
        var portalFile = Path.Combine(portalDir, "portal.png");
        File.WriteAllBytes(hadesOld, new byte[] { 137, 80 });
        File.WriteAllBytes(hadesNew, new byte[] { 137, 80 });
        File.WriteAllBytes(portalFile, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Hades",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = hadesDir,
                FilePaths = new[] { hadesOld }
            },
            new()
            {
                GameId = "G00002",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = portalDir,
                FilePaths = new[] { portalFile }
            }
        };

        var ticks = DateTime.UtcNow.Ticks;
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [hadesOld] = new LibraryMetadataIndexEntry
            {
                FilePath = hadesOld,
                Stamp = "1",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks - 20,
                Starred = false,
                IndexAddedUtcTicks = ticks - 20,
                RetroAchievementsGameId = string.Empty
            },
            [hadesNew] = new LibraryMetadataIndexEntry
            {
                FilePath = hadesNew,
                Stamp = "2",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks,
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            },
            [portalFile] = new LibraryMetadataIndexEntry
            {
                FilePath = portalFile,
                Stamp = "3",
                GameId = "G00002",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks - 10,
                Starred = false,
                IndexAddedUtcTicks = ticks - 10,
                RetroAchievementsGameId = string.Empty
            }
        };

        var initialCache = new List<LibraryFolderInfo>
        {
            new()
            {
                GameId = "G00001",
                Name = "Hades",
                FolderPath = hadesDir,
                FileCount = 1,
                PreviewImagePath = hadesOld,
                PlatformLabel = "Steam",
                FilePaths = new[] { hadesOld },
                NewestCaptureUtcTicks = ticks - 20,
                NewestRecentSortUtcTicks = ticks - 20
            },
            new()
            {
                GameId = "G00002",
                Name = "Portal",
                FolderPath = portalDir,
                FileCount = 1,
                PreviewImagePath = portalFile,
                PlatformLabel = "Steam",
                FilePaths = new[] { portalFile },
                NewestCaptureUtcTicks = ticks - 10,
                NewestRecentSortUtcTicks = ticks - 10
            }
        };

        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows, initialCache);
        var fullRebuildCalled = false;
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => fullRebuildCalled = true);

        try
        {
            var updated = scanner.TryUpdateLibraryFolderCacheForTouchedPaths(lib, new[] { hadesNew });

            Assert.True(updated);
            Assert.False(fullRebuildCalled);
            var cache = host.LoadLibraryFolderCacheSnapshot(lib);
            var hades = Assert.Single(cache, folder => string.Equals(folder.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, hades.FileCount);
            Assert.Contains(hadesOld, hades.FilePaths);
            Assert.Contains(hadesNew, hades.FilePaths);
            var portal = Assert.Single(cache, folder => string.Equals(folder.GameId, "G00002", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, portal.FileCount);
            Assert.Equal(new[] { portalFile }, portal.FilePaths);
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void UpsertLibraryMetadataIndexEntries_UsesTouchedFolderCacheUpdate_WhenCacheExists()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-upsert-incremental-cache-" + Guid.NewGuid().ToString("N"));
        var hadesDir = Path.Combine(lib, "Hades");
        var portalDir = Path.Combine(lib, "Portal");
        Directory.CreateDirectory(hadesDir);
        Directory.CreateDirectory(portalDir);
        var hadesOld = Path.Combine(hadesDir, "old.png");
        var hadesNew = Path.Combine(hadesDir, "new.png");
        var portalFile = Path.Combine(portalDir, "portal.png");
        File.WriteAllBytes(hadesOld, new byte[] { 137, 80 });
        File.WriteAllBytes(hadesNew, new byte[] { 137, 80 });
        File.WriteAllBytes(portalFile, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Hades",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = hadesDir,
                FilePaths = new[] { hadesOld }
            },
            new()
            {
                GameId = "G00002",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = portalDir,
                FilePaths = new[] { portalFile }
            }
        };

        var ticks = DateTime.UtcNow.Ticks;
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [hadesOld] = new LibraryMetadataIndexEntry
            {
                FilePath = hadesOld,
                Stamp = "1",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks - 20,
                Starred = false,
                IndexAddedUtcTicks = ticks - 20,
                RetroAchievementsGameId = string.Empty
            },
            [portalFile] = new LibraryMetadataIndexEntry
            {
                FilePath = portalFile,
                Stamp = "3",
                GameId = "G00002",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks - 10,
                Starred = false,
                IndexAddedUtcTicks = ticks - 10,
                RetroAchievementsGameId = string.Empty
            }
        };
        var initialCache = new List<LibraryFolderInfo>
        {
            new()
            {
                GameId = "G00001",
                Name = "Hades",
                FolderPath = hadesDir,
                FileCount = 1,
                PreviewImagePath = hadesOld,
                PlatformLabel = "Steam",
                FilePaths = new[] { hadesOld },
                NewestCaptureUtcTicks = ticks - 20,
                NewestRecentSortUtcTicks = ticks - 20
            },
            new()
            {
                GameId = "G00002",
                Name = "Portal",
                FolderPath = portalDir,
                FileCount = 1,
                PreviewImagePath = portalFile,
                PlatformLabel = "Steam",
                FilePaths = new[] { portalFile },
                NewestCaptureUtcTicks = ticks - 10,
                NewestRecentSortUtcTicks = ticks - 10
            }
        };

        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows, initialCache);
        var fullRebuildCalled = false;
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => fullRebuildCalled = true);

        try
        {
            scanner.UpsertLibraryMetadataIndexEntries(new[] { hadesNew }, lib);

            Assert.False(fullRebuildCalled);
            var cache = host.LoadLibraryFolderCacheSnapshot(lib);
            var hades = Assert.Single(cache, folder => string.Equals(folder.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, hades.FileCount);
            Assert.Contains(hadesOld, hades.FilePaths);
            Assert.Contains(hadesNew, hades.FilePaths);
            var portal = Assert.Single(cache, folder => string.Equals(folder.GameId, "G00002", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { portalFile }, portal.FilePaths);
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void RefreshFolderCacheAfterGameIndexChange_UsesExplicitFullRebuild()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-explicit-cache-rebuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lib);
        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(
            fs,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase),
            new List<GameIndexEditorRow>());
        var fullRebuildCalled = false;
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => fullRebuildCalled = true);

        try
        {
            scanner.RefreshFolderCacheAfterGameIndexChange(lib);

            Assert.True(fullRebuildCalled);
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void LoadLibraryFoldersCached_ForceRefreshIgnoresCachedSnapshot()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-force-refresh-cache-" + Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(lib, "Portal");
        Directory.CreateDirectory(gameDir);
        var indexedFile = Path.Combine(gameDir, "fresh.png");
        File.WriteAllBytes(indexedFile, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = gameDir,
                FilePaths = new[] { indexedFile }
            }
        };
        var ticks = DateTime.UtcNow.Ticks;
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [indexedFile] = new LibraryMetadataIndexEntry
            {
                FilePath = indexedFile,
                Stamp = "1",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks,
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            }
        };
        var staleCache = new List<LibraryFolderInfo>
        {
            new()
            {
                GameId = "G99999",
                Name = "Stale cached row",
                FolderPath = Path.Combine(lib, "Stale"),
                FileCount = 0,
                PreviewImagePath = string.Empty,
                PlatformLabel = "Steam",
                FilePaths = Array.Empty<string>()
            }
        };

        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows, staleCache);
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs);

        try
        {
            var folders = scanner.LoadLibraryFoldersCached(lib, forceRefresh: true);

            var folder = Assert.Single(folders);
            Assert.Equal("G00001", folder.GameId);
            Assert.Equal("Portal", folder.Name);
            Assert.Equal(1, folder.FileCount);
            Assert.Contains(indexedFile, folder.FilePaths);
            var savedCache = host.LoadLibraryFolderCacheSnapshot(lib);
            Assert.Single(savedCache, cached => string.Equals(cached.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public async Task LoadLibraryFoldersCached_MissingCaptureTicksReturnsBeforeMetadataRepairCompletes()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-fast-projection-repair-" + Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(lib, "Portal");
        Directory.CreateDirectory(gameDir);
        var indexedFile = Path.Combine(gameDir, "fresh.png");
        File.WriteAllBytes(indexedFile, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = gameDir,
                FilePaths = new[] { indexedFile }
            }
        };
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [indexedFile] = new LibraryMetadataIndexEntry
            {
                FilePath = indexedFile,
                Stamp = "1",
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
        var metadata = new BlockingMetadataRepairService();
        var scanner = new LibraryScanner(host, metadata, fs);

        try
        {
            var loadTask = Task.Run(() => scanner.LoadLibraryFoldersCached(lib, forceRefresh: true));
            var completed = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(loadTask, completed);
            var folders = await loadTask;
            var folder = Assert.Single(folders);
            Assert.Equal("G00001", folder.GameId);
            Assert.Equal(1, folder.FileCount);
            Assert.Contains(indexedFile, folder.FilePaths);
            Assert.True(await WaitForSignalAsync(metadata.Started, TimeSpan.FromSeconds(2)), "Background metadata repair should be queued after fast projection.");

            metadata.Release();

            Assert.True(SpinWait.SpinUntil(() =>
            {
                var repairedIndex = host.LoadLibraryMetadataIndex(lib, true);
                if (!repairedIndex.TryGetValue(indexedFile, out var entry) || entry == null || entry.CaptureUtcTicks <= 0)
                    return false;
                var cache = host.LoadLibraryFolderCacheSnapshot(lib);
                var cachedFolder = cache.SingleOrDefault(item => string.Equals(item.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
                return cachedFolder != null && cachedFolder.NewestCaptureUtcTicks > 0;
            }, TimeSpan.FromSeconds(3)), "Background metadata repair should update the index and touched cached folder row.");
        }
        finally
        {
            metadata.Release();
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public async Task LoadLibraryFoldersCached_CacheMissReturnsUsableRowsBeforeMetadataRepairCompletes()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-cache-miss-fast-projection-" + Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(lib, "Portal");
        Directory.CreateDirectory(gameDir);
        var indexedFile = Path.Combine(gameDir, "fresh.png");
        File.WriteAllBytes(indexedFile, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = gameDir,
                FilePaths = new[] { indexedFile }
            }
        };
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [indexedFile] = new LibraryMetadataIndexEntry
            {
                FilePath = indexedFile,
                Stamp = "1",
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
        var metadata = new BlockingMetadataRepairService();
        var scanner = new LibraryScanner(host, metadata, fs);

        try
        {
            var loadTask = Task.Run(() => scanner.LoadLibraryFoldersCached(lib, forceRefresh: false));
            var completed = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(loadTask, completed);
            var folders = await loadTask;
            var folder = Assert.Single(folders);
            Assert.Equal("G00001", folder.GameId);
            Assert.Equal("Portal", folder.Name);
            Assert.Equal(1, folder.FileCount);
            Assert.Contains(indexedFile, folder.FilePaths);
            var savedCacheBeforeRepair = host.LoadLibraryFolderCacheSnapshot(lib);
            var cachedFolderBeforeRepair = Assert.Single(savedCacheBeforeRepair, item => string.Equals(item.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, cachedFolderBeforeRepair.NewestCaptureUtcTicks);
            Assert.True(await WaitForSignalAsync(metadata.Started, TimeSpan.FromSeconds(2)), "Cache-miss library open should queue background metadata repair after painting usable rows.");

            metadata.Release();

            Assert.True(SpinWait.SpinUntil(() =>
            {
                var repairedIndex = host.LoadLibraryMetadataIndex(lib, true);
                if (!repairedIndex.TryGetValue(indexedFile, out var entry) || entry == null || entry.CaptureUtcTicks <= 0)
                    return false;
                var cache = host.LoadLibraryFolderCacheSnapshot(lib);
                var cachedFolder = cache.SingleOrDefault(item => string.Equals(item.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
                return cachedFolder != null && cachedFolder.NewestCaptureUtcTicks > 0;
            }, TimeSpan.FromSeconds(3)), "Background metadata repair should update the cache-miss folder row after initial paint.");
        }
        finally
        {
            metadata.Release();
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public async Task LoadLibraryFoldersCached_OrphanGameIdReturnsBeforeMetadataRepairCompletes()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-orphan-gameid-fast-projection-" + Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(lib, "Portal");
        Directory.CreateDirectory(gameDir);
        var indexedFile = Path.Combine(gameDir, "fresh.png");
        File.WriteAllBytes(indexedFile, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = gameDir,
                FilePaths = new[] { indexedFile }
            }
        };
        var ticks = DateTime.UtcNow.Ticks;
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [indexedFile] = new LibraryMetadataIndexEntry
            {
                FilePath = indexedFile,
                Stamp = "1",
                GameId = "G99999",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks,
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            }
        };

        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows);
        var metadata = new BlockingMetadataRepairService();
        var scanner = new LibraryScanner(host, metadata, fs);

        try
        {
            var loadTask = Task.Run(() => scanner.LoadLibraryFoldersCached(lib, forceRefresh: false));
            var completed = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.Same(loadTask, completed);
            var folders = await loadTask;
            var folder = Assert.Single(folders);
            Assert.True(folder.PendingGameAssignment);
            Assert.Equal(string.Empty, folder.GameId);
            Assert.StartsWith("Needs assignment", folder.Name);
            Assert.Equal(1, folder.FileCount);
            Assert.Contains(indexedFile, folder.FilePaths);
            var savedCacheBeforeRepair = host.LoadLibraryFolderCacheSnapshot(lib);
            var cachedPendingFolder = Assert.Single(savedCacheBeforeRepair);
            Assert.True(cachedPendingFolder.PendingGameAssignment);
            Assert.Equal(string.Empty, cachedPendingFolder.GameId);
            Assert.True(await WaitForSignalAsync(metadata.Started, TimeSpan.FromSeconds(2)), "Orphan GameId repair should be queued after fast projection.");

            metadata.Release();

            Assert.True(SpinWait.SpinUntil(() =>
            {
                var repairedIndex = host.LoadLibraryMetadataIndex(lib, true);
                if (!repairedIndex.TryGetValue(indexedFile, out var entry) || entry == null)
                    return false;
                if (!string.Equals(entry.GameId, "G00001", StringComparison.OrdinalIgnoreCase))
                    return false;

                var cache = host.LoadLibraryFolderCacheSnapshot(lib);
                var cachedFolder = cache.SingleOrDefault(item => string.Equals(item.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
                return cachedFolder != null
                    && !cachedFolder.PendingGameAssignment
                    && cachedFolder.FilePaths.Contains(indexedFile, StringComparer.OrdinalIgnoreCase);
            }, TimeSpan.FromSeconds(3)), "Background metadata repair should resolve orphan GameId rows and update the touched cached folder row.");
        }
        finally
        {
            metadata.Release();
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void LoadLibraryFoldersCached_IndexOnlyRefreshProjectsFromSuppliedIndexFileList()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-index-only-projection-" + Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(lib, "Portal");
        var nestedDir = Path.Combine(gameDir, "Nested");
        Directory.CreateDirectory(nestedDir);
        var indexedFile = Path.Combine(nestedDir, "indexed.png");
        var excludedFile = Path.Combine(nestedDir, "excluded.png");
        File.WriteAllBytes(indexedFile, new byte[] { 137, 80 });
        File.WriteAllBytes(excludedFile, new byte[] { 137, 80 });

        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = gameDir,
                FilePaths = new[] { indexedFile, excludedFile }
            }
        };
        var ticks = DateTime.UtcNow.Ticks;
        var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [indexedFile] = new LibraryMetadataIndexEntry
            {
                FilePath = indexedFile,
                Stamp = "1",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks,
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            },
            [excludedFile] = new LibraryMetadataIndexEntry
            {
                FilePath = excludedFile,
                Stamp = "2",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = ticks,
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            }
        };

        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(fs, index, rows)
        {
            IndexOnlyRefreshFiles = new List<string> { indexedFile }
        };
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs);

        try
        {
            var folders = scanner.LoadLibraryFoldersCached(lib, forceRefresh: false);

            Assert.Equal(1, host.IndexOnlyRefreshCalls);
            var folder = Assert.Single(folders);
            Assert.Equal("G00001", folder.GameId);
            Assert.Equal(1, folder.FileCount);
            Assert.Contains(indexedFile, folder.FilePaths);
            Assert.DoesNotContain(excludedFile, folder.FilePaths);
            var savedCache = host.LoadLibraryFolderCacheSnapshot(lib);
            var cachedFolder = Assert.Single(savedCache, item => string.Equals(item.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { indexedFile }, cachedFolder.FilePaths);
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    static Task<bool> WaitForSignalAsync(ManualResetEventSlim signal, TimeSpan timeout) =>
        Task.Run(() => signal.Wait(timeout));

    [Fact]
    public void ImportSort_UpdatesFolderCacheForNewFile_WithoutFullLibraryScan()
    {
        var lib = Path.Combine(Path.GetTempPath(), "pv-import-sort-incremental-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lib);
        var rootFile = Path.Combine(lib, "Portal.png");
        File.WriteAllBytes(rootFile, new byte[] { 137, 80 });
        var portalDir = Path.Combine(lib, "Portal");
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = portalDir,
                FilePaths = Array.Empty<string>()
            }
        };
        var initialCache = new List<LibraryFolderInfo>
        {
            new()
            {
                GameId = "G00001",
                Name = "Portal",
                FolderPath = portalDir,
                FileCount = 0,
                PreviewImagePath = string.Empty,
                PlatformLabel = "Steam",
                FilePaths = Array.Empty<string>()
            }
        };

        var fs = new FileSystemService();
        var host = new PhotoSaveTestHost(
            fs,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase),
            rows,
            initialCache);
        var fullRebuildCalled = false;
        var scanner = new LibraryScanner(host, new NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => fullRebuildCalled = true);
        var import = new ImportService(new ImportServiceDependencies
        {
            FileSystem = fs,
            LogService = NullLogService.Instance,
            MetadataService = new NoOpMetadataService(),
            GetFileCreationTime = _ => DateTime.MinValue,
            GetFileLastWriteTime = _ => DateTime.MinValue,
            CoverService = new StubCoverService(),
            GetDestinationRoot = () => lib,
            GetLibraryRoot = () => lib,
            GetConflictMode = () => "Rename",
            UniquePath = UniqueFile,
            MoveMetadataSidecarIfPresent = (_, _) => { },
            IsMedia = path =>
            {
                var ext = Path.GetExtension(path);
                return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase);
            },
            GetSafeGameFolderName = Safe,
            GetGameNameFromFileName = path => Path.GetFileNameWithoutExtension(path) ?? string.Empty,
            EnsureDirectoryExists = (path, _) => fs.CreateDirectory(path),
            GetLibraryScanner = () => scanner,
            LoadSavedGameIndexRows = _ => rows.Select(row => new GameIndexEditorRow
            {
                GameId = row.GameId,
                Name = row.Name,
                PlatformLabel = row.PlatformLabel,
                FolderPath = row.FolderPath,
                FilePaths = row.FilePaths,
                StorageGroupId = row.StorageGroupId
            }).ToList(),
            BuildGameIndexTitleCounts = sourceRows => (sourceRows ?? Array.Empty<GameIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(row => Norm(row.Name, row.FolderPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            NormalizeGameIndexName = name => Norm(name, string.Empty),
            NormalizeGameIndexNameWithFolder = Norm,
            NormalizeConsoleLabel = Plat,
            BuildGameIndexIdentity = (name, platform) => Norm(name, string.Empty) + "|" + Plat(platform),
            CleanTag = value => (value ?? string.Empty).Trim(),
            ParseFilenameForImport = _ => new FilenameParseResult
            {
                GameTitleHint = "Portal",
                PlatformLabel = "Steam"
            }
        });

        try
        {
            var result = import.SortDestinationRootIntoGameFolders(lib, lib);

            var sortedPath = Path.Combine(portalDir, "Portal.png");
            Assert.Equal(1, result.Sorted);
            Assert.False(fullRebuildCalled);
            Assert.False(File.Exists(rootFile));
            Assert.True(File.Exists(sortedPath));
            var cache = host.LoadLibraryFolderCacheSnapshot(lib);
            var folder = Assert.Single(cache, item => string.Equals(item.GameId, "G00001", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, folder.FileCount);
            Assert.Equal(sortedPath, folder.PreviewImagePath, ignoreCase: true);
            Assert.Contains(sortedPath, folder.FilePaths);
        }
        finally
        {
            try
            {
                if (Directory.Exists(lib)) Directory.Delete(lib, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }
}
