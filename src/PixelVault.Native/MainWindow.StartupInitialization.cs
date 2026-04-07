using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal static (
            string DataRoot,
            string LogsRoot,
            string CacheRoot,
            string CoversRoot,
            string ThumbsRoot,
            string SavedCoversRoot,
            string SettingsPath,
            string ChangelogPath,
            string UndoManifestPath) ComputePersistentStorageLayout(string appRoot, Func<string, string> resolvePersistentDataRoot)
        {
            var dataRoot = resolvePersistentDataRoot(appRoot);
            var cacheRoot = Path.Combine(dataRoot, "cache");
            return (
                dataRoot,
                Path.Combine(dataRoot, "logs"),
                cacheRoot,
                Path.Combine(cacheRoot, "covers"),
                Path.Combine(cacheRoot, "thumbs"),
                Path.Combine(dataRoot, "saved-covers"),
                Path.Combine(dataRoot, "PixelVault.settings.ini"),
                Path.Combine(appRoot, "CHANGELOG.md"),
                Path.Combine(cacheRoot, "last-import.tsv"));
        }

        internal static (ISettingsService Settings, IFileSystemService FileSystem) CreateSettingsAndFileServices()
        {
            return (new SettingsService(), new FileSystemService());
        }

        internal static ICoverService CreateCoverService(MainWindow mw, IFileSystemService fileSystem, string coversRoot)
        {
            return new CoverService(new CoverServiceDependencies
            {
                FileSystem = fileSystem,
                AppVersion = AppVersion,
                CoversRoot = coversRoot,
                RequestTimeoutMilliseconds = SteamRequestTimeoutMilliseconds,
                GetSteamGridDbApiToken = delegate { return mw.CurrentSteamGridDbApiToken(); },
                NormalizeTitle = delegate(string value) { return mw.NormalizeTitle(value); },
                NormalizeConsoleLabel = delegate(string value) { return MainWindow.NormalizeConsoleLabel(value); },
                SafeCacheName = delegate(string value) { return mw.SafeCacheName(value); },
                StripTags = delegate(string value) { return mw.StripTags(value); },
                Sanitize = delegate(string value) { return MainWindow.Sanitize(value); },
                Log = delegate(string message) { mw.Log(message); },
                LogPerformanceSample = delegate(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds) { mw.LogPerformanceSample(area, stopwatch, detail, thresholdMilliseconds); },
                ClearImageCache = delegate { mw.ClearImageCache(); },
                RemoveCachedImageEntries = delegate(IEnumerable<string> paths) { mw.RemoveCachedImageEntries(paths); }
            });
        }

        internal static IMetadataService CreateMetadataService(MainWindow mw, string cacheRoot)
        {
            return new MetadataService(new MetadataServiceDependencies
            {
                GetExifToolPath = delegate { return mw.exifToolPath; },
                CacheRoot = cacheRoot,
                IsVideo = delegate(string file) { return MainWindow.IsVideo(file); },
                MetadataSidecarPath = delegate(string file) { return mw.MetadataSidecarPath(file); },
                MetadataReadPath = delegate(string file) { return mw.MetadataReadPath(file); },
                BuildMetadataTagSet = delegate(IEnumerable<string> platformTags, IEnumerable<string> extraTags, bool addPhotographyTag) { return mw.BuildMetadataTagSet(platformTags, extraTags, addPhotographyTag); },
                CleanComment = delegate(string value) { return MainWindow.CleanComment(value); },
                CleanTag = delegate(string value) { return MainWindow.CleanTag(value); },
                ParseEmbeddedMetadataDateValue = delegate(string value) { return MainWindow.ParseEmbeddedMetadataDateValue(value); },
                GetMetadataWorkerCount = delegate(int workItems) { return mw.GetMetadataWorkerCount(workItems); },
                Log = delegate(string message) { mw.Log(message); },
                RunExe = delegate(string file, string[] args, string cwd, bool logOutput) { mw.RunExe(file, args, cwd, logOutput); },
                RunExeCapture = delegate(string file, string[] args, string cwd, bool logOutput, CancellationToken cancellationToken) { return mw.RunExeCapture(file, args, cwd, logOutput, cancellationToken); }
            });
        }

        internal static ILibraryScanner CreateLibraryScanner(MainWindow mw, IMetadataService metadataService, IFileSystemService fileSystemService)
        {
            return new LibraryScanner(new LibraryScanHost(mw), metadataService, fileSystemService);
        }

        internal static ImportServiceDependencies BuildImportServiceDependencies(
            MainWindow mw,
            ILibraryScanner libraryScanner,
            IFileSystemService fileSystemService,
            IMetadataService metadataService,
            ICoverService coverService,
            IGameIndexEditorAssignmentService gameIndexEditorAssignmentService)
        {
            return new ImportServiceDependencies
            {
                UndoManifestPath = () => mw.undoManifestPath,
                GetDestinationRoot = () => mw.destinationRoot,
                GetLibraryRoot = () => mw.libraryRoot,
                GetConflictMode = mw.CurrentConflictMode,
                UniquePath = MainWindow.Unique,
                MoveMetadataSidecarIfPresent = mw.MoveMetadataSidecarIfPresent,
                AddSidecarUndoEntryIfPresent = mw.AddSidecarUndoEntryIfPresent,
                Log = mw.Log,
                IsMedia = MainWindow.IsMedia,
                GetSafeGameFolderName = mw.GetSafeGameFolderName,
                GetGameNameFromFileName = mw.GetGameNameFromFileName,
                EnsureDirectoryExists = MainWindow.EnsureDir,
                GetLibraryScanner = () => libraryScanner,
                EnumerateSourceMediaFiles = mw.EnumerateSourceFiles,
                ParseFilenameForImport = mw.ParseFilename,
                EnsureSteamAppIdInGameIndex = mw.EnsureSteamAppIdInGameIndex,
                SanitizeManualRenameGameTitle = MainWindow.Sanitize,
                NormalizeTitleForManualRename = mw.NormalizeTitle,
                FileSystem = fileSystemService,
                MetadataService = metadataService,
                GetFileCreationTime = path => File.GetCreationTime(path),
                GetFileLastWriteTime = path => File.GetLastWriteTime(path),
                GamePhotographyTagLabel = GamePhotographyTag,
                CoverService = coverService,
                NormalizeGameIndexName = name => mw.NormalizeGameIndexName(name),
                DetermineManualMetadataPlatformLabel = mw.DetermineManualMetadataPlatformLabel,
                ManualMetadataChangesGroupingIdentity = mw.ManualMetadataChangesGroupingIdentity,
                GameIndexEditorAssignment = gameIndexEditorAssignmentService,
                BuildManualMetadataGameTitleChoiceLabel = (name, platform) => mw.BuildGameTitleChoiceLabel(name, platform),
                ParseManualMetadataTagText = MainWindow.ParseTagText,
                CleanTag = MainWindow.CleanTag,
                LoadManualMetadataGameTitleRowsAsync = (root, ct) => Task.Factory.StartNew(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    var rows = mw.GetSavedGameIndexRowsForRoot(root);
                    if (rows == null || rows.Count == 0) rows = mw.LoadGameIndexEditorRowsCore(root, null);
                    return rows ?? new List<GameIndexEditorRow>();
                }, ct, TaskCreationOptions.None, TaskScheduler.Default)
            };
        }

        internal static ILibrarySession CreateLibrarySessionForStartup(
            MainWindow mw,
            LibraryWorkspaceContext workspace,
            ILibraryScanner libraryScanner,
            IFileSystemService fileSystemService,
            IGameIndexEditorAssignmentService gameIndexEditorAssignmentService,
            IIndexPersistenceService indexPersistenceService)
        {
            return new LibrarySession(
                workspace,
                libraryScanner,
                fileSystemService,
                gameIndexEditorAssignmentService,
                mw.LoadLibraryMetadataIndex,
                mw.LoadSavedGameIndexRows,
                mw.SaveLibraryMetadataIndex,
                mw.LoadLibraryMetadataIndexForFilePaths,
                mw.MergePersistLibraryMetadataIndexEntries,
                mw.LoadLibraryFolderCacheSnapshot,
                mw.ResolveIndexedLibraryDate,
                mw.BuildResolvedLibraryMetadataIndexEntry,
                mw.RefreshLibraryCoversAsync,
                mw.ShowLibraryMetadataScanWindow,
                MainWindow.EnsureDir,
                indexPersistenceService,
                (path, onCompleted) => mw.ToggleLibraryFileStarredByPath(path, onCompleted),
                (path, comment, onCompleted) => mw.SaveLibraryFileCommentByPath(path, comment, onCompleted));
        }

        internal static IGameIndexService CreateGameIndexServiceForStartup(
            MainWindow mw,
            ILibraryScanner libraryScanner,
            ILibrarySession librarySession,
            IIndexPersistenceService indexPersistenceService,
            IGameIndexEditorAssignmentService gameIndexEditorAssignmentService)
        {
            return new GameIndexService(new GameIndexServiceDependencies
            {
                LibraryScanner = libraryScanner,
                LibrarySession = librarySession,
                IndexPersistence = indexPersistenceService,
                GameIndexEditorAssignment = gameIndexEditorAssignmentService,
                HostLibraryRoot = () => mw.libraryRoot,
                MergeGameIndexRows = rows => mw.MergeGameIndexRows(rows),
                BuildGameIndexRowsFromFolders = folders => mw.BuildGameIndexRowsFromFolders(folders),
                RefreshCachedLibraryFoldersFromGameIndex = mw.RefreshCachedLibraryFoldersFromGameIndex,
                ApplyEditorSaveRowPolicies = mw.ApplyGameIndexEditorSaveRowPolicies,
                BuildGameIndexSaveAliasMap = mw.BuildGameIndexSaveAliasMap,
                AlignLibraryFoldersToGameIndex = mw.AlignLibraryFoldersToGameIndex,
                RewriteGameIdAliasesInLibraryFolderCacheFile = mw.RewriteGameIdAliasesInLibraryFolderCacheFile
            });
        }

        void CreateStartupDirectories()
        {
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(logsRoot);
            Directory.CreateDirectory(cacheRoot);
            Directory.CreateDirectory(coversRoot);
            Directory.CreateDirectory(thumbsRoot);
            Directory.CreateDirectory(savedCoversRoot);
        }

        void InitializeDefaultWorkspaceRootsAndTools()
        {
            sourceRoot = @"E:\Game Capture Uploads";
            destinationRoot = @"E:\Game Captures";
            libraryRoot = destinationRoot;
            exifToolPath = Path.Combine(appRoot, "tools", "exiftool.exe");
            ffmpegPath = Path.Combine(appRoot, "tools", "ffmpeg.exe");
        }
    }
}
