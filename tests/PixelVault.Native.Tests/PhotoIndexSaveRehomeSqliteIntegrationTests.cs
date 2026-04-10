using System.Diagnostics;
using System.IO;
using System.Threading;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>
/// LIBST Step 9: <see cref="LibraryScanner.SavePhotoIndexEditorRows"/> with real <c>photo_index</c> / game index SQLite persistence.
/// </summary>
public sealed class PhotoIndexSaveRehomeSqliteIntegrationTests
{
    static readonly Func<string, string, string> Norm = (n, _) => (n ?? string.Empty).Trim();
    static readonly Func<string, string> Safe = n => string.IsNullOrWhiteSpace(n) ? "Unknown Game" : n.Trim();
    static readonly Func<string, string> Plat = p => (p ?? string.Empty).Trim();
    static string Nid(string id) => (id ?? string.Empty).Trim().ToUpperInvariant();

    sealed class SqliteBackedPhotoRehomeHost : ILibraryScanHost
    {
        readonly IndexPersistenceHarness _harness;
        readonly IFileSystemService _fs;
        readonly object _sync = new object();
        readonly ReaderWriterLockSlim _rw = new ReaderWriterLockSlim();

        public SqliteBackedPhotoRehomeHost(IndexPersistenceHarness harness, IFileSystemService fs)
        {
            _harness = harness;
            _fs = fs;
        }

        public object LibraryMaintenanceSync => _sync;
        public ReaderWriterLockSlim LibraryFolderCacheRwLock => _rw;

        public void EnsureLibraryRootExists(string root) => _fs.CreateDirectory(root);
        public void EnsureExifTool() { }

        public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false) =>
            _harness.Service.LoadLibraryMetadataIndexEntries(root);

        public void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index) =>
            _harness.Service.SaveLibraryMetadataIndexEntries(root, index);

        public List<GameIndexEditorRow> LoadSavedGameIndexRows(string root) => _harness.Service.LoadSavedGameIndexRows(root);

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
            : text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

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
            var index = new Dictionary<string, LibraryMetadataIndexEntry>(
                _harness.Service.LoadLibraryMetadataIndexEntries(root), StringComparer.OrdinalIgnoreCase);
            var gameRows = _harness.Service.LoadSavedGameIndexRows(root);
            var moved = PhotoIndexSaveRehomeIntegrationTests.MoveTowardCanonicalForIntegrationTest(root, filePaths, gameRows, index, _fs);
            if (moved > 0)
                _harness.Service.SaveLibraryMetadataIndexEntries(root, index);
            return moved;
        }

        public long ToCaptureUtcTicks(DateTime captureTime) => captureTime.Ticks;

        public string NormalizeGameId(string value) => Nid(value);

        public string NormalizeConsoleLabel(string value) => Plat(value);

        public long ResolveLibraryMetadataCaptureUtcTicks(string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry) =>
            existingEntry?.CaptureUtcTicks ?? 0L;

        public void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows) =>
            _harness.Service.SaveSavedGameIndexRows(root, rows);

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

        static GameIndexEditorRow? FindById(List<GameIndexEditorRow> rows, string gameId)
        {
            var wanted = Nid(gameId);
            if (string.IsNullOrWhiteSpace(wanted)) return null;
            return rows.FirstOrDefault(r => r != null && string.Equals(Nid(r.GameId), wanted, StringComparison.OrdinalIgnoreCase));
        }

        public GameIndexEditorRow FindSavedGameIndexRowById(IEnumerable<GameIndexEditorRow> rows, string gameId) => FindById(rows.ToList(), gameId)!;

        public string ResolveGameIdForIndexedFile(
            string root, string file, string platformLabel, IEnumerable<string> tags, Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows, string preferredGameId) =>
            throw new NotSupportedException();

        public bool SyncGameIndexRowsFromLibraryFolders(List<GameIndexEditorRow> rows, List<LibraryFolderInfo> folders) => false;

        public bool PruneObsoleteMultipleTagsRows(List<GameIndexEditorRow> rows) => false;

        public string ResolveLibraryFolderSteamAppId(string platformLabel, IEnumerable<string> files) => string.Empty;
    }

    [Fact]
    public void SavePhotoIndex_ChangingGameId_RehomesFileAndUpdatesSqlitePhotoIndex()
    {
        using var harness = new IndexPersistenceHarness();
        var lib = harness.LibraryRoot;
        var wrongDir = Path.Combine(lib, "wrong_place");
        Directory.CreateDirectory(wrongDir);
        var filePath = Path.Combine(wrongDir, "capture.png");
        File.WriteAllBytes(filePath, new byte[] { 137, 80 });

        var gameRows = new List<GameIndexEditorRow>
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
        harness.Service.SaveSavedGameIndexRows(lib, gameRows);

        var meta = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
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
        harness.Service.SaveLibraryMetadataIndexEntries(lib, meta);

        var fs = new FileSystemService();
        var host = new SqliteBackedPhotoRehomeHost(harness, fs);
        var scanner = new LibraryScanner(host, new PhotoIndexSaveRehomeIntegrationTests.NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => { });

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

        scanner.SavePhotoIndexEditorRows(lib, editorRows);

        var expectedDir = Path.Combine(lib, Safe("Portal"));
        var expectedFile = Path.Combine(expectedDir, "capture.png");
        Assert.True(Directory.Exists(expectedDir));
        Assert.True(File.Exists(expectedFile));
        Assert.False(File.Exists(filePath));

        var fromDb = harness.Service.LoadLibraryMetadataIndexEntries(lib);
        Assert.Single(fromDb);
        var entry = fromDb.Values.Single();
        Assert.Equal(expectedFile, entry.FilePath, ignoreCase: true);
        Assert.Equal("G00002", entry.GameId);

        Assert.True(File.Exists(harness.IndexDatabasePath));
    }

    [Fact]
    public void SavePhotoIndex_MergesIntoExistingIndex_WithoutDroppingEntriesMissingFromEditorRowList()
    {
        using var harness = new IndexPersistenceHarness();
        var lib = harness.LibraryRoot;
        var dir1 = Path.Combine(lib, "folder_a");
        var dir2 = Path.Combine(lib, "folder_b");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        var filePath1 = Path.Combine(dir1, "one.png");
        var filePath2 = Path.Combine(dir2, "two.png");
        File.WriteAllBytes(filePath1, new byte[] { 137, 80 });
        File.WriteAllBytes(filePath2, new byte[] { 137, 80 });

        var gameRows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Hades",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = dir1
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
        harness.Service.SaveSavedGameIndexRows(lib, gameRows);

        var ticks = DateTime.UtcNow.Ticks;
        var meta = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [filePath1] = new LibraryMetadataIndexEntry
            {
                FilePath = filePath1,
                Stamp = "a",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = 0,
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            },
            [filePath2] = new LibraryMetadataIndexEntry
            {
                FilePath = filePath2,
                Stamp = "a",
                GameId = "G00001",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                CaptureUtcTicks = 0,
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            }
        };
        harness.Service.SaveLibraryMetadataIndexEntries(lib, meta);

        var fs = new FileSystemService();
        var host = new SqliteBackedPhotoRehomeHost(harness, fs);
        var scanner = new LibraryScanner(host, new PhotoIndexSaveRehomeIntegrationTests.NoOpMetadataService(), fs,
            folderCacheRebuildHook: (_, _) => { });

        var editorRowsOnlyFirstFile = new List<PhotoIndexEditorRow>
        {
            new()
            {
                FilePath = filePath1,
                Stamp = "b",
                GameId = "G00002",
                ConsoleLabel = "Steam",
                TagText = "Steam",
                Starred = false,
                IndexAddedUtcTicks = ticks,
                RetroAchievementsGameId = string.Empty
            }
        };

        scanner.SavePhotoIndexEditorRows(lib, editorRowsOnlyFirstFile);

        var fromDb = harness.Service.LoadLibraryMetadataIndexEntries(lib);
        Assert.Equal(2, fromDb.Count);
        var byGid = fromDb.Values.Where(v => v != null).ToLookup(e => Nid(e.GameId));
        Assert.Single(byGid["G00002"]);
        Assert.Single(byGid["G00001"]);
        Assert.Contains("one.png", byGid["G00002"].Single().FilePath ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(filePath2, byGid["G00001"].Single().FilePath, ignoreCase: true);
    }
}
