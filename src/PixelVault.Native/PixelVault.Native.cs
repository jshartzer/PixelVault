using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Forms = System.Windows.Forms;

namespace PixelVaultNative
{
    internal static class Program
    {
        static void MergeGlobalScrollBarTheme(Application app)
        {
            if (app == null) return;
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PixelVault;component/Themes/PixelVaultScrollBars.xaml", UriKind.Absolute)
            });
        }

        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)3072 |
                (SecurityProtocolType)768 |
                SecurityProtocolType.Tls;
            Batteries_V2.Init();
            var app = new Application();
            MergeGlobalScrollBarTheme(app);
            app.Run(new MainWindow());
        }
    }

    public sealed partial class MainWindow : Window
    {
        const string AppVersion = "0.991";
        const string GamePhotographyTag = "Game Photography";
        const string CustomPlatformPrefix = "Platform:";
        const string ClearedExternalIdSentinel = "__PV_CLEARED__";
        const int MaxImageCacheEntries = 720;
        const int SteamRequestTimeoutMilliseconds = 15000;
        readonly string appRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        readonly string dataRoot;
        readonly string logsRoot;
        readonly string cacheRoot;
        readonly string coversRoot;
        readonly string thumbsRoot;
        readonly string savedCoversRoot;
        readonly string settingsPath;
        readonly string changelogPath;
        readonly string undoManifestPath;
        readonly LibraryBitmapLruCache libraryBitmapCache = new LibraryBitmapLruCache(MaxImageCacheEntries);
        readonly LibraryImageLoadCoordinator imageLoadCoordinator = new LibraryImageLoadCoordinator();
        LibraryThumbnailPipeline libraryThumbnailPipeline;
        BitmapSource libraryCompletionBadgeBitmap;
        readonly SemaphoreSlim videoClipInfoWarmLimiter = new SemaphoreSlim(2);
        readonly SemaphoreSlim videoPreviewWarmLimiter = new SemaphoreSlim(1);
        readonly HashSet<string> failedFfmpegPosterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, byte> activeVideoPreviewGenerations = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, byte> activeVideoInfoGenerations = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, LibraryMetadataIndexEntry> libraryMetadataIndex = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
        readonly object libraryMetadataIndexSync = new object();
        readonly object libraryMaintenanceSync = new object();
        readonly object logFileSync = new object();
        string libraryMetadataIndexRoot;

        string sourceRoot;
        string destinationRoot;
        string libraryRoot;
        string starredExportFolder = string.Empty;
        /// <summary>Persisted companion to <c>library_index_anchor</c>; when it differs from the active library path, startup explains per-path indexes.</summary>
        string libraryIndexAnchor = string.Empty;
        readonly LibraryWorkspaceContext libraryWorkspace;

        internal string LibraryWorkspaceRoot => libraryRoot;
        string exifToolPath;
        string ffmpegPath;
        string steamGridDbApiToken;
        int libraryFolderTileSize = 300;
        string libraryFolderSortMode = "platform";
        string libraryGroupingMode = "all";
        bool troubleshootingLoggingEnabled;
        bool troubleshootingLogRedactPaths;
        bool libraryDoubleClickSetsFolderCover;
        readonly string _diagnosticsSessionId;
        const long TroubleshootingLogMaxBytes = 5_000_000L;
        string _libraryBrowserPersistedSearch = string.Empty;
        string _libraryBrowserPersistedLastViewKey = string.Empty;
        double _libraryBrowserPersistedFolderScroll;
        double _libraryBrowserPersistedDetailScroll;
        double _libraryBrowserPersistedFolderPaneWidth;
        LibraryBrowserWorkingSet _libraryBrowserLiveWorkingSet;
        string _manualMetadataRecentTitleLabelsSerialized = string.Empty;
        Action<bool> activeLibraryFolderRefresh;
        LibraryFolderInfo activeSelectedLibraryFolder;

        TextBox logBox;
        TextBlock status;
        bool importSearchSubfoldersForRename = true;
        string importMoveConflictMode = "Rename";
        /// <summary>Thread-safe mirror for background metadata (avoid Dispatcher.Invoke); default matches former Settings checkbox.</summary>
        volatile bool _includeGameCaptureKeywordsMirror = true;
        Window photoIndexEditorWindow;
        Window gameIndexEditorWindow;
        bool gameIndexEditorLoadPending;
        Window filenameConventionEditorWindow;
        PhotographyGalleryWindow _activePhotographyGalleryWindow;
        readonly ICoverService coverService;
        readonly IFilenameParserService filenameParserService;
        readonly IFilenameRulesService filenameRulesService;
        readonly IIndexPersistenceService indexPersistenceService;
        readonly IMetadataService metadataService;
        readonly ISettingsService settingsService;
        readonly IFileSystemService fileSystemService;
        readonly ILibraryScanner libraryScanner;
        readonly ILibrarySession librarySession;
        readonly IImportService importService;
        readonly IGameIndexEditorAssignmentService gameIndexEditorAssignmentService;
        readonly IGameIndexService gameIndexService;

        sealed class LibraryDetailRenderSnapshot
        {
            public List<LibraryDetailRenderGroup> Groups = new List<LibraryDetailRenderGroup>();
            public List<string> VisibleFiles = new List<string>();
            public Dictionary<string, LibraryTimelineCaptureContext> TimelineContextByFile = new Dictionary<string, LibraryTimelineCaptureContext>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, LibraryDetailMediaLayoutInfo> MediaLayoutByFile = new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);
        }

        sealed class LibraryDetailRenderGroup
        {
            public DateTime CaptureDate;
            public List<string> Files = new List<string>();
        }

        internal sealed class LibraryTimelineCaptureContext
        {
            internal string GameTitle;
            internal string PlatformLabel;
            internal DateTime CaptureDate;
            internal string Comment;
        }

        LibraryMetadataIndexEntry CloneLibraryMetadataIndexEntry(LibraryMetadataIndexEntry entry)
        {
            if (entry == null) return null;
            return new LibraryMetadataIndexEntry
            {
                FilePath = entry.FilePath,
                Stamp = entry.Stamp,
                GameId = NormalizeGameId(entry.GameId),
                ConsoleLabel = entry.ConsoleLabel,
                TagText = entry.TagText,
                CaptureUtcTicks = entry.CaptureUtcTicks,
                Starred = entry.Starred,
                IndexAddedUtcTicks = entry.IndexAddedUtcTicks
            };
        }

        Dictionary<string, LibraryMetadataIndexEntry> CloneLibraryMetadataIndexEntries(IEnumerable<KeyValuePair<string, LibraryMetadataIndexEntry>> entries)
        {
            var clone = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in entries ?? Enumerable.Empty<KeyValuePair<string, LibraryMetadataIndexEntry>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                clone[pair.Key] = CloneLibraryMetadataIndexEntry(pair.Value);
            }
            return clone;
        }

        LibraryFolderInfo CloneLibraryFolderInfo(LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            return new LibraryFolderInfo
            {
                GameId = folder.GameId,
                Name = folder.Name,
                FolderPath = folder.FolderPath,
                FileCount = folder.FileCount,
                PreviewImagePath = folder.PreviewImagePath,
                PlatformLabel = folder.PlatformLabel,
                FilePaths = folder.FilePaths == null ? null : folder.FilePaths.ToArray(),
                NewestCaptureUtcTicks = folder.NewestCaptureUtcTicks,
                NewestRecentSortUtcTicks = folder.NewestRecentSortUtcTicks,
                SteamAppId = folder.SteamAppId,
                SteamGridDbId = folder.SteamGridDbId,
                SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                IsCompleted100Percent = folder.IsCompleted100Percent,
                CompletedUtcTicks = folder.CompletedUtcTicks,
                IsFavorite = folder.IsFavorite,
                IsShowcase = folder.IsShowcase,
                CollectionNotes = folder.CollectionNotes
            };
        }

        bool TryGetCachedFileTags(string file, long expectedStamp, out string[] tags)
        {
            return libraryWorkspace.TryGetCachedFileTags(file, expectedStamp, out tags);
        }

        void SetCachedFileTags(string file, IEnumerable<string> tags, long stamp)
        {
            libraryWorkspace.SetCachedFileTags(file, tags, stamp);
        }

        public MainWindow()
        {
            _diagnosticsSessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            dataRoot = ResolvePersistentDataRoot(appRoot);
            logsRoot = Path.Combine(dataRoot, "logs");
            cacheRoot = Path.Combine(dataRoot, "cache");
            coversRoot = Path.Combine(cacheRoot, "covers");
            thumbsRoot = Path.Combine(cacheRoot, "thumbs");
            libraryThumbnailPipeline = new LibraryThumbnailPipeline(
                thumbsRoot,
                IsVideo,
                EnsureVideoPoster,
                Log,
                libraryBitmapCache.TryGet,
                libraryBitmapCache.Store);
            savedCoversRoot = Path.Combine(dataRoot, "saved-covers");
            settingsPath = Path.Combine(dataRoot, "PixelVault.settings.ini");
            changelogPath = Path.Combine(appRoot, "CHANGELOG.md");
            undoManifestPath = Path.Combine(cacheRoot, "last-import.tsv");
            settingsService = new SettingsService();
            fileSystemService = new FileSystemService();
            coverService = new CoverService(new CoverServiceDependencies
            {
                FileSystem = fileSystemService,
                AppVersion = AppVersion,
                CoversRoot = coversRoot,
                RequestTimeoutMilliseconds = SteamRequestTimeoutMilliseconds,
                GetSteamGridDbApiToken = delegate { return CurrentSteamGridDbApiToken(); },
                NormalizeTitle = delegate(string value) { return NormalizeTitle(value); },
                NormalizeConsoleLabel = delegate(string value) { return NormalizeConsoleLabel(value); },
                SafeCacheName = delegate(string value) { return SafeCacheName(value); },
                StripTags = delegate(string value) { return StripTags(value); },
                Sanitize = delegate(string value) { return Sanitize(value); },
                Log = delegate(string message) { Log(message); },
                LogPerformanceSample = delegate(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds) { LogPerformanceSample(area, stopwatch, detail, thresholdMilliseconds); },
                ClearImageCache = delegate { ClearImageCache(); },
                RemoveCachedImageEntries = delegate(IEnumerable<string> paths) { RemoveCachedImageEntries(paths); }
            });
            (indexPersistenceService, filenameParserService, gameIndexEditorAssignmentService, filenameRulesService) = CreateIndexFilenameRulesServices(cacheRoot, this);
            metadataService = new MetadataService(new MetadataServiceDependencies
            {
                GetExifToolPath = delegate { return exifToolPath; },
                CacheRoot = cacheRoot,
                IsVideo = delegate(string file) { return IsVideo(file); },
                MetadataSidecarPath = delegate(string file) { return MetadataSidecarPath(file); },
                MetadataReadPath = delegate(string file) { return MetadataReadPath(file); },
                BuildMetadataTagSet = delegate(IEnumerable<string> platformTags, IEnumerable<string> extraTags, bool addPhotographyTag) { return BuildMetadataTagSet(platformTags, extraTags, addPhotographyTag); },
                CleanComment = delegate(string value) { return CleanComment(value); },
                CleanTag = delegate(string value) { return CleanTag(value); },
                ParseEmbeddedMetadataDateValue = delegate(string value) { return ParseEmbeddedMetadataDateValue(value); },
                GetMetadataWorkerCount = delegate(int workItems) { return GetMetadataWorkerCount(workItems); },
                Log = delegate(string message) { Log(message); },
                RunExe = delegate(string file, string[] args, string cwd, bool logOutput) { RunExe(file, args, cwd, logOutput); },
                RunExeCapture = delegate(string file, string[] args, string cwd, bool logOutput, CancellationToken cancellationToken) { return RunExeCapture(file, args, cwd, logOutput, cancellationToken); }
            });
            libraryScanner = new LibraryScanner(new LibraryScanHost(this), metadataService, fileSystemService);
            importService = new ImportService(new ImportServiceDependencies
            {
                UndoManifestPath = () => undoManifestPath,
                GetDestinationRoot = () => destinationRoot,
                GetLibraryRoot = () => libraryRoot,
                GetConflictMode = CurrentConflictMode,
                UniquePath = Unique,
                MoveMetadataSidecarIfPresent = MoveMetadataSidecarIfPresent,
                AddSidecarUndoEntryIfPresent = AddSidecarUndoEntryIfPresent,
                Log = Log,
                IsMedia = IsMedia,
                GetSafeGameFolderName = GetSafeGameFolderName,
                GetGameNameFromFileName = GetGameNameFromFileName,
                EnsureDirectoryExists = EnsureDir,
                GetLibraryScanner = () => libraryScanner,
                EnumerateSourceMediaFiles = EnumerateSourceFiles,
                ParseFilenameForImport = ParseFilename,
                EnsureSteamAppIdInGameIndex = EnsureSteamAppIdInGameIndex,
                SanitizeManualRenameGameTitle = Sanitize,
                NormalizeTitleForManualRename = NormalizeTitle,
                FileSystem = fileSystemService,
                MetadataService = metadataService,
                GetFileCreationTime = path => File.GetCreationTime(path),
                GetFileLastWriteTime = path => File.GetLastWriteTime(path),
                GamePhotographyTagLabel = GamePhotographyTag,
                CoverService = coverService,
                NormalizeGameIndexName = name => NormalizeGameIndexName(name),
                DetermineManualMetadataPlatformLabel = DetermineManualMetadataPlatformLabel,
                ManualMetadataChangesGroupingIdentity = ManualMetadataChangesGroupingIdentity,
                GameIndexEditorAssignment = gameIndexEditorAssignmentService,
                BuildManualMetadataGameTitleChoiceLabel = (name, platform) => BuildGameTitleChoiceLabel(name, platform),
                ParseManualMetadataTagText = ParseTagText,
                CleanTag = CleanTag,
                LoadManualMetadataGameTitleRowsAsync = (root, ct) => Task.Factory.StartNew(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    var rows = GetSavedGameIndexRowsForRoot(root);
                    if (rows == null || rows.Count == 0) rows = LoadGameIndexEditorRowsCore(root, null);
                    return rows ?? new List<GameIndexEditorRow>();
                }, ct, TaskCreationOptions.None, TaskScheduler.Default)
            });
            libraryWorkspace = new LibraryWorkspaceContext(this);
            librarySession = new LibrarySession(
                libraryWorkspace,
                libraryScanner,
                fileSystemService,
                gameIndexEditorAssignmentService,
                LoadLibraryMetadataIndex,
                LoadSavedGameIndexRows,
                SaveLibraryMetadataIndex,
                LoadLibraryFolderCacheSnapshot,
                ResolveIndexedLibraryDate,
                BuildResolvedLibraryMetadataIndexEntry,
                RefreshLibraryCoversAsync,
                ShowLibraryMetadataScanWindow,
                EnsureDir);
            importService.AttachLibrarySessionAccessor(() => librarySession);
            gameIndexService = new GameIndexService(new GameIndexServiceDependencies
            {
                LibraryScanner = libraryScanner,
                LibrarySession = librarySession,
                IndexPersistence = indexPersistenceService,
                GameIndexEditorAssignment = gameIndexEditorAssignmentService,
                HostLibraryRoot = () => libraryRoot,
                MergeGameIndexRows = rows => MergeGameIndexRows(rows),
                BuildGameIndexRowsFromFolders = folders => BuildGameIndexRowsFromFolders(folders),
                RefreshCachedLibraryFoldersFromGameIndex = RefreshCachedLibraryFoldersFromGameIndex,
                ApplyEditorSaveRowPolicies = ApplyGameIndexEditorSaveRowPolicies,
                BuildGameIndexSaveAliasMap = BuildGameIndexSaveAliasMap,
                AlignLibraryFoldersToGameIndex = AlignLibraryFoldersToGameIndex,
                RewriteGameIdAliasesInLibraryFolderCacheFile = RewriteGameIdAliasesInLibraryFolderCacheFile
            });
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(logsRoot);
            Directory.CreateDirectory(cacheRoot);
            Directory.CreateDirectory(coversRoot);
            Directory.CreateDirectory(thumbsRoot);
            Directory.CreateDirectory(savedCoversRoot);
            EnsureSavedCoversReadme();
            if (!File.Exists(changelogPath)) File.WriteAllText(changelogPath, "# PixelVault Changelog\r\n\r\n## 0.530\r\n- Replaced the broken library separator glyph with a plain pipe so folder details read cleanly.\r\n- Grouped the Game Library folders into collapsible Steam, PS5, Xbox, Multiple Tags, and Other sections.\r\n- Increased the library folder art size a bit and tightened the caption text underneath for a cleaner browse view.\r\n");
            MigratePersistentDataFromLegacyVersions();
            sourceRoot = @"E:\Game Capture Uploads";
            destinationRoot = @"E:\Game Captures";
            libraryRoot = destinationRoot;
            exifToolPath = Path.Combine(appRoot, "tools", "exiftool.exe");
            ffmpegPath = Path.Combine(appRoot, "tools", "ffmpeg.exe");
            LoadSettings();
            try
            {
                if (!string.IsNullOrWhiteSpace(libraryRoot) && Directory.Exists(libraryRoot)) gameIndexService.GetSavedRowsForRoot(libraryRoot);
            }
            catch (Exception ex)
            {
                Log("Startup: could not preload saved game index for library root. " + ex.Message);
            }

            Title = "PixelVault " + AppVersion;
            Width = PreferredLibraryWindowWidth();
            Height = PreferredLibraryWindowHeight();
            MinWidth = 720;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush("#0F1519");
            var iconPath = Path.Combine(appRoot, "assets", "PixelVault.ico");
            if (File.Exists(iconPath)) Icon = BitmapFrame.Create(new Uri(iconPath));
            Content = new Grid();
            ShowLibraryBrowser(true);
            Log("PixelVault " + AppVersion + " ready.");
        }

        SolidColorBrush Brush(string hex) { return UiBrushHelper.FromHex(hex); }
        Canvas BuildGamepadGlyphCanvas(Brush stroke, double strokeThickness)
        {
            var art = new Canvas { Width = 108, Height = 48 };
            art.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 12 42 C 8 42 5 39 4 33 C 1 23 6 15 12 10 C 17 6 25 4 34 4 L 41 4 C 42 4 43 5 44 6 C 45 7 46 8 48 8 L 60 8 C 62 8 63 7 64 6 C 65 5 66 4 67 4 L 74 4 C 83 4 91 6 96 10 C 102 15 107 23 104 33 C 103 39 100 42 96 42 C 90 42 84 39 78 32 L 69 22 C 66 19 64 18 60 18 L 48 18 C 44 18 42 19 39 22 L 30 32 C 24 39 18 42 12 42 Z"),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            art.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 28 40 L 40 28 C 44 24 47 22 52 22 L 56 22 C 61 22 64 24 68 28 L 80 40"),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            return art;
        }
        FrameworkElement BuildGamepadGlyph(Brush stroke, double strokeThickness, double width, double height)
        {
            return new Viewbox
            {
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform,
                Child = BuildGamepadGlyphCanvas(stroke, strokeThickness)
            };
        }
        FrameworkElement BuildSymbolIcon(string glyph, string foregroundHex, double fontSize)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = fontSize,
                Foreground = Brush(foregroundHex),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }
        object BuildToolbarButtonContent(string glyph, string label, string iconHex = "#D8E4EA")
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border
            {
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 8, 0),
                Child = BuildSymbolIcon(glyph, iconHex, 13)
            });
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }
        string IntakeBadgeCountText(int count)
        {
            if (count <= 0) return string.Empty;
            if (count > 99) return "99+";
            return count.ToString();
        }
        double PreferredLibraryWindowWidth()
        {
            var available = Math.Max(720, SystemParameters.WorkArea.Width - 24);
            return Math.Min(available, 2560);
        }
        double PreferredLibraryWindowHeight()
        {
            var available = Math.Max(520, SystemParameters.WorkArea.Height - 24);
            return Math.Min(available, 1280);
        }
        string ResolveWorkspaceAssetPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            foreach (var basePath in new[]
            {
                Path.Combine(appRoot, "assets"),
                Path.GetFullPath(Path.Combine(appRoot, "..", "..", "assets"))
            }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var candidate = Path.Combine(basePath, fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            return string.Empty;
        }
        /// <summary>Toolbar / intake header bitmap; knocks out near-white backing so the glyph reads on dark chrome.</summary>
        BitmapSource LoadIntakeReviewQueueBitmap()
        {
            var path = ResolveWorkspaceAssetPath("IntakeReviewQueue.png");
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = 192;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                var converted = new FormatConvertedBitmap(bmp, PixelFormats.Pbgra32, null, 0);
                converted.Freeze();
                var w = converted.PixelWidth;
                var h = converted.PixelHeight;
                var stride = w * 4;
                var pixels = new byte[stride * h];
                converted.CopyPixels(pixels, stride, 0);
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var blue = pixels[i];
                    var green = pixels[i + 1];
                    var red = pixels[i + 2];
                    if (red >= 245 && green >= 245 && blue >= 245) pixels[i + 3] = 0;
                }
                var trimmed = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                trimmed.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                trimmed.Freeze();
                return trimmed;
            }
            catch
            {
                return null;
            }
        }
        static bool IsNearWhiteBitmapPixel(byte[] pixels, int offset)
        {
            if (pixels == null || offset < 0 || offset + 3 >= pixels.Length) return false;
            var alpha = pixels[offset + 3];
            if (alpha == 0) return false;
            return pixels[offset] >= 240 && pixels[offset + 1] >= 240 && pixels[offset + 2] >= 240;
        }
        static void RemoveEdgeConnectedNearWhitePixels(byte[] pixels, int width, int height, int stride)
        {
            if (pixels == null || width <= 0 || height <= 0 || stride <= 0) return;
            var visited = new bool[width * height];
            var queue = new Queue<int>();
            void TryEnqueue(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return;
                var index = (y * width) + x;
                if (visited[index]) return;
                var pixelOffset = (y * stride) + (x * 4);
                if (!IsNearWhiteBitmapPixel(pixels, pixelOffset)) return;
                visited[index] = true;
                queue.Enqueue(index);
            }

            for (var x = 0; x < width; x++)
            {
                TryEnqueue(x, 0);
                TryEnqueue(x, height - 1);
            }
            for (var y = 1; y < height - 1; y++)
            {
                TryEnqueue(0, y);
                TryEnqueue(width - 1, y);
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % width;
                var y = index / width;
                var pixelOffset = (y * stride) + (x * 4);
                pixels[pixelOffset + 3] = 0;
                TryEnqueue(x - 1, y);
                TryEnqueue(x + 1, y);
                TryEnqueue(x, y - 1);
                TryEnqueue(x, y + 1);
            }
        }
        static void KeepLargestOpaqueComponent(byte[] pixels, int width, int height, int stride)
        {
            if (pixels == null || width <= 0 || height <= 0 || stride <= 0) return;
            var visited = new bool[width * height];
            List<int> largestComponent = null;
            var queue = new Queue<int>();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var startIndex = (y * width) + x;
                    if (visited[startIndex]) continue;
                    visited[startIndex] = true;
                    var startOffset = (y * stride) + (x * 4);
                    if (pixels[startOffset + 3] == 0) continue;

                    var component = new List<int>();
                    queue.Enqueue(startIndex);
                    while (queue.Count > 0)
                    {
                        var index = queue.Dequeue();
                        component.Add(index);
                        var cx = index % width;
                        var cy = index / width;

                        void TryVisit(int nx, int ny)
                        {
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) return;
                            var neighborIndex = (ny * width) + nx;
                            if (visited[neighborIndex]) return;
                            visited[neighborIndex] = true;
                            var neighborOffset = (ny * stride) + (nx * 4);
                            if (pixels[neighborOffset + 3] == 0) return;
                            queue.Enqueue(neighborIndex);
                        }

                        TryVisit(cx - 1, cy);
                        TryVisit(cx + 1, cy);
                        TryVisit(cx, cy - 1);
                        TryVisit(cx, cy + 1);
                    }

                    if (largestComponent == null || component.Count > largestComponent.Count)
                    {
                        largestComponent = component;
                    }
                }
            }

            if (largestComponent == null || largestComponent.Count == 0) return;

            var keep = new bool[width * height];
            foreach (var index in largestComponent) keep[index] = true;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width) + x;
                    if (keep[index]) continue;
                    var pixelOffset = (y * stride) + (x * 4);
                    pixels[pixelOffset + 3] = 0;
                }
            }
        }
        BitmapSource LoadCompletionBadgeBitmap()
        {
            if (libraryCompletionBadgeBitmap != null) return libraryCompletionBadgeBitmap;
            var path = ResolveWorkspaceAssetPath("100 Percent Medal.png")
                ?? ResolveWorkspaceAssetPath("100 Percent Icon.png");
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelHeight = 256;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                libraryCompletionBadgeBitmap = bmp;
                return libraryCompletionBadgeBitmap;
            }
            catch
            {
                return null;
            }
        }
        string ResolveLibrarySectionIconPath(string platformLabel)
        {
            var normalized = NormalizeConsoleLabel(platformLabel);
            switch (normalized)
            {
                case "Steam":
                    return ResolveWorkspaceAssetPath("Steam Library Icon.jpg");
                case "PS5":
                case "PlayStation":
                    return ResolveWorkspaceAssetPath("PS5 Library Logo.png");
                case "Xbox":
                    return ResolveWorkspaceAssetPath("Xbox Library Logo.png");
                case "Xbox PC":
                    return ResolveWorkspaceAssetPath("Xbox PC Library Icon.png");
                case "PC":
                    return ResolveWorkspaceAssetPath("PC Library Icon.jpg");
                case "Multiple Tags":
                case "Other":
                    return ResolveWorkspaceAssetPath("emulator library licon.png");
                default:
                    return ResolveWorkspaceAssetPath("PixelVault.png");
            }
        }
        SolidColorBrush LibrarySectionAccentBrush(string platformLabel)
        {
            switch (NormalizeConsoleLabel(platformLabel))
            {
                case "Steam":
                    return Brush("#3A8FD6");
                case "PS5":
                case "PlayStation":
                    return Brush("#4E7CFF");
                case "Xbox":
                    return Brush("#69B157");
                case "Xbox PC":
                    return Brush("#5FA77A");
                case "PC":
                    return Brush("#8DA0AF");
                case "Multiple Tags":
                    return Brush("#D39B4A");
                default:
                    return Brush("#B67ECF");
            }
        }
        string LibrarySectionCountLabel(int count)
        {
            return count == 1 ? "game" : "games";
        }
        DateTime GetLibraryFolderNewestDate(LibraryFolderInfo folder)
        {
            if (folder == null) return DateTime.MinValue;
            if (folder.NewestCaptureUtcTicks > 0)
            {
                try
                {
                    return new DateTime(folder.NewestCaptureUtcTicks, DateTimeKind.Utc).ToLocalTime();
                }
                catch
                {
                    folder.NewestCaptureUtcTicks = 0;
                }
            }
            var newestSource = (folder.FilePaths ?? new string[0])
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
            if (string.IsNullOrWhiteSpace(newestSource) && !string.IsNullOrWhiteSpace(folder.PreviewImagePath) && File.Exists(folder.PreviewImagePath))
            {
                newestSource = folder.PreviewImagePath;
            }
            var newest = string.IsNullOrWhiteSpace(newestSource) ? DateTime.MinValue : ResolveIndexedLibraryDate(libraryRoot, newestSource);
            if (newest > DateTime.MinValue) folder.NewestCaptureUtcTicks = newest.ToUniversalTime().Ticks;
            return newest;
        }
        bool PopulateMissingLibraryFolderSortKeys(IEnumerable<LibraryFolderInfo> folders)
        {
            bool changed = false;
            foreach (var folder in (folders ?? Enumerable.Empty<LibraryFolderInfo>()).Where(entry => entry != null))
            {
                if (folder.NewestCaptureUtcTicks <= 0)
                {
                    var newest = GetLibraryFolderNewestDate(folder);
                    if (newest > DateTime.MinValue)
                    {
                        folder.NewestCaptureUtcTicks = newest.ToUniversalTime().Ticks;
                        changed = true;
                    }
                }

                if (folder.NewestRecentSortUtcTicks <= 0)
                {
                    var paths = folder.FilePaths;
                    if (paths != null && paths.Length > 0)
                    {
                        long maxR = 0;
                        foreach (var path in paths)
                        {
                            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                            var t = ResolveLibraryFileRecentSortUtcTicks(libraryRoot, path, null);
                            if (t > maxR) maxR = t;
                        }
                        if (maxR > 0)
                        {
                            folder.NewestRecentSortUtcTicks = maxR;
                            changed = true;
                        }
                    }
                }
            }
            return changed;
        }
        /// <summary>Emits one PERF line when <paramref name="stopwatch"/> elapsed ms is ≥ <paramref name="thresholdMilliseconds"/> (0 = always log).</summary>
        void LogPerformanceSample(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds = 80)
        {
            if (stopwatch == null) return;
            if (stopwatch.ElapsedMilliseconds < thresholdMilliseconds) return;
            var sessionSegment = troubleshootingLoggingEnabled ? " | S=" + _diagnosticsSessionId : string.Empty;
            var detailSegment = string.IsNullOrWhiteSpace(detail) ? string.Empty : " | " + detail;
            Log("PERF | " + area + " | " + stopwatch.ElapsedMilliseconds + " ms | T=" + Environment.CurrentManagedThreadId + sessionSegment + detailSegment);
        }
        FrameworkElement BuildLibraryTilePlatformBadge(string platformLabel)
        {
            var normalized = NormalizeConsoleLabel(platformLabel);
            var iconPath = ResolveLibrarySectionIconPath(normalized);
            var accent = LibrarySectionAccentBrush(normalized);
            var badge = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(11),
                Background = Brush("#F6FAFC"),
                BorderBrush = accent,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8),
                Opacity = 0.96
            };
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                badge.Child = new Image
                {
                    Source = LoadImageSource(iconPath, 68),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                badge.Child = new TextBlock
                {
                    Text = normalized == "Multiple Tags" ? "+" : "•",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("#1D2931"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
            }
            return badge;
        }
        FrameworkElement BuildLibraryTileCompletionBadge()
        {
            var badgeBitmap = LoadCompletionBadgeBitmap();
            if (badgeBitmap != null)
            {
                const double targetHeight = 78;
                var targetWidth = targetHeight;
                if (badgeBitmap.PixelWidth > 0 && badgeBitmap.PixelHeight > 0)
                {
                    targetWidth = Math.Max(44, Math.Min(92, targetHeight * badgeBitmap.PixelWidth / (double)badgeBitmap.PixelHeight));
                }
                return new Image
                {
                    Source = badgeBitmap,
                    Width = targetWidth,
                    Height = targetHeight,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 10, 10, 0),
                    SnapsToDevicePixels = true,
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 14,
                        Direction = 270,
                        ShadowDepth = 4,
                        Opacity = 0.42
                    }
                };
            }
            return new TextBlock
            {
                Text = "100%",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 6,
                    Direction = 270,
                    ShadowDepth = 1,
                    Opacity = 0.75
                }
            };
        }
        FrameworkElement BuildLibrarySectionHeader(string platformLabel, int folderCount, bool sectionCollapsed, Action toggleSectionCollapse)
        {
            var resolvedLabel = NormalizeConsoleLabel(platformLabel);
            var accent = LibrarySectionAccentBrush(resolvedLabel);
            var iconPath = ResolveLibrarySectionIconPath(resolvedLabel);

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevronGlyph = new TextBlock
            {
                Text = sectionCollapsed ? "\uE76C" : "\uE70D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = Brush("#C5D4DE"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var chevronHit = new Border
            {
                Width = 36,
                MinHeight = 48,
                Margin = new Thickness(0, 0, 6, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = chevronGlyph,
                ToolTip = sectionCollapsed ? "Expand section" : "Collapse section"
            };
            if (toggleSectionCollapse != null)
            {
                chevronHit.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    toggleSectionCollapse();
                };
            }
            headerGrid.Children.Add(chevronHit);

            var iconFrame = new Border
            {
                Width = 56,
                Height = 56,
                Margin = new Thickness(0, 0, 14, 0),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(16),
                Background = Brush("#EEF3F6"),
                BorderBrush = accent,
                BorderThickness = new Thickness(1.5),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                iconFrame.Child = new Image
                {
                    Source = LoadImageSource(iconPath, 112),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                iconFrame.Child = new TextBlock
                {
                    Text = resolvedLabel == "Multiple Tags" ? "+" : "•",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("#1D2931"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
            }
            Grid.SetColumn(iconFrame, 1);
            headerGrid.Children.Add(iconFrame);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = resolvedLabel,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new Border
            {
                Width = 78,
                Height = 3,
                CornerRadius = new CornerRadius(999),
                Background = accent,
                Margin = new Thickness(0, 7, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            });
            Grid.SetColumn(titleStack, 2);
            headerGrid.Children.Add(titleStack);

            var countLine = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            countLine.Children.Add(new TextBlock
            {
                Text = folderCount.ToString(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            countLine.Children.Add(new TextBlock
            {
                Text = "\u00A0" + LibrarySectionCountLabel(folderCount),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#9AAAB4"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 1, 0, 0)
            });
            Grid.SetColumn(countLine, 3);
            headerGrid.Children.Add(countLine);

            return new Border
            {
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#1B252B"),
                    (Color)ColorConverter.ConvertFromString("#12191D"),
                    90),
                BorderBrush = Brush("#2A3942"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 12, 18, 12),
                Child = headerGrid
            };
        }
        string FindSteamGridDbApiTokenInEnvironment() => SettingsService.FindSteamGridDbApiTokenInEnvironment();
        string CurrentSteamGridDbApiToken()
        {
            var envToken = FindSteamGridDbApiTokenInEnvironment();
            return !string.IsNullOrWhiteSpace(envToken) ? envToken : (steamGridDbApiToken ?? string.Empty).Trim();
        }
        bool HasSteamGridDbApiToken()
        {
            return !string.IsNullOrWhiteSpace(CurrentSteamGridDbApiToken());
        }
        bool IsClearedExternalIdValue(string value)
        {
            return string.Equals((value ?? string.Empty).Trim(), ClearedExternalIdSentinel, StringComparison.Ordinal);
        }
        string DisplayExternalIdValue(string value)
        {
            return IsClearedExternalIdValue(value) ? string.Empty : CleanTag(value);
        }
        string SerializeExternalIdValue(string value, bool suppressAutoResolve)
        {
            return suppressAutoResolve ? ClearedExternalIdSentinel : CleanTag(value);
        }
        bool ShouldSuppressExternalIdAutoResolve(string editedValue, string previousValue, bool previousSuppressed)
        {
            var cleanedEdited = CleanTag(editedValue);
            if (!string.IsNullOrWhiteSpace(cleanedEdited)) return false;
            return previousSuppressed || !string.IsNullOrWhiteSpace(CleanTag(previousValue));
        }
        double ResolveScrollViewerLayoutWidth(ScrollViewer scrollViewer, double fallback = 0)
        {
            if (scrollViewer == null) return Math.Max(0d, fallback);
            var paddingWidth = scrollViewer.Padding.Left + scrollViewer.Padding.Right;
            var viewportWidth = scrollViewer.ViewportWidth;
            var actualWidth = scrollViewer.ActualWidth;
            var resolved = Math.Max(viewportWidth, Math.Max(0d, actualWidth - paddingWidth));
            if (scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                resolved = Math.Max(0d, resolved - SystemParameters.VerticalScrollBarWidth);
            }
            if (resolved <= 0d && scrollViewer.Parent is FrameworkElement parent)
            {
                resolved = Math.Max(0d, parent.ActualWidth - paddingWidth);
            }
            return resolved <= 0d ? Math.Max(0d, fallback) : resolved;
        }
        int NormalizeLibraryFolderTileSize(int value) => SettingsService.NormalizeLibraryFolderTileSize(value);
        (int Columns, int TileSize) CalculateResponsiveLibraryFolderLayout(ScrollViewer scrollViewer)
        {
            // Match folder cover tile Margin right (horizontal rhythm between columns).
            const int gapPx = 12;
            // Hard cap: at most four covers per row so tiles stay larger and column count does not jump 5–8 on wide layouts.
            const int libraryFolderMaxColumns = 4;
            // When slack is nearly tied on a narrow folder pane, prefer one more column only within this budget (stable wide layouts avoid this).
            const double moreColumnsSlackBudgetPx = 8;
            var viewportWidth = ResolveScrollViewerLayoutWidth(scrollViewer);
            viewportWidth = Math.Max(120, viewportWidth - 18);
            var maxColumnsCeiling = viewportWidth >= 360 ? libraryFolderMaxColumns : 1;
            var preferDenseGridInSplitPane = viewportWidth < 900;
            var minTile = viewportWidth < 260 ? 112 : 140;
            var userCap = NormalizeLibraryFolderTileSize(libraryFolderTileSize);
            const int layoutMaxTile = 1000;
            int TileWidthForColumns(int c, int rawEqualSplit)
            {
                var tileWidth = Math.Max(minTile, Math.Min(layoutMaxTile, rawEqualSplit));
                tileWidth = (int)(Math.Round(tileWidth / 16d) * 16);
                tileWidth = Math.Max(minTile, Math.Min(layoutMaxTile, Math.Min(rawEqualSplit, tileWidth)));
                while (tileWidth > minTile && c * tileWidth + (c - 1) * gapPx > viewportWidth)
                    tileWidth = Math.Max(minTile, tileWidth - 16);
                return tileWidth;
            }
            bool ShouldPreferLayout(double slack, int c, int tileW, double prevSlack, int prevC, int prevTileW)
            {
                if (slack < prevSlack - 0.5) return true;
                if (preferDenseGridInSplitPane && slack <= prevSlack + moreColumnsSlackBudgetPx && c > prevC) return true;
                if (Math.Abs(slack - prevSlack) < 0.5)
                {
                    if (c > prevC) return true;
                    if (c == prevC && tileW > prevTileW) return true;
                    if (c == prevC && tileW != prevTileW && Math.Abs(tileW - userCap) < Math.Abs(prevTileW - userCap)) return true;
                }
                return false;
            }
            var bestColumns = 1;
            var bestTileW = minTile;
            var bestSlack = double.MaxValue;
            for (var c = 1; c <= maxColumnsCeiling; c++)
            {
                var rawTile = (int)Math.Floor((viewportWidth - ((c - 1) * gapPx)) / (double)c);
                if (rawTile < minTile) continue;
                var tileWidth = TileWidthForColumns(c, rawTile);
                var used = c * tileWidth + (c - 1) * gapPx;
                var slack = viewportWidth - used;
                if (!(slack > -0.5)) continue;
                if (!ShouldPreferLayout(slack, c, tileWidth, bestSlack, bestColumns, bestTileW)) continue;
                bestSlack = slack;
                bestColumns = c;
                bestTileW = tileWidth;
            }
            {
                var span = viewportWidth - (bestColumns - 1) * gapPx;
                var rawFill = (int)Math.Floor(span / (double)bestColumns);
                var capped = Math.Max(minTile, Math.Min(userCap, rawFill));
                bestTileW = TileWidthForColumns(bestColumns, capped);
            }
            return (bestColumns, bestTileW);
        }
        (int Columns, int TileSize) CalculateResponsiveLibraryDetailLayout(ScrollViewer scrollViewer)
        {
            const int gapPx = 8;
            const double moreColumnsSlackBudgetPx = 16d;
            const double libraryDetailPhotoTileSizeScale = 1.75d;
            var viewportWidth = ResolveScrollViewerLayoutWidth(scrollViewer);
            viewportWidth = Math.Max(160, viewportWidth - 24);
            var maxColumnsCeiling = viewportWidth >= 1100d ? 4 : (viewportWidth >= 560d ? 3 : (viewportWidth >= 360d ? 2 : 1));
            var minTile = (int)Math.Round((viewportWidth < 420d ? 156 : (viewportWidth < 900d ? 176 : 208)) * libraryDetailPhotoTileSizeScale);
            var layoutMaxTile = (int)Math.Round(900d * libraryDetailPhotoTileSizeScale);

            int ClampRoundedTile(int rawEqualSplit)
            {
                var clamped = Math.Max(minTile, Math.Min(layoutMaxTile, rawEqualSplit));
                var roundedDown = (int)(Math.Floor(clamped / 12d) * 12);
                if (roundedDown < minTile) roundedDown = clamped;
                return Math.Max(minTile, Math.Min(rawEqualSplit, roundedDown));
            }

            var bestColumns = 1;
            var bestTileWidth = ClampRoundedTile((int)Math.Floor(viewportWidth));
            var bestSlack = Math.Max(0d, viewportWidth - bestTileWidth);

            for (var columns = 1; columns <= maxColumnsCeiling; columns++)
            {
                var rawEqualSplit = (int)Math.Floor((viewportWidth - ((columns - 1) * gapPx)) / columns);
                if (rawEqualSplit < minTile) continue;
                var tileWidth = ClampRoundedTile(rawEqualSplit);
                var usedWidth = (columns * tileWidth) + ((columns - 1) * gapPx);
                var slack = Math.Max(0d, viewportWidth - usedWidth);
                if (slack + moreColumnsSlackBudgetPx < bestSlack)
                {
                    bestColumns = columns;
                    bestTileWidth = tileWidth;
                    bestSlack = slack;
                    continue;
                }

                if (slack <= bestSlack + moreColumnsSlackBudgetPx && columns > bestColumns)
                {
                    bestColumns = columns;
                    bestTileWidth = tileWidth;
                    bestSlack = slack;
                }
            }

            return (bestColumns, bestTileWidth);
        }
        string NormalizeLibraryFolderSortMode(string value) => SettingsService.NormalizeLibraryFolderSortMode(value);
        string LibraryFolderSortModeLabel(string value)
        {
            switch (NormalizeLibraryFolderSortMode(value))
            {
                case "recent":
                    return "Recently Added";
                case "photos":
                    return "Most Photos";
                default:
                    return "Platform";
            }
        }
        Button Btn(string t, RoutedEventHandler click, string bg, Brush fg) { var b = new Button { Content = t, Width = 176, Height = 48, Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 12, 12), Foreground = fg, Background = bg != null ? Brush(bg) : Brushes.White, BorderBrush = Brush("#C0CCD6"), BorderThickness = new Thickness(1), FontWeight = FontWeights.SemiBold, Effect = new DropShadowEffect { Color = Color.FromArgb(64, 18, 27, 36), BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.55 } }; if (click != null) b.Click += click; return b; }
        Style LibraryToolbarButtonStyle(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#F4F7FA")
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(foregroundHex)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(backgroundHex)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(borderHex)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 10, 16, 10)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(UIElement.EffectProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildLibraryToolbarButtonTemplate(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex)));
            return style;
        }

        ControlTemplate BuildLibraryToolbarButtonTemplate(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Chrome";
            borderFactory.SetValue(Border.BackgroundProperty, Brush(backgroundHex));
            borderFactory.SetValue(Border.BorderBrushProperty, Brush(borderHex));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.MarginProperty, new Thickness(0));
            borderFactory.AppendChild(contentFactory);
            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush(hoverBackgroundHex), "Chrome"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(hoverBackgroundHex), "Chrome"));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush(pressedBackgroundHex), "Chrome"));
            pressedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brush(pressedBackgroundHex), "Chrome"));
            template.Triggers.Add(pressedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

        Button ApplyLibraryToolbarChrome(Button button, string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#F4F7FA")
        {
            button.Style = LibraryToolbarButtonStyle(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
            button.Cursor = System.Windows.Input.Cursors.Hand;
            button.Height = Math.Max(button.Height, 42);
            button.MinWidth = button.Width;
            return button;
        }
        Button ApplyLibraryPillChrome(Button button, string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#DCE6EC")
        {
            button.Style = LibraryToolbarButtonStyle(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
            button.Cursor = System.Windows.Input.Cursors.Hand;
            button.Height = Math.Max(button.Height, 34);
            button.Padding = new Thickness(12, 7, 12, 7);
            button.FontSize = Math.Max(button.FontSize, 12);
            button.MinWidth = 0;
            return button;
        }
        ControlTemplate BuildRoundedTileButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "TileChrome";
            borderFactory.SetValue(Border.BackgroundProperty, Brush("#151E24"));
            borderFactory.SetValue(Border.BorderBrushProperty, Brush("#25333D"));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            borderFactory.AppendChild(contentFactory);
            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#35515E"), "TileChrome"));
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#19242B"), "TileChrome"));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#436878"), "TileChrome"));
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#11191E"), "TileChrome"));
            template.Triggers.Add(pressedTrigger);

            return template;
        }


        void RefreshMainUi()
        {
            ShowLibraryBrowser(true);
        }

        Window ResolveStatusWindowOwner()
        {
            var activeWindow = Application.Current == null
                ? null
                : Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window != null && window.IsVisible && window.IsActive);
            if (activeWindow != null) return activeWindow;
            return IsLoaded && IsVisible ? this : null;
        }

        void RefreshActiveLibraryFolders(bool forceRefresh)
        {
            var refresh = activeLibraryFolderRefresh;
            if (refresh != null) refresh(forceRefresh);
        }

        string PickFolder(string current)
        {
            using (var dialog = new Forms.OpenFileDialog())
            {
                dialog.Filter = "Folders|*.folder";
                dialog.CheckFileExists = false;
                dialog.ValidateNames = false;
                dialog.DereferenceLinks = true;
                dialog.FileName = "Select this folder";
                if (Directory.Exists(current)) dialog.InitialDirectory = current;
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return null;
                var selected = Path.GetDirectoryName(dialog.FileName);
                return Directory.Exists(selected) ? selected : null;
            }
        }

        string PickFile(string current, string filter, string initialDirectoryFallback = null)
        {
            using (var dialog = new Forms.OpenFileDialog())
            {
                dialog.Filter = filter;
                if (File.Exists(current)) dialog.FileName = current;
                else if (!string.IsNullOrWhiteSpace(initialDirectoryFallback) && Directory.Exists(initialDirectoryFallback)) dialog.InitialDirectory = initialDirectoryFallback;
                return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.FileName : null;
            }
        }

        List<string> GetSourceRoots()
        {
            var raw = sourceRoot ?? string.Empty;
            return raw
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        string PrimarySourceRoot()
        {
            var roots = GetSourceRoots();
            return roots.Count > 0 ? roots[0] : string.Empty;
        }

        string SourceRootsEditorText()
        {
            return string.Join(Environment.NewLine, GetSourceRoots());
        }

        string SourceRootsSummary()
        {
            var roots = GetSourceRoots();
            return roots.Count == 0 ? "(none)" : string.Join(" | ", roots);
        }

        string SerializeSourceRoots(string raw) => SettingsService.SerializeSourceRoots(raw);

        string AppendSourceRoot(string existing, string newPath)
        {
            return SerializeSourceRoots((existing ?? string.Empty) + Environment.NewLine + (newPath ?? string.Empty))
                .Replace(";", Environment.NewLine);
        }

        void EnsureSourceFolders()
        {
            var roots = GetSourceRoots();
            if (roots.Count == 0) throw new InvalidOperationException("Source folders not found: no source folders configured.");
            var missing = roots.Where(path => !Directory.Exists(path)).ToList();
            if (missing.Count > 0) throw new InvalidOperationException("Source folders not found: " + string.Join(" | ", missing));
        }

        IEnumerable<string> EnumerateSourceFiles(SearchOption option, Func<string, bool> predicate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in GetSourceRoots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*", option))
                {
                    if (!predicate(file)) continue;
                    var fullPath = Path.GetFullPath(file);
                    if (seen.Add(fullPath)) yield return fullPath;
                }
            }
        }

        string FindExecutableOnPath(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName)) return null;
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var entry in pathValue.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(entry.Trim(), executableName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            return null;
        }

        void OpenSourceFolders()
        {
            foreach (var root in GetSourceRoots()) OpenFolder(root);
        }

        void ClearLibraryImageCaches()
        {
            libraryWorkspace.ClearFolderImageListings();
            libraryWorkspace.ClearFileTagCache();
            ClearImageCache();
        }
        void RunLibraryMetadataWorkflowWithProgress(LibraryFolderInfo folder, List<ManualMetadataItem> items, Action refreshLibrary)
        {
            var originalSavedGameIndexRow = folder == null ? null : FindSavedGameIndexRow(GetSavedGameIndexRowsForRoot(libraryRoot), folder);
            var totalPerStage = Math.Max(items.Count, 1);
            var totalWork = totalPerStage * 3;
            var folderDisplayName = folder == null ? "selected captures" : (folder.Name ?? "selected captures");
            var closeButton = Btn("Close", null, "#334249", Brushes.White);
            closeButton.IsEnabled = false;
            var libMetaView = WorkflowProgressWindow.Create(
                this,
                "PixelVault " + AppVersion + " Library Metadata Progress",
                "Applying library metadata",
                "Preparing " + items.Count + " capture(s)...",
                0,
                totalWork,
                0,
                false,
                closeButton,
                WorkflowProgressWindow.DefaultMaxLogLines);
            var progressWindow = libMetaView.Window;
            var progressMeta = libMetaView.MetaText;
            var progressBar = libMetaView.ProgressBar;
            bool progressFinished = false;
            Action<string> appendProgress = libMetaView.AppendLogLine;
            closeButton.Click += delegate
            {
                if (!progressFinished) return;
                progressWindow.Close();
            };

            status.Text = "Applying library metadata";
            appendProgress("Starting library metadata apply for " + items.Count + " capture(s) in " + folderDisplayName + ".");
            Action<int, string> updateProgress = delegate(int completed, string detail)
            {
                var safeCompleted = Math.Max(0, Math.Min(completed, totalWork));
                var remaining = Math.Max(totalWork - safeCompleted, 0);
                progressBar.Value = safeCompleted;
                progressMeta.Text = safeCompleted + " of " + totalWork + " steps complete | " + remaining + " remaining";
                appendProgress(detail);
            };
            Action<int, int, int, string> reportStage = delegate(int stageOffset, int current, int stageTotal, string detail)
            {
                var safeStageTotal = Math.Max(stageTotal, 1);
                var safeCurrent = Math.Max(0, Math.Min(current, safeStageTotal));
                progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    updateProgress(Math.Min(stageOffset + safeCurrent, totalWork), detail);
                }));
            };

            System.Threading.Tasks.Task.Factory.StartNew(delegate
            {
                importService.RunManualRename(items, delegate(int current, int total, string detail)
                {
                    reportStage(0, current, total, detail);
                });
                RunManualMetadata(items, delegate(int current, int total, string detail)
                {
                    reportStage(totalPerStage, current, total, detail);
                });
                var moved = OrganizeLibraryItems(items, delegate(int current, int total, string detail)
                {
                    reportStage(totalPerStage * 2, current, total, detail);
                });
                if (librarySession != null && string.Equals(libraryRoot, librarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase))
                    librarySession.UpsertLibraryMetadataIndexEntries(items);
                else
                    libraryScanner.UpsertLibraryMetadataIndexEntries(items, libraryRoot);
                PreserveLibraryMetadataEditGameIndex(libraryRoot, folder, originalSavedGameIndexRow, items);
                progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    progressFinished = true;
                    updateProgress(totalWork, "Library metadata apply complete for " + folderDisplayName + ". Edited " + items.Count + " capture(s); reorganized " + moved + ".");
                    status.Text = moved > 0 ? "Library metadata updated and organized" : "Library metadata updated";

                    if (refreshLibrary != null) refreshLibrary();
                    Log("Library metadata apply complete for " + folderDisplayName + ". Edited " + items.Count + " capture(s); reorganized " + moved + ".");
                    closeButton.IsEnabled = true;
                }));
            }).ContinueWith(delegate(System.Threading.Tasks.Task task)
            {
                if (!task.IsFaulted) return;
                var flattened = task.Exception == null ? null : task.Exception.Flatten();
                var error = flattened == null ? new Exception("Library metadata apply failed.") : flattened.InnerExceptions.First();
                progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    progressFinished = true;
                    progressMeta.Text = error.Message;
                    appendProgress("ERROR: " + error.Message);
                    closeButton.IsEnabled = true;
                    status.Text = "Library metadata failed";
                    LogException("Library metadata apply", error);
                    MessageBox.Show(error.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                }));
            });

            progressWindow.ShowDialog();
        }

        FilenameParseResult ParseFilename(string file)
        {
            return filenameParserService.Parse(file, libraryRoot);
        }

        FilenameParseResult ParseFilename(string file, string root)
        {
            return filenameParserService.Parse(file, string.IsNullOrWhiteSpace(root) ? libraryRoot : root);
        }

        /// <summary>Resolves a game title for sorting/organize. Pass <see cref="Path.GetFileName(string)"/> (with extension) when available so convention rules match; bare stems still work.</summary>
        string GetGameNameFromFileName(string fileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath)) return string.Empty;
            var fileName = fileNameOrPath;
            if (fileName.IndexOf('\\') >= 0 || fileName.IndexOf('/') >= 0)
                fileName = Path.GetFileName(fileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            var parseInput = string.IsNullOrWhiteSpace(Path.GetExtension(fileName))
                ? fileName + ".png"
                : fileName;
            var parsed = filenameParserService.Parse(parseInput, libraryRoot);
            if (!string.IsNullOrWhiteSpace(parsed.GameTitleHint)) return parsed.GameTitleHint.Trim();
            return filenameParserService.GetGameTitleHint(Path.GetFileNameWithoutExtension(parseInput), libraryRoot) ?? string.Empty;
        }

        string GetSafeGameFolderName(string name)
        {
            name = NormalizeGameFolderCapitalization(name ?? string.Empty);
            name = Regex.Replace(name, "[<>:\"/\\\\|?*\\x00-\\x1F]", string.Empty);
            name = name.Trim().TrimEnd('.');
            name = Regex.Replace(name, "\\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(name) ? "Unknown Game" : name;
        }

        string NormalizeGameFolderCapitalization(string name)
        {
            if (Regex.IsMatch(name ?? string.Empty, "[A-Z]") && !Regex.IsMatch(name ?? string.Empty, "[a-z]"))
            {
                return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase((name ?? string.Empty).ToLowerInvariant());
            }
            return name ?? string.Empty;
        }

        List<string> GetCachedFolderImages(string folderPath)
        {
            return libraryWorkspace.GetCachedFolderImages(folderPath);
        }
        List<string> GetFilesForLibraryFolderEntry(LibraryFolderInfo folder, bool imagesOnly)
        {
            if (folder == null) return new List<string>();
            if (folder.FilePaths != null && folder.FilePaths.Length > 0)
            {
                return folder.FilePaths
                    .Where(File.Exists)
                    .Where(file => !imagesOnly || IsImage(file))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            if (string.IsNullOrWhiteSpace(folder.FolderPath) || !Directory.Exists(folder.FolderPath)) return new List<string>();
            IEnumerable<string> files = imagesOnly
                ? GetCachedFolderImages(folder.FolderPath)
                : Directory.EnumerateFiles(folder.FolderPath, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia);
            var desired = NormalizeConsoleLabel(folder.PlatformLabel);
            return files
                .Where(file => NormalizeConsoleLabel(DetermineFolderPlatform(new List<string> { file }, null)) == desired)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        string MetadataSidecarPath(string file)
        {
            return IsVideo(file) ? file + ".xmp" : null;
        }

        long MetadataCacheStamp(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return 0;
            var info = new FileInfo(file);
            long stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;
            var sidecar = MetadataSidecarPath(file);
            if (!string.IsNullOrWhiteSpace(sidecar) && File.Exists(sidecar))
            {
                var sidecarInfo = new FileInfo(sidecar);
                stamp = stamp ^ sidecarInfo.LastWriteTimeUtc.Ticks ^ sidecarInfo.Length;
            }
            return stamp;
        }

        void DeleteMetadataSidecarIfPresent(string file)
        {
            var sidecar = MetadataSidecarPath(file);
            if (string.IsNullOrWhiteSpace(sidecar) || !File.Exists(sidecar)) return;
            File.Delete(sidecar);
            Log("Deleted sidecar: " + sidecar);
        }

        void AddSidecarUndoEntryIfPresent(string targetFile, string sourceDirectory, List<UndoImportEntry> entries)
        {
            var sidecar = MetadataSidecarPath(targetFile);
            if (string.IsNullOrWhiteSpace(sidecar) || !File.Exists(sidecar) || entries == null) return;
            entries.Add(new UndoImportEntry { SourceDirectory = sourceDirectory, ImportedFileName = Path.GetFileName(sidecar), CurrentPath = sidecar });
        }

        bool SameLibraryFolderSelection(LibraryFolderInfo left, LibraryFolderInfo right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            return string.Equals(left.FolderPath ?? string.Empty, right.FolderPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.PlatformLabel ?? string.Empty, right.PlatformLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Name ?? string.Empty, right.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        string ParseSteamGridDbIdFromGamePayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var match = Regex.Match(json, "\"id\"\\s*:\\s*(?<id>\\d+)");
            return match.Success ? match.Groups["id"].Value : null;
        }

        List<Tuple<string, string>> ParseSteamGridDbSearchResults(string json)
        {
            var matches = new List<Tuple<string, string>>();
            if (string.IsNullOrWhiteSpace(json)) return matches;
            foreach (Match match in Regex.Matches(json, "\"id\"\\s*:\\s*(?<id>\\d+)\\s*,\\s*\"name\"\\s*:\\s*\"(?<name>(?:\\\\.|[^\"])*)\"", RegexOptions.Singleline))
            {
                var id = match.Groups["id"].Value;
                var name = Regex.Unescape(match.Groups["name"].Value).Replace("\\/", "/");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                matches.Add(Tuple.Create(id, WebUtility.HtmlDecode(name)));
            }
            return matches;
        }

        string FindBestSteamGridDbSearchMatch(string title, IEnumerable<Tuple<string, string>> matches)
        {
            var candidates = (matches ?? Enumerable.Empty<Tuple<string, string>>()).ToList();
            if (string.IsNullOrWhiteSpace(title) || candidates.Count == 0) return null;
            var wanted = NormalizeTitle(title);
            var exact = candidates.FirstOrDefault(candidate => NormalizeTitle(candidate.Item2) == wanted);
            if (exact != null) return exact.Item1;
            var loose = candidates.Where(candidate =>
            {
                var normalized = NormalizeTitle(candidate.Item2);
                return normalized.StartsWith(wanted + " ", StringComparison.Ordinal)
                    || wanted.StartsWith(normalized + " ", StringComparison.Ordinal)
                    || normalized.Contains(wanted)
                    || wanted.Contains(normalized);
            }).ToList();
            return loose.Count == 1 ? loose[0].Item1 : null;
        }

        async Task<string> ResolveBestLibraryFolderSteamAppIdAsync(string root, LibraryFolderInfo folder, bool allowLookup = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name)) return string.Empty;
            if (folder.SuppressSteamAppIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) return folder.SteamAppId;
            cancellationToken.ThrowIfCancellationRequested();
            var saved = FindSavedGameIndexRow(GetSavedGameIndexRowsForRoot(root), folder);
            if (saved != null && saved.SuppressSteamAppIdAutoResolve)
            {
                folder.SteamAppId = string.Empty;
                folder.SuppressSteamAppIdAutoResolve = true;
                return string.Empty;
            }
            if (saved != null && !string.IsNullOrWhiteSpace(saved.SteamAppId))
            {
                folder.SteamAppId = saved.SteamAppId;
                folder.SuppressSteamAppIdAutoResolve = false;
                return folder.SteamAppId;
            }
            if (!ShouldUseSteamStoreLookups(folder)) return string.Empty;
            if (!allowLookup) return folder.SteamAppId ?? string.Empty;
            var appId = ResolveLibraryFolderSteamAppId(folder.PlatformLabel, folder.FilePaths ?? new string[0]);
            if (string.IsNullOrWhiteSpace(appId)) appId = await coverService.TryResolveSteamAppIdAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(appId))
            {
                folder.SteamAppId = appId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamAppId ?? string.Empty;
        }

        async Task<string> ResolveBestLibraryFolderSteamGridDbIdAsync(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name) || !HasSteamGridDbApiToken()) return string.Empty;
            if (folder.SuppressSteamGridDbIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId)) return folder.SteamGridDbId;
            cancellationToken.ThrowIfCancellationRequested();
            var saved = FindSavedGameIndexRow(GetSavedGameIndexRowsForRoot(root), folder);
            if (saved != null && saved.SuppressSteamGridDbIdAutoResolve)
            {
                folder.SteamGridDbId = string.Empty;
                folder.SuppressSteamGridDbIdAutoResolve = true;
                return string.Empty;
            }
            if (saved != null && !string.IsNullOrWhiteSpace(saved.SteamGridDbId))
            {
                folder.SteamGridDbId = saved.SteamGridDbId;
                folder.SuppressSteamGridDbIdAutoResolve = false;
                return folder.SteamGridDbId;
            }
            string steamGridDbId = null;
            if (ShouldUseSteamStoreLookups(folder))
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                steamGridDbId = !string.IsNullOrWhiteSpace(appId)
                    ? await coverService.TryResolveSteamGridDbIdBySteamAppIdAsync(appId, cancellationToken).ConfigureAwait(false)
                    : null;
            }
            if (string.IsNullOrWhiteSpace(steamGridDbId))
            {
                steamGridDbId = await coverService.TryResolveSteamGridDbIdByNameAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(steamGridDbId))
            {
                folder.SteamGridDbId = steamGridDbId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamGridDbId ?? string.Empty;
        }

        bool ShouldUseSteamStoreLookups(LibraryFolderInfo folder)
        {
            var platform = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            if (string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase)
                || string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var file in (folder == null ? new string[0] : (folder.FilePaths ?? new string[0])).Where(File.Exists).Take(3))
            {
                var parsedPlatform = NormalizeConsoleLabel(ParseFilename(file, libraryRoot).PlatformLabel);
                if (string.Equals(parsedPlatform, "Steam", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parsedPlatform, "PC", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        int EnrichLibraryFoldersWithSteamAppIds(string root, List<LibraryFolderInfo> folders, Action<int, int, string> progress)
        {
            var targetFolders = (folders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .GroupBy(BuildLibraryFolderMasterKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targetFolders.Count == 0) return 0;
            int resolved = 0;
            for (int i = 0; i < targetFolders.Count; i++)
            {
                var folder = targetFolders[i];
                var detailPrefix = "Steam AppID " + (i + 1) + " of " + targetFolders.Count + " | " + folder.Name;
                if (!string.IsNullOrWhiteSpace(folder.SteamAppId))
                {
                    if (progress != null) progress(i + 1, targetFolders.Count, detailPrefix + " | already cached as " + folder.SteamAppId);
                    continue;
                }
                var appId = ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    var matchKey = BuildLibraryFolderMasterKey(folder);
                    foreach (var match in folders.Where(entry => entry != null && string.Equals(BuildLibraryFolderMasterKey(entry), matchKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamAppId = appId;
                    }
                    resolved++;
                    if (progress != null) progress(i + 1, targetFolders.Count, detailPrefix + " | resolved " + appId);
                }
                else
                {
                    if (progress != null) progress(i + 1, targetFolders.Count, detailPrefix + " | no match");
                }
            }
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null || cached.Count == 0)
            {
                RefreshCachedLibraryFoldersFromGameIndex(root);
                return resolved;
            }
            foreach (var updated in folders.Where(entry => entry != null))
            {
                var normalizedGameId = NormalizeGameId(updated.GameId);
                var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                    ? cached.FirstOrDefault(entry => string.Equals(NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (match == null)
                {
                    var folderKey = BuildLibraryFolderMasterKey(updated);
                    match = cached.FirstOrDefault(entry => string.Equals(BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
                }
                if (match == null) continue;
                if (!string.IsNullOrWhiteSpace(updated.SteamAppId)) match.SteamAppId = updated.SteamAppId;
            }
            SaveLibraryFolderCache(root, stamp, cached);
            return resolved;
        }

        bool HasDedicatedLibraryCover(LibraryFolderInfo folder)
        {
            return coverService.HasDedicatedLibraryCover(folder);
        }

        void DeleteCachedCover(string title)
        {
            coverService.DeleteCachedCover(title);
        }

        async Task<string> ForceRefreshLibraryArtAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            var custom = CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom)) return custom;

            var existingCached = CachedCoverPath(folder.Name);
            string backupPath = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(existingCached) && File.Exists(existingCached))
                {
                    backupPath = existingCached + ".bak-" + Guid.NewGuid().ToString("N");
                    fileSystemService.CopyFile(existingCached, backupPath, true);
                }

                var steamDownloaded = await TryDownloadSteamCoverAsync(folder, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(steamDownloaded) && File.Exists(steamDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamDownloaded;
                }

                DeleteCachedCover(folder.Name);
                var steamGridDbDownloaded = await TryDownloadSteamGridDbCoverAsync(folder, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(steamGridDbDownloaded) && File.Exists(steamGridDbDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamGridDbDownloaded;
                }

                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !string.IsNullOrWhiteSpace(existingCached))
                {
                    if (File.Exists(existingCached)) File.Delete(existingCached);
                    File.Move(backupPath, existingCached);
                    RemoveCachedImageEntries(new[] { existingCached });
                    return existingCached;
                }
            }
            catch (Exception ex)
            {
                Log("ForceRefreshLibraryArt failed mid-refresh. " + ex.Message);
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !string.IsNullOrWhiteSpace(existingCached))
                {
                    try
                    {
                        if (File.Exists(existingCached)) File.Delete(existingCached);
                        File.Move(backupPath, existingCached);
                        RemoveCachedImageEntries(new[] { existingCached });
                        return existingCached;
                    }
                    catch (Exception restoreEx)
                    {
                        Log("Custom cover: could not promote backup over cached file. " + restoreEx.Message);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    try
                    {
                        File.Delete(backupPath);
                    }
                    catch (Exception delEx)
                    {
                        Log("Custom cover: could not delete temp backup. " + delEx.Message);
                    }
                }
            }

            return CachedCoverPath(folder.Name);
        }

        async Task<(int resolvedIds, int coversReady)> RefreshLibraryCoversAsync(string root, List<LibraryFolderInfo> libraryFolders, List<LibraryFolderInfo> requestedFolders, Action<int, int, string> progress, CancellationToken cancellationToken, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh)
        {
            var resolvedIds = 0;
            var coversReady = 0;
            var allFolders = (libraryFolders ?? requestedFolders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .ToList();
            var targetFolders = (requestedFolders ?? libraryFolders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .GroupBy(BuildLibraryFolderMasterKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var totalWork = Math.Max(targetFolders.Count * 2, 1);
            if (targetFolders.Count == 0)
            {
                if (progress != null) progress(0, 0, "No library folders available for cover refresh.");
                return (0, 0);
            }
            var completed = 0;
            for (int i = 0; i < targetFolders.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = targetFolders[i];
                var itemLabel = "Game " + (i + 1) + " of " + targetFolders.Count + " | " + folder.Name;
                var hadAppId = !string.IsNullOrWhiteSpace(folder.SteamAppId);
                var hadSteamGridDbId = !string.IsNullOrWhiteSpace(folder.SteamGridDbId);
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(root, folder, cancellationToken).ConfigureAwait(false);
                if ((!hadAppId && !string.IsNullOrWhiteSpace(appId)) || (!hadSteamGridDbId && !string.IsNullOrWhiteSpace(steamGridDbId)))
                {
                    resolvedIds++;
                    var matchKey = BuildLibraryFolderMasterKey(folder);
                    foreach (var match in allFolders.Where(entry => entry != null && string.Equals(BuildLibraryFolderMasterKey(entry), matchKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamAppId = appId;
                        match.SteamGridDbId = steamGridDbId;
                    }
                }
                completed++;
                var idDetail = !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(steamGridDbId)
                    ? "AppID " + appId + " | STID " + steamGridDbId
                    : (!string.IsNullOrWhiteSpace(steamGridDbId)
                        ? "STID " + steamGridDbId
                        : (!string.IsNullOrWhiteSpace(appId) ? "AppID " + appId : "no external ID"));
                if (progress != null) progress(completed, totalWork, itemLabel + " | " + idDetail);
                cancellationToken.ThrowIfCancellationRequested();
                var hasCustomCover = !string.IsNullOrWhiteSpace(CustomCoverPath(folder));
                var hadCachedCover = CachedCoverPath(folder.Name) != null;
                var coverReady = hasCustomCover || hadCachedCover;
                var coverDetail = coverReady ? "cover already present" : "cover missing";
                if (forceRefreshExistingCovers && hadCachedCover && !hasCustomCover)
                {
                    var refreshedCover = await ForceRefreshLibraryArtAsync(folder, cancellationToken).ConfigureAwait(false);
                    coverReady = !string.IsNullOrWhiteSpace(refreshedCover) && File.Exists(refreshedCover);
                    coverDetail = coverReady ? "cover refreshed" : "cover refresh not available";
                }
                else if (forceRefreshExistingCovers && hasCustomCover)
                {
                    coverDetail = "custom cover preserved";
                }
                else if (!coverReady)
                {
                    await ResolveLibraryArtAsync(folder, true, cancellationToken).ConfigureAwait(false);
                    coverReady = HasDedicatedLibraryCover(folder);
                    coverDetail = coverReady ? "cover ready" : "cover not available";
                }
                if (coverReady) coversReady++;
                completed++;
                if (progress != null) progress(completed, totalWork, itemLabel + " | " + coverDetail);
            }
            if (rebuildFullCacheAfterRefresh)
            {
                RefreshCachedLibraryFoldersFromGameIndex(root);
                return (resolvedIds, coversReady);
            }
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null || cached.Count == 0)
            {
                SaveLibraryFolderCache(root, stamp, allFolders);
                return (resolvedIds, coversReady);
            }
            foreach (var updated in allFolders.Where(entry => entry != null))
            {
                var normalizedGameId = NormalizeGameId(updated.GameId);
                var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                    ? cached.FirstOrDefault(entry => string.Equals(NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (match == null)
                {
                    var folderKey = BuildLibraryFolderMasterKey(updated);
                    match = cached.FirstOrDefault(entry => string.Equals(BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
                }
                if (match == null) continue;
                if (!string.IsNullOrWhiteSpace(updated.SteamAppId)) match.SteamAppId = updated.SteamAppId;
                if (!string.IsNullOrWhiteSpace(updated.SteamGridDbId)) match.SteamGridDbId = updated.SteamGridDbId;
            }
            SaveLibraryFolderCache(root, stamp, cached);
            return (resolvedIds, coversReady);
        }

        bool TryGetCustomOrCachedCoverPath(LibraryFolderInfo folder, out string path)
        {
            path = null;
            if (folder == null) return false;
            var custom = CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                path = custom;
                return true;
            }

            var cached = CachedCoverPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        /// <summary>Custom cover, on-disk cache entry, or folder preview path — no network (Library tiles / banner when downloads are off).</summary>
        internal string GetLibraryArtPathForDisplayOnly(LibraryFolderInfo folder)
        {
            if (TryGetCustomOrCachedCoverPath(folder, out var early)) return early;
            return folder?.PreviewImagePath;
        }

        /// <summary>
        /// Resolve a library folder cover path. When <paramref name="allowDownload"/> is false, returns a <b>completed</b> task
        /// (<see cref="GetLibraryArtPathForDisplayOnly"/> only — no network). When true, may download via Steam / SteamGridDB.
        /// </summary>
        Task<string> ResolveLibraryArtAsync(LibraryFolderInfo folder, bool allowDownload, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return Task.FromResult<string>(null);
            if (!allowDownload) return Task.FromResult(GetLibraryArtPathForDisplayOnly(folder));
            return ResolveLibraryArtWithDownloadAsync(folder, cancellationToken);
        }

        async Task<string> ResolveLibraryArtWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken)
        {
            if (TryGetCustomOrCachedCoverPath(folder, out var early)) return early;
            var downloaded = await TryDownloadSteamCoverAsync(folder, cancellationToken).ConfigureAwait(false);
            if (downloaded != null) return downloaded;
            var steamGridDbDownloaded = await TryDownloadSteamGridDbCoverAsync(folder, cancellationToken).ConfigureAwait(false);
            if (steamGridDbDownloaded != null) return steamGridDbDownloaded;
            return folder.PreviewImagePath;
        }

        string CustomCoverKey(LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            try
            {
                var source = (folder.FolderPath ?? string.Empty) + "|" + NormalizeConsoleLabel(folder.PlatformLabel) + "|" + (folder.Name ?? string.Empty);
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(source));
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }

        string CustomCoverPath(LibraryFolderInfo folder)
        {
            return coverService.CustomCoverPath(folder);
        }

        void SaveCustomCover(LibraryFolderInfo folder, string sourcePath)
        {
            coverService.SaveCustomCover(folder, sourcePath);
        }

        void ClearCustomCover(LibraryFolderInfo folder)
        {
            coverService.ClearCustomCover(folder);
        }

        string CachedCoverPath(string title)
        {
            return coverService.CachedCoverPath(title);
        }

        async Task<string> TryDownloadSteamCoverAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            try
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(libraryRoot, folder, true, cancellationToken).ConfigureAwait(false);
                var downloaded = await coverService.TryDownloadSteamCoverAsync(folder.Name, appId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    folder.PreviewImagePath = downloaded;
                    UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                    return downloaded;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("Steam cover download failed for " + (folder == null ? "unknown" : folder.Name) + ". " + ex.Message);
            }
            return null;
        }

        async Task<string> TryDownloadSteamGridDbCoverAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || !HasSteamGridDbApiToken()) return null;
            try
            {
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(libraryRoot, folder, cancellationToken).ConfigureAwait(false);
                var downloaded = await coverService.TryDownloadSteamGridDbCoverAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    folder.PreviewImagePath = downloaded;
                    UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                    return downloaded;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("SteamGridDB cover download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        void UpdateCachedLibraryFolderInfo(string root, LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(root)) return;
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null) return;
            var normalizedGameId = NormalizeGameId(folder.GameId);
            var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                ? cached.FirstOrDefault(entry => string.Equals(NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (match == null)
            {
                var folderKey = BuildLibraryFolderMasterKey(folder);
                match = cached.FirstOrDefault(entry => string.Equals(BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
            }
            if (match == null) return;
            match.GameId = !string.IsNullOrWhiteSpace(normalizedGameId) ? normalizedGameId : match.GameId;
            match.NewestCaptureUtcTicks = folder.NewestCaptureUtcTicks;
            match.NewestRecentSortUtcTicks = folder.NewestRecentSortUtcTicks;
            match.SteamAppId = folder.SteamAppId ?? string.Empty;
            match.SteamGridDbId = folder.SteamGridDbId ?? string.Empty;
            match.SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve;
            match.SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve;
            if (!string.IsNullOrWhiteSpace(folder.PreviewImagePath) && File.Exists(folder.PreviewImagePath))
            {
                match.PreviewImagePath = folder.PreviewImagePath;
            }
            SaveLibraryFolderCache(root, stamp, cached);
            UpsertSavedGameIndexRow(root, folder);
        }

        string PrimaryPlatformLabel(string file)
        {
            return ParseFilename(file).PlatformLabel;
        }

        string FilenameGuessLabel(string file)
        {
            var parsed = ParseFilename(file);
            var appId = parsed.SteamAppId;
            if (!string.IsNullOrWhiteSpace(appId)) return "Steam AppID " + appId;
            if (parsed.RoutesToManualWhenMissingSteamAppId) return "Steam export | AppID needed";
            var label = parsed.PlatformLabel;
            return string.Equals(label, "Other", StringComparison.OrdinalIgnoreCase) ? "No confident match" : label;
        }

        bool IsSteamManualExportWithoutAppId(string file)
        {
            var parsed = ParseFilename(file);
            return parsed.RoutesToManualWhenMissingSteamAppId && string.IsNullOrWhiteSpace(parsed.SteamAppId);
        }

        DateTime? ParseSteamManualExportCaptureDate(string file)
        {
            return IsSteamManualExportWithoutAppId(file) ? ParseFilename(file).CaptureTime : (DateTime?)null;
        }

        int PlatformGroupOrder(string label)
        {
            switch (label)
            {
                case "Steam": return 0;
                case "PS5": return 1;
                case "Xbox": return 2;
                case "Xbox PC": return 3;
                case "PC": return 4;
                case "Multiple Tags": return 5;
                case "Other": return 6;
                default: return 7;
            }
        }

        Brush PreviewBadgeBrush(string label)
        {
            switch (label)
            {
                case "Xbox": return Brush("#2E8B57");
                case "Xbox PC": return Brush("#4D8F68");
                case "Steam": return Brush("#2F6FDB");
                case "PC": return Brush("#4F6D7A");
                case "PS5": return Brush("#2563EB");
                case "PlayStation": return Brush("#2563EB");
                default: return Brush("#8B6F47");
            }
        }

        static int ParseInt(string value) { int result; return int.TryParse(value, out result) ? result : 0; }
        static long ParseLong(string value) { long result; return long.TryParse(value, out result) ? result : 0L; }
        static string FormatFriendlyTimestamp(DateTime value)
        {
            int hour12 = value.Hour % 12;
            if (hour12 == 0) hour12 = 12;
            var suffix = value.Hour >= 12 ? "PM" : "AM";
            return value.Year.ToString("0000") + "-" + value.Month.ToString("00") + "-" + value.Day.ToString("00") + " " + hour12 + ":" + value.Minute.ToString("00") + ":" + value.Second.ToString("00") + " " + suffix;
        }
        static string Sanitize(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-'); return Regex.Replace(s, "\\s+", " ").Trim(); }
        static string CleanComment(string s) { return string.IsNullOrWhiteSpace(s) ? string.Empty : Regex.Replace(s.Replace("\r", " ").Replace("\n", " "), "\\s+", " ").Trim(); }
        internal static string CleanTag(string s) { return string.IsNullOrWhiteSpace(s) ? string.Empty : Regex.Replace(s, "\\s+", " ").Trim(); }
        static string[] ParseTagText(string s) { return (s ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(CleanTag).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
        static bool SameManualText(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        static string Unique(string path) { if (!File.Exists(path)) return path; var dir = Path.GetDirectoryName(path); var name = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path); int i = 2; string candidate; do { candidate = Path.Combine(dir, name + " (" + i + ")" + ext); i++; } while (File.Exists(candidate)); return candidate; }
        static void EnsureDir(string path, string label) { if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new InvalidOperationException(label + " not found: " + path); }
        internal static bool IsImage(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp"; }
        static bool IsPngOrJpeg(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg"; }
        static bool IsVideo(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".mp4" || e == ".mkv" || e == ".avi" || e == ".mov" || e == ".wmv" || e == ".webm"; }
        static bool IsMedia(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return new[] { ".jpg", ".jpeg", ".png", ".webp", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" }.Contains(e); }
        static string Quote(string s) { return s.Contains(" ") ? "\"" + s.Replace("\"", "\\\"") + "\"" : s; }
        DateTime GetLibraryDate(string file)
        {
            var parsed = ParseFilename(file);
            var tags = parsed.PlatformTags ?? new string[0];
            if (!tags.Contains("Xbox"))
            {
                if (parsed.CaptureTime.HasValue) return parsed.CaptureTime.Value;
            }
            var created = File.GetCreationTime(file);
            var modified = File.GetLastWriteTime(file);
            if (created == DateTime.MinValue) return modified;
            if (modified == DateTime.MinValue) return created;
            return created < modified ? created : modified;
        }

        DateTime? ParseCaptureDate(string file)
        {
            return ParseFilename(file).CaptureTime;
        }

        string SafeCacheName(string title) { return Regex.Replace(NormalizeTitle(title), @"\s+", "_"); }

        string NormalizeTitle(string title) { title = WebUtility.HtmlDecode(title ?? string.Empty); title = title.Replace("â„¢", " ").Replace("Â®", " ").Replace("Â©", " ").Replace("_", " ").Replace("-", " ").Replace(":", " "); title = Regex.Replace(title, @"[^\p{L}\p{Nd}]+", " "); return Regex.Replace(title, @"\s+", " ").Trim().ToLowerInvariant(); }
        string StripTags(string html) { return Regex.Replace(html ?? string.Empty, "<.*?>", string.Empty); }

        string ResolvePersistentDataRoot(string currentAppRoot)
        {
            try
            {
                var currentDir = new DirectoryInfo(currentAppRoot);
                if (currentDir != null
                    && currentDir.Parent != null
                    && string.Equals(currentDir.Parent.Name, "dist", StringComparison.OrdinalIgnoreCase)
                    && currentDir.Parent.Parent != null
                    && (Regex.IsMatch(currentDir.Name, @"^PixelVault-\d+\.\d+$", RegexOptions.IgnoreCase)
                        || string.Equals(currentDir.Name, "PixelVault-current", StringComparison.OrdinalIgnoreCase)))
                {
                    return Path.Combine(currentDir.Parent.Parent.FullName, "PixelVaultData");
                }

                var probe = currentDir;
                while (probe != null)
                {
                    var pixelVaultDataPath = Path.Combine(probe.FullName, "PixelVaultData");
                    var sourceProjectPath = Path.Combine(probe.FullName, "src", "PixelVault.Native");
                    if (Directory.Exists(pixelVaultDataPath) && Directory.Exists(sourceProjectPath))
                    {
                        return pixelVaultDataPath;
                    }
                    probe = probe.Parent;
                }
            }
            catch (Exception ex)
            {
                Log("ResolvePersistentDataRoot: " + ex.Message);
            }
            return currentAppRoot;
        }

        void MigratePersistentDataFromLegacyVersions()
        {
            if (string.Equals(dataRoot, appRoot, StringComparison.OrdinalIgnoreCase)) return;
            CopyIfNewerOrMissing(Path.Combine(appRoot, "PixelVault.settings.ini"), settingsPath);
            // Shared PixelVaultData becomes authoritative once it exists; release-local caches
            // can help bootstrap missing files, but they should never roll newer shared index data back.
            CopyDirectoryContentsIfMissing(Path.Combine(appRoot, "cache"), cacheRoot);
            CopyDirectoryContentsIfMissing(Path.Combine(appRoot, "logs"), logsRoot);
            var currentDir = new DirectoryInfo(appRoot);
            var distDir = currentDir == null ? null : currentDir.Parent;
            if (distDir == null || !distDir.Exists) return;
            foreach (var dir in distDir.GetDirectories("PixelVault-*").OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(dir.FullName.TrimEnd(Path.DirectorySeparatorChar), appRoot, StringComparison.OrdinalIgnoreCase)) continue;
                CopyIfNewerOrMissing(Path.Combine(dir.FullName, "PixelVault.settings.ini"), settingsPath);
                CopyDirectoryContentsIfMissing(Path.Combine(dir.FullName, "cache"), cacheRoot);
                CopyDirectoryContentsIfMissing(Path.Combine(dir.FullName, "logs"), logsRoot);
            }
        }

        void CopyIfNewerOrMissing(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath)) return;
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
            if (File.Exists(destinationPath))
            {
                var sourceInfo = new FileInfo(sourcePath);
                var destinationInfo = new FileInfo(destinationPath);
                if (destinationInfo.Length == sourceInfo.Length && destinationInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc) return;
            }
            fileSystemService.CopyFile(sourcePath, destinationPath, true);
        }

        void CopyDirectoryContentsIfNewer(string sourceDirectory, string destinationDirectory)
        {
            if (!Directory.Exists(sourceDirectory)) return;
            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = sourceFile.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                var destinationFile = Path.Combine(destinationDirectory, relative);
                var destinationFolder = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationFolder)) Directory.CreateDirectory(destinationFolder);
                if (File.Exists(destinationFile))
                {
                    var sourceInfo = new FileInfo(sourceFile);
                    var destinationInfo = new FileInfo(destinationFile);
                    if (destinationInfo.Length == sourceInfo.Length && destinationInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc) continue;
                }
                fileSystemService.CopyFile(sourceFile, destinationFile, true);
            }
        }

        void CopyDirectoryContentsIfMissing(string sourceDirectory, string destinationDirectory)
        {
            if (!Directory.Exists(sourceDirectory)) return;
            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = sourceFile.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                var destinationFile = Path.Combine(destinationDirectory, relative);
                if (File.Exists(destinationFile)) continue;
                var destinationFolder = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrWhiteSpace(destinationFolder)) Directory.CreateDirectory(destinationFolder);
                fileSystemService.CopyFile(sourceFile, destinationFile, false);
            }
        }
        void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            var fullPath = Path.GetFullPath(path);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true, Verb = "open" });
            }
            catch (Exception ex)
            {
                Log("OpenFolder primary open failed; trying explorer. " + ex.Message);
                Process.Start(new ProcessStartInfo("explorer.exe", fullPath) { UseShellExecute = true });
            }
        }

        void EnsureSavedCoversReadme()
        {
            try
            {
                var readme = Path.Combine(savedCoversRoot, "README.txt");
                if (File.Exists(readme)) return;
                File.WriteAllText(readme,
                    "My Covers (permanent stash)\r\n" +
                    "\r\n" +
                    "Save or copy cover images here (JPG, PNG, GIF, BMP). Subfolders are fine.\r\n" +
                    "In the library, right-click a game folder, choose Set Custom Cover — the file picker starts here (use Open My Covers Folder in the same menu if you want Explorer).\r\n" +
                    "This folder is not part of the cache; PixelVault will not delete it when refreshing covers.\r\n");
            }
            catch (Exception ex)
            {
                Log("EnsureSavedCoversReadme: " + ex.Message);
            }
        }

        void OpenSavedCoversFolder()
        {
            try
            {
                Directory.CreateDirectory(savedCoversRoot);
                EnsureSavedCoversReadme();
            }
            catch (Exception ex)
            {
                Log("OpenSavedCoversFolder setup: " + ex.Message);
            }
            OpenFolder(savedCoversRoot);
        }

        void OpenPhotoIndexEditor()
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                MessageBox.Show("Library folder not found. Check Settings before opening the photo index.", "PixelVault");
                return;
            }
            if (photoIndexEditorWindow != null)
            {
                if (photoIndexEditorWindow.IsVisible)
                {
                    photoIndexEditorWindow.Activate();
                    return;
                }
                photoIndexEditorWindow = null;
            }

            try
            {
                PhotoIndexEditorHost.Show(
                    this,
                    AppVersion,
                    libraryRoot,
                    w => photoIndexEditorWindow = w,
                    w => { if (ReferenceEquals(photoIndexEditorWindow, w)) photoIndexEditorWindow = null; },
                    new PhotoIndexEditorServices
                    {
                        SetStatus = delegate(string text) { if (status != null) status.Text = text; },
                        Log = Log,
                        CreateButton = Btn,
                        LoadRows = libraryScanner.LoadPhotoIndexEditorRows,
                        SaveRows = libraryScanner.SavePhotoIndexEditorRows,
                        ReadEmbeddedKeywordTagsDirect = path => ReadEmbeddedKeywordTagsDirect(path),
                        DetermineConsoleLabelFromTags = DetermineConsoleLabelFromTags,
                        BuildLibraryMetadataStamp = BuildLibraryMetadataStamp,
                        OpenFolder = OpenFolder,
                        OpenWithShell = OpenWithShell,
                        NormalizeGameId = NormalizeGameId,
                        CleanTag = CleanTag,
                        ParseTagText = ParseTagText
                    });
            }
            catch (Exception ex)
            {
                status.Text = "Photo index unavailable";
                Log("Failed to open photo index. " + ex.Message);
                MessageBox.Show("Could not open the photo index." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault");
            }
        }
        void OpenGameIndexEditor()
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                MessageBox.Show("Library folder not found. Check Settings before opening the game index.", "PixelVault");
                return;
            }
            if (gameIndexEditorWindow != null)
            {
                if (gameIndexEditorWindow.IsVisible)
                {
                    gameIndexEditorWindow.Activate();
                    return;
                }
                gameIndexEditorWindow = null;
            }
            if (gameIndexEditorLoadPending)
            {
                if (status != null) status.Text = "Loading game index...";
                return;
            }

            gameIndexEditorLoadPending = true;
            var requestedLibraryRoot = libraryRoot;
            if (status != null) status.Text = "Loading game index...";
            Task.Factory.StartNew(delegate
            {
                return LoadGameIndexEditorRowsCore(requestedLibraryRoot, null);
            }).ContinueWith(delegate(Task<List<GameIndexEditorRow>> loadTask)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    gameIndexEditorLoadPending = false;
                    if (gameIndexEditorWindow != null && gameIndexEditorWindow.IsVisible)
                    {
                        gameIndexEditorWindow.Activate();
                        if (status != null) status.Text = "Library ready";
                        return;
                    }
                    if (loadTask.IsFaulted)
                    {
                        if (status != null) status.Text = "Game index unavailable";
                        var flattened = loadTask.Exception == null ? null : loadTask.Exception.Flatten();
                        var err = flattened == null ? new Exception("Game index load failed.") : flattened.InnerExceptions.First();
                        LogException("Game index load", err);
                        MessageBox.Show("Could not load the game index." + Environment.NewLine + Environment.NewLine + err.Message, "PixelVault");
                        return;
                    }

                    var preloaded = loadTask.Status == TaskStatus.RanToCompletion && loadTask.Result != null
                        ? loadTask.Result
                        : new List<GameIndexEditorRow>();
                    if (status != null) status.Text = "Library ready";
                    try
                    {
                        GameIndexEditorHost.Show(
                            this,
                            AppVersion,
                            requestedLibraryRoot,
                            w => gameIndexEditorWindow = w,
                            w => { if (ReferenceEquals(gameIndexEditorWindow, w)) gameIndexEditorWindow = null; },
                            new GameIndexEditorServices
                            {
                                SetStatus = delegate(string text) { if (status != null) status.Text = text; },
                                Log = Log,
                                CreateButton = Btn,
                                LoadRows = LoadGameIndexEditorRows,
                                LoadRowsForBackground = root => LoadGameIndexEditorRowsCore(root, null),
                                SaveRows = SaveGameIndexEditorRows,
                                CreateGameId = CreateGameId,
                                NormalizeGameIndexName = NormalizeGameIndexName,
                                NormalizeConsoleLabel = NormalizeConsoleLabel,
                                MergeRows = delegate(List<GameIndexEditorRow> rows) { return MergeGameIndexRows(rows); },
                                BuildMergeKey = BuildGameIndexMergeKey,
                                RunBackgroundWorkflowIntArray = RunBackgroundWorkflowWithProgress<int[]>,
                                ThrowIfWorkflowCancellationRequested = ThrowIfWorkflowCancellationRequested,
                                ResolveMissingSteamAppIdsAsync = ResolveMissingGameIndexSteamAppIdsAsync,
                                ResolveMissingSteamGridDbIdsAsync = ResolveMissingGameIndexSteamGridDbIdsAsync,
                                OpenFolder = OpenFolder
                            },
                            preloaded);
                    }
                    catch (Exception ex)
                    {
                        if (status != null) status.Text = "Game index unavailable";
                        LogException("Open game index", ex);
                        MessageBox.Show("Could not open the game index." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault");
                    }
                }));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        void OpenWithShell(string path) { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }

        string LogFilePath() { return Path.Combine(logsRoot, "PixelVault-native.log"); }
        string TroubleshootingLogFilePath() { return Path.Combine(logsRoot, "PixelVault-troubleshooting.log"); }
        string TryReadLogFile()
        {
            var path = LogFilePath();
            if (!File.Exists(path)) return string.Empty;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException)
                {
                    Thread.Sleep(25 * (attempt + 1));
                }
            }
            return string.Empty;
        }
        static string FormatLogUtcTimestamp()
        {
            return DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        void RotateTroubleshootingLogIfNeeded(string troubleshootingPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(troubleshootingPath) || !File.Exists(troubleshootingPath)) return;
                var length = new FileInfo(troubleshootingPath).Length;
                if (length < TroubleshootingLogMaxBytes) return;
                var rotated = Path.Combine(
                    logsRoot,
                    "PixelVault-troubleshooting-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                File.Move(troubleshootingPath, rotated);
            }
            catch
            {
            }
        }

        void AppendLogFileLine(string path, string line)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(logsRoot);
            lock (logFileSync)
            {
                if (string.Equals(path, TroubleshootingLogFilePath(), StringComparison.OrdinalIgnoreCase))
                    RotateTroubleshootingLogIfNeeded(path);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.WriteLine(line);
                            writer.Flush();
                            return;
                        }
                    }
                    catch (IOException)
                    {
                        if (attempt == 3) return;
                        Thread.Sleep(25 * (attempt + 1));
                    }
                }
            }
        }
        void AppendLogFileLine(string line)
        {
            AppendLogFileLine(LogFilePath(), line);
        }
        void LoadLogView()
        {
            if (logBox == null) return;
            var content = TryReadLogFile();
            logBox.Text = content;
            logBox.ScrollToEnd();
        }
        void Log(string message)
        {
            var line = "[" + FormatLogUtcTimestamp() + "] " + message;
            if (logBox != null)
            {
                Action append = delegate
                {
                    logBox.AppendText(line + Environment.NewLine);
                    logBox.ScrollToEnd();
                };
                if (logBox.Dispatcher.CheckAccess()) append();
                else logBox.Dispatcher.BeginInvoke(append);
            }
            AppendLogFileLine(line);
        }

        /// <summary>Writes a full exception (including stack trace) to the main log with ERROR prefix and managed thread id.</summary>
        internal void LogException(string context, Exception ex)
        {
            if (ex == null) return;
            var prefix = string.IsNullOrWhiteSpace(context) ? string.Empty : context + " | ";
            Log("ERROR | T" + Environment.CurrentManagedThreadId + " | " + prefix + ex);
        }

        string FormatExceptionForTroubleshooting(Exception ex)
        {
            if (ex == null) return string.Empty;
            var s = ex.ToString();
            const int max = 32768;
            if (s.Length > max) s = s.Substring(0, max) + "... (truncated)";
            return s;
        }

        /// <summary>
        /// Strips absolute path-shaped fragments from free text when <see cref="troubleshootingLogRedactPaths"/> is on (exception messages, stack lines, IO errors).
        /// </summary>
        string RedactEmbeddedPathsForTroubleshooting(string text)
        {
            if (string.IsNullOrEmpty(text) || !troubleshootingLogRedactPaths) return text ?? string.Empty;
            try
            {
                var s = text;
                // Quoted Win32 extended paths (common in IO exception text): '\\?\C:\…' or '\\?\UNC\…'
                s = Regex.Replace(
                    s,
                    @"'(?:\\{2}\?\\)([^']*)'",
                    m => "'" + RedactBareWindowsPathForTroubleshooting(m.Groups[1].Value) + "'",
                    RegexOptions.CultureInvariant);
                // Quoted drive-letter paths — regex above stops at spaces; IO messages quote full paths.
                s = Regex.Replace(
                    s,
                    @"'([A-Za-z]:\\[^']*)'",
                    m => "'" + RedactBareWindowsPathForTroubleshooting(m.Groups[1].Value) + "'",
                    RegexOptions.CultureInvariant);
                // Double-quoted drive paths
                s = Regex.Replace(
                    s,
                    @"""([A-Za-z]:\\[^""]*)""",
                    m => "\"" + RedactBareWindowsPathForTroubleshooting(m.Groups[1].Value) + "\"",
                    RegexOptions.CultureInvariant);
                // DIAG-style key=value segments often hold spaced paths; stop at ';' or line end (not at first space).
                s = Regex.Replace(
                    s,
                    @"([A-Za-z_][\w]*=)(\\\\[^;|\r\n]+)",
                    m => m.Groups[1].Value + RedactBareWindowsPathForTroubleshooting(m.Groups[2].Value),
                    RegexOptions.CultureInvariant);
                s = Regex.Replace(
                    s,
                    @"([A-Za-z_][\w]*=)([A-Za-z]:\\[^;|\r\n]+)",
                    m => m.Groups[1].Value + RedactBareWindowsPathForTroubleshooting(m.Groups[2].Value),
                    RegexOptions.CultureInvariant);
                // Long/Win32 extended: \\?\C:\... or \\?\UNC\...
                s = Regex.Replace(s, @"\\{2}\?\\[^\s|""]+", RedactPathMatchForTroubleshooting, RegexOptions.CultureInvariant);
                // Standard UNC \\server\share\...
                s = Regex.Replace(s, @"\\{2}(?!\?\\)[^\s|""]+", RedactPathMatchForTroubleshooting, RegexOptions.CultureInvariant);
                // Drive-letter paths (UTF-16 style); optional forward-slash form — tokens without spaces only
                s = Regex.Replace(s, @"(?<![\w/:])(?:[A-Za-z]:\\[^\s|""]+|[A-Za-z]:/[^\s|""]+)", RedactPathMatchForTroubleshooting, RegexOptions.CultureInvariant);
                return s;
            }
            catch
            {
                return text;
            }
        }

        /// <summary>
        /// Turns a Windows file path into <see cref="FormatPathForTroubleshooting"/> form (e.g. <c>.../LastSegment</c>) when redaction is on.
        /// </summary>
        string RedactBareWindowsPathForTroubleshooting(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
            try
            {
                var t = raw.Trim().Trim('"', '\'');
                t = t.Replace('/', Path.DirectorySeparatorChar);
                return FormatPathForTroubleshooting(t);
            }
            catch
            {
                return "(redacted)";
            }
        }

        string RedactPathMatchForTroubleshooting(Match m)
        {
            if (m == null || string.IsNullOrEmpty(m.Value)) return string.Empty;
            var raw = m.Value.TrimEnd('"', '\'', ')', ']', ',', ';');
            if (string.IsNullOrWhiteSpace(raw)) return m.Value;
            // Stack / compiler snippets like "…\Foo.cs:line 42" or "…\Foo.cs:12" — strip :line so we do not treat ":12" as path.
            if (Regex.IsMatch(raw, @"\.[A-Za-z0-9]{1,12}:\d+$", RegexOptions.CultureInvariant))
                raw = Regex.Replace(raw, @":\d+$", string.Empty, RegexOptions.CultureInvariant);
            try
            {
                return FormatPathForTroubleshooting(raw.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                return "(redacted)";
            }
        }

        /// <summary>
        /// Troubleshooting-only log file shape:
        /// <c>[UTC] DIAG | S&lt;session&gt; | T&lt;managedThreadId&gt; | &lt;Area&gt; | &lt;message&gt;</c>.
        /// When path redaction is enabled, <paramref name="message"/> is passed through <see cref="RedactEmbeddedPathsForTroubleshooting"/> so IO exceptions cannot bypass folder-path redaction via <c>ex.Message</c> / stack text.
        /// </summary>
        void LogTroubleshooting(string area, string message)
        {
            if (!troubleshootingLoggingEnabled) return;
            var safeBody = RedactEmbeddedPathsForTroubleshooting(message ?? string.Empty);
            var line = "[" + FormatLogUtcTimestamp() + "] "
                + "DIAG"
                + " | S=" + _diagnosticsSessionId
                + " | T=" + Environment.CurrentManagedThreadId
                + " | " + (string.IsNullOrWhiteSpace(area) ? "General" : area.Trim())
                + " | " + safeBody;
            AppendLogFileLine(TroubleshootingLogFilePath(), line);
        }

        string FormatPathForTroubleshooting(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (!troubleshootingLogRedactPaths) return path;
            try
            {
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var name = Path.GetFileName(trimmed);
                return string.IsNullOrWhiteSpace(name) ? "(redacted)" : ".../" + name;
            }
            catch
            {
                return "(redacted)";
            }
        }

        /// <summary>
        /// <see cref="LibraryBrowserFolderView.ViewKey"/> can embed a full folder path (e.g. console grouping). When path redaction is on, only path-like pipe segments are shortened.
        /// </summary>
        string FormatViewKeyForTroubleshooting(string viewKey)
        {
            if (string.IsNullOrWhiteSpace(viewKey) || !troubleshootingLogRedactPaths) return viewKey ?? string.Empty;
            var parts = viewKey.Split('|');
            for (var i = 0; i < parts.Length; i++)
            {
                if (TroubleshootingSegmentLooksLikePath(parts[i]))
                    parts[i] = FormatPathForTroubleshooting(parts[i]);
            }
            return string.Join("|", parts);
        }

        static bool TroubleshootingSegmentLooksLikePath(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return false;
            if (segment.IndexOf(Path.DirectorySeparatorChar) >= 0) return true;
            if (segment.IndexOf(Path.AltDirectorySeparatorChar) >= 0) return true;
            if (segment.StartsWith("\\\\", StringComparison.Ordinal)) return true;
            return segment.Length >= 2 && char.IsLetter(segment[0]) && segment[1] == ':';
        }
    }
}

















































































