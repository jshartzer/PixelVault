using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Windows.Documents;
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
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)3072 |
                (SecurityProtocolType)768 |
                SecurityProtocolType.Tls;
            Batteries_V2.Init();
            new Application().Run(new MainWindow());
        }
    }

    public sealed partial class MainWindow : Window
    {
        const string AppVersion = "0.845";
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
        readonly Dictionary<string, BitmapImage> imageCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<string> imageCacheOrder = new LinkedList<string>();
        readonly Dictionary<string, LinkedListNode<string>> imageCacheOrderNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        readonly object imageCacheSync = new object();
        readonly SemaphoreSlim imageLoadLimiter = new SemaphoreSlim(Math.Max(2, Math.Min(Environment.ProcessorCount, 6)));
        readonly SemaphoreSlim priorityImageLoadLimiter = new SemaphoreSlim(Math.Max(1, Math.Min(Environment.ProcessorCount, 3)));
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
        readonly LibraryWorkspaceContext libraryWorkspace;

        internal string LibraryWorkspaceRoot => libraryRoot;
        string exifToolPath;
        string ffmpegPath;
        string steamGridDbApiToken;
        int libraryFolderTileSize = 240;
        string libraryFolderSortMode = "platform";
        string libraryGroupingMode = "all";
        string _libraryBrowserPersistedSearch = string.Empty;
        string _libraryBrowserPersistedLastViewKey = string.Empty;
        double _libraryBrowserPersistedFolderScroll;
        double _libraryBrowserPersistedDetailScroll;
        Action<bool> activeLibraryFolderRefresh;
        LibraryFolderInfo activeSelectedLibraryFolder;

        RichTextBox previewBox;
        TextBox logBox;
        TextBlock status;
        CheckBox recurseBox, keywordsBox;
        /// <summary>Thread-safe mirror of keywordsBox.IsChecked for background metadata (avoid Dispatcher.Invoke).</summary>
        volatile bool _includeGameCaptureKeywordsMirror;
        ComboBox conflictBox;
        Window photoIndexEditorWindow;
        Window gameIndexEditorWindow;
        bool gameIndexEditorLoadPending;
        Window filenameConventionEditorWindow;
        int previewRefreshVersion;
        CancellationTokenSource previewRefreshCancellation;
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

        sealed class LibraryDetailRenderSnapshot
        {
            public List<LibraryDetailRenderGroup> Groups = new List<LibraryDetailRenderGroup>();
            public List<string> VisibleFiles = new List<string>();
        }

        sealed class LibraryDetailRenderGroup
        {
            public DateTime CaptureDate;
            public List<string> Files = new List<string>();
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
                CaptureUtcTicks = entry.CaptureUtcTicks
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
                SteamAppId = folder.SteamAppId,
                SteamGridDbId = folder.SteamGridDbId,
                SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve
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
            dataRoot = ResolvePersistentDataRoot(appRoot);
            logsRoot = Path.Combine(dataRoot, "logs");
            cacheRoot = Path.Combine(dataRoot, "cache");
            coversRoot = Path.Combine(cacheRoot, "covers");
            thumbsRoot = Path.Combine(cacheRoot, "thumbs");
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
            indexPersistenceService = new IndexPersistenceService(new IndexPersistenceServiceDependencies
            {
                CacheRoot = cacheRoot,
                SafeCacheName = delegate(string value) { return SafeCacheName(value); },
                NormalizeGameId = delegate(string value) { return NormalizeGameId(value); },
                NormalizeGameIndexName = delegate(string value) { return NormalizeGameIndexName(value); },
                NormalizeConsoleLabel = delegate(string value) { return NormalizeConsoleLabel(value); },
                DisplayExternalIdValue = delegate(string value) { return DisplayExternalIdValue(value); },
                IsClearedExternalIdValue = delegate(string value) { return IsClearedExternalIdValue(value); },
                SerializeExternalIdValue = delegate(string value, bool suppressAutoResolve) { return SerializeExternalIdValue(value, suppressAutoResolve); },
                MergeGameIndexRows = delegate(IEnumerable<GameIndexEditorRow> rows) { return MergeGameIndexRows(rows); },
                BuildGameIdAliasMap = delegate(IEnumerable<GameIndexEditorRow> sourceRows, IEnumerable<GameIndexEditorRow> normalizedRows) { return BuildGameIdAliasMap(sourceRows, normalizedRows); },
                HasGameIdAliasChanges = delegate(Dictionary<string, string> aliasMap) { return HasGameIdAliasChanges(aliasMap); },
                ParseInt = delegate(string value) { return ParseInt(value); },
                ParseTagText = delegate(string value) { return ParseTagText(value); },
                DetermineConsoleLabelFromTags = delegate(IEnumerable<string> tags) { return DetermineConsoleLabelFromTags(tags); },
                RewriteGameIdAliasesInLibraryFolderCacheFile = delegate(string root, Dictionary<string, string> aliasMap) { RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap); },
                ApplyGameIdAliasesToCachedMetadataIndex = delegate(string root, Dictionary<string, string> aliasMap) { ApplyGameIdAliasesToCachedMetadataIndex(root, aliasMap); }
            });
            filenameParserService = new FilenameParserService(new FilenameParserServiceDependencies
            {
                LoadCustomConventions = delegate(string root) { return indexPersistenceService.LoadFilenameConventions(root); },
                LoadSavedGameIndexRows = delegate(string root) { return indexPersistenceService.LoadSavedGameIndexRows(root); },
                NormalizeGameIndexName = delegate(string value) { return NormalizeGameIndexName(value); },
                ParseTagText = delegate(string value) { return ParseTagText(value); },
                IsVideo = delegate(string file) { return IsVideo(file); },
                NormalizeConsoleLabel = delegate(string value) { return NormalizeConsoleLabel(value); }
            });
            gameIndexEditorAssignmentService = new GameIndexEditorAssignmentService(
                indexPersistenceService,
                filenameParserService,
                (name, folderPath) => NormalizeGameIndexName(name, folderPath),
                value => NormalizeConsoleLabel(value ?? string.Empty),
                value => NormalizeGameId(value ?? string.Empty),
                value => CleanTag(value ?? string.Empty),
                ids => CreateGameId(ids));
            filenameRulesService = new FilenameRulesService(new FilenameRulesServiceDependencies
            {
                GetConventionRules = delegate(string root) { return filenameParserService.GetConventionRules(root); },
                LoadSamples = delegate(string root, int maxCount) { return indexPersistenceService.LoadFilenameConventionSamples(root, maxCount); },
                SaveConventions = delegate(string root, IEnumerable<FilenameConventionRule> rules) { indexPersistenceService.SaveFilenameConventions(root, rules); },
                InvalidateRules = delegate(string root) { filenameParserService.InvalidateRules(root); },
                DeleteSamples = delegate(string root, IEnumerable<long> sampleIds) { indexPersistenceService.DeleteFilenameConventionSamples(root, sampleIds); },
                BuildCustomRuleFromSample = delegate(FilenameConventionSample sample) { return BuildCustomFilenameConventionFromSample(sample); },
                ParseTagText = delegate(string value) { return ParseTagText(value); },
                NormalizeConsoleLabel = delegate(string value) { return NormalizeConsoleLabel(value); },
                DefaultPlatformTagsTextForLabel = delegate(string value) { return DefaultPlatformTagsTextForLabel(value); },
                CleanTag = delegate(string value) { return CleanTag(value); }
            });
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
                CleanTag = CleanTag
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
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(logsRoot);
            Directory.CreateDirectory(cacheRoot);
            Directory.CreateDirectory(coversRoot);
            Directory.CreateDirectory(thumbsRoot);
            Directory.CreateDirectory(savedCoversRoot);
            EnsureSavedCoversReadme();
            if (!File.Exists(changelogPath)) File.WriteAllText(changelogPath, "# PixelVault Changelog\r\n\r\n## 0.530\r\n- Replaced the broken library separator glyph with a plain pipe so folder details read cleanly.\r\n- Grouped the Game Library folders into collapsible Steam, PS5, Xbox, Multiple Tags, and Other sections.\r\n- Increased the library folder art size a bit and tightened the caption text underneath for a cleaner browse view.\r\n");
            MigratePersistentDataFromLegacyVersions();
            sourceRoot = @"Y:\Game Capture Uploads";
            destinationRoot = @"Y:\Game Captures";
            libraryRoot = destinationRoot;
            exifToolPath = Path.Combine(appRoot, "tools", "exiftool.exe");
            ffmpegPath = Path.Combine(appRoot, "tools", "ffmpeg.exe");
            LoadSettings();
            try
            {
                if (!string.IsNullOrWhiteSpace(libraryRoot) && Directory.Exists(libraryRoot)) LoadSavedGameIndexRows(libraryRoot);
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
        FrameworkElement BuildGamepadGlyph(Brush stroke, double strokeThickness, double width, double height)
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
            return new Viewbox
            {
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform,
                Child = art
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
        double PreferredSettingsWindowHeight()
        {
            var available = Math.Max(820, SystemParameters.WorkArea.Height - 32);
            return Math.Min(available, 1480);
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
                if (folder.NewestCaptureUtcTicks > 0) continue;
                var newest = GetLibraryFolderNewestDate(folder);
                if (newest > DateTime.MinValue)
                {
                    folder.NewestCaptureUtcTicks = newest.ToUniversalTime().Ticks;
                    changed = true;
                }
            }
            return changed;
        }
        void LogPerformanceSample(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds = 80)
        {
            if (stopwatch == null) return;
            if (stopwatch.ElapsedMilliseconds < thresholdMilliseconds) return;
            Log("PERF | " + area + " | " + stopwatch.ElapsedMilliseconds + " ms" + (string.IsNullOrWhiteSpace(detail) ? string.Empty : " | " + detail));
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
        int NormalizeLibraryFolderTileSize(int value) => SettingsService.NormalizeLibraryFolderTileSize(value);
        (int Columns, int TileSize) CalculateResponsiveLibraryFolderLayout(ScrollViewer scrollViewer)
        {
            var viewportWidth = scrollViewer == null ? 0 : scrollViewer.ViewportWidth;
            if (viewportWidth <= 0 && scrollViewer != null) viewportWidth = scrollViewer.ActualWidth;
            viewportWidth = Math.Max(120, viewportWidth - 18);
            // Four columns once the pane is wide enough for ~4× min tile + gutters (default splitter favors the browser pane).
            var columns = viewportWidth >= 700 ? 4 : (viewportWidth >= 600 ? 3 : (viewportWidth >= 400 ? 2 : 1));
            var tileWidth = (int)Math.Floor((viewportWidth - ((columns - 1) * 14)) / columns);
            var minTile = viewportWidth < 260 ? 112 : 140;
            tileWidth = Math.Max(minTile, Math.Min(360, tileWidth));
            tileWidth = Math.Max(minTile, Math.Min(360, (int)(Math.Round(tileWidth / 16d) * 16)));
            return (columns, tileWidth);
        }
        (int Columns, int TileSize) CalculateResponsiveLibraryDetailLayout(ScrollViewer scrollViewer)
        {
            var viewportWidth = scrollViewer == null ? 0 : scrollViewer.ViewportWidth;
            if (viewportWidth <= 0 && scrollViewer != null) viewportWidth = scrollViewer.ActualWidth;
            viewportWidth = Math.Max(120, viewportWidth - 64);
            var columns = viewportWidth >= 1450 ? 3 : (viewportWidth >= 780 ? 2 : 1);
            var tileWidth = (int)Math.Floor((viewportWidth - ((columns - 1) * 8)) / columns);
            var minTile = viewportWidth < 340 ? 120 : 180;
            tileWidth = Math.Max(minTile, Math.Min(680, tileWidth));
            tileWidth = Math.Max(minTile, Math.Min(680, (int)(Math.Round(tileWidth / 24d) * 24)));
            return (columns, tileWidth);
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
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));

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

        void ShowLibraryMetadataScanWindow(Window owner, string root, string folderPath, bool forceRescan, Action<bool> setBusyState = null, Action onSuccess = null)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show("Library folder not found. Check Settings before running a metadata rebuild.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Window progressWindow = null;
            TextBlock progressMeta = null;
            ProgressBar progressBar = null;
            Action<string> appendProgress = null;
            Button actionButton = null;
            bool scanFinished = false;
            CancellationTokenSource scanCancellation = null;
            Action finishButtons = delegate
            {
                if (setBusyState != null) setBusyState(false);
                System.Windows.Input.Mouse.OverrideCursor = null;
            };

            try
            {
                var resolvedOwner = owner != null && owner.IsVisible ? owner : ResolveStatusWindowOwner();
                var scopeLabel = string.IsNullOrWhiteSpace(folderPath)
                    ? (forceRescan ? "full library rebuild" : "full library refresh")
                    : ((Path.GetFileName(folderPath) ?? "selected folder") + (forceRescan ? " rebuild" : " refresh"));
                var scanHeading = string.IsNullOrWhiteSpace(folderPath)
                    ? (forceRescan ? "Rebuilding library metadata index" : "Refreshing library metadata index")
                    : (forceRescan ? "Rebuilding folder metadata index" : "Refreshing folder metadata index");
                actionButton = Btn("Cancel Scan", null, "#7A2F2F", Brushes.White);
                var scanProgressView = WorkflowProgressWindow.Create(
                    resolvedOwner,
                    "PixelVault Scan Monitor",
                    scanHeading,
                    "Building file list...",
                    0,
                    1,
                    0,
                    true,
                    actionButton,
                    WorkflowProgressWindow.ScanStyleMaxLogLines);
                progressWindow = scanProgressView.Window;
                progressMeta = scanProgressView.MetaText;
                progressBar = scanProgressView.ProgressBar;
                appendProgress = scanProgressView.AppendLogLine;
                actionButton.Click += delegate
                {
                    if (!scanFinished)
                    {
                        if (scanCancellation != null && !scanCancellation.IsCancellationRequested) scanCancellation.Cancel();
                        actionButton.IsEnabled = false;
                        if (progressMeta != null) progressMeta.Text = "Cancel requested. Stopping the current metadata read...";
                        appendProgress("Cancel requested. Stopping the current metadata read.");
                    }
                    else if (progressWindow != null)
                    {
                        progressWindow.Close();
                    }
                };
                progressWindow.Show();

                appendProgress("Starting scan for " + scopeLabel + ".");
                if (status != null)
                {
                    status.Text = string.IsNullOrWhiteSpace(folderPath)
                        ? (forceRescan ? "Rebuilding library metadata index" : "Refreshing library metadata index")
                        : (forceRescan ? "Rebuilding folder metadata index" : "Refreshing folder metadata index");
                }
                if (setBusyState != null) setBusyState(true);
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                var capturedFolderPath = folderPath;
                var capturedForceRescan = forceRescan;
                scanCancellation = new CancellationTokenSource();
                libraryScanner.ScanLibraryMetadataIndexAsync(root, capturedFolderPath, capturedForceRescan, delegate(int currentCount, int totalCount, string detail)
                {
                    if (progressWindow == null) return;
                    progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (progressBar == null || progressMeta == null) return;
                        progressBar.IsIndeterminate = totalCount <= 0;
                        if (totalCount > 0)
                        {
                            progressBar.Maximum = totalCount;
                            progressBar.Value = Math.Min(currentCount, totalCount);
                            var remaining = Math.Max(totalCount - currentCount, 0);
                            progressMeta.Text = currentCount + " of " + totalCount + " processed | " + remaining + " remaining";
                        }
                        else
                        {
                            progressMeta.Text = detail;
                        }
                        appendProgress(detail);
                    }));
                }, scanCancellation.Token).ContinueWith(delegate(System.Threading.Tasks.Task<int> scanTask)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        scanFinished = true;
                        if (scanCancellation != null)
                        {
                            scanCancellation.Dispose();
                            scanCancellation = null;
                        }
                        finishButtons();
                        if (scanTask.IsCanceled || (scanTask.IsFaulted && scanTask.Exception != null && scanTask.Exception.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException)))
                        {
                            if (status != null) status.Text = "Library scan cancelled";
                            if (progressMeta != null) progressMeta.Text = "Scan cancelled before completion.";
                            appendProgress("Scan cancelled.");
                        }
                        else if (scanTask.IsFaulted)
                        {
                            if (status != null) status.Text = "Library scan failed";
                            var flattened = scanTask.Exception == null ? null : scanTask.Exception.Flatten();
                            var scanError = flattened == null ? new Exception("Library scan failed.") : flattened.InnerExceptions.First();
                            if (progressMeta != null) progressMeta.Text = scanError.Message;
                            appendProgress("ERROR: " + scanError.Message);
                            Log(scanError.ToString());
                            MessageBox.Show(scanError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            if (status != null)
                            {
                                status.Text = string.IsNullOrWhiteSpace(folderPath)
                                    ? (forceRescan ? "Library metadata index rebuilt" : "Library metadata index refreshed")
                                    : (forceRescan ? "Folder metadata index rebuilt" : "Folder metadata index refreshed");
                            }
                            if (progressMeta != null) progressMeta.Text += " | complete";
                            appendProgress("Scan finished successfully.");
                            if (onSuccess != null) onSuccess();
                        }
                        if (actionButton != null)
                        {
                            actionButton.IsEnabled = true;
                            actionButton.Content = "Close";
                        }
                    }));
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                scanFinished = true;
                if (scanCancellation != null)
                {
                    scanCancellation.Dispose();
                    scanCancellation = null;
                }
                finishButtons();
                if (status != null) status.Text = "Library scan failed";
                Log(ex.ToString());
                if (progressMeta != null) progressMeta.Text = ex.Message;
                if (appendProgress != null) appendProgress("ERROR: " + ex.Message);
                if (actionButton != null)
                {
                    actionButton.IsEnabled = true;
                    actionButton.Content = "Close";
                }
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        List<string> BuildImportSummaryLines(string workflowLabel, bool usedReview, RenameStepResult renameResult, DeleteStepResult deleteResult, MetadataStepResult metadataResult, MoveStepResult moveResult, SortStepResult sortResult, int manualItemsLeft, bool manualItemsLeftAreUploadSkips = false)
        {
            var lines = new List<string>();
            lines.Add("Workflow: " + workflowLabel + (usedReview ? " with review window." : "."));
            lines.Add("Rename summary: renamed " + (renameResult == null ? 0 : renameResult.Renamed) + ", skipped " + (renameResult == null ? 0 : renameResult.Skipped) + ".");
            if (deleteResult != null && (usedReview || deleteResult.Deleted > 0 || deleteResult.Skipped > 0))
            {
                lines.Add("Delete summary: deleted " + deleteResult.Deleted + ", skipped " + deleteResult.Skipped + ".");
            }
            lines.Add("Metadata summary: updated " + (metadataResult == null ? 0 : metadataResult.Updated) + ", skipped " + (metadataResult == null ? 0 : metadataResult.Skipped) + ".");
            lines.Add("Move summary: moved " + (moveResult == null ? 0 : moveResult.Moved) + ", skipped " + (moveResult == null ? 0 : moveResult.Skipped) + ", renamed-on-conflict " + (moveResult == null ? 0 : moveResult.RenamedOnConflict) + ".");
            if (sortResult == null)
            {
                lines.Add("Sort summary: skipped because no files were imported into the destination root.");
            }
            else
            {
                lines.Add("Sort summary: sorted " + sortResult.Sorted + ", folders created " + sortResult.FoldersCreated + ", renamed-on-conflict " + sortResult.RenamedOnConflict + ".");
            }
            if (manualItemsLeftAreUploadSkips)
            {
                if (manualItemsLeft > 0)
                {
                    lines.Add("Upload folder: " + manualItemsLeft + " file(s) were not selected for this import and remain in the upload folder.");
                }
                else
                {
                    lines.Add("Upload folder: every listed file was included in this import (none left unselected).");
                }
            }
            else if (manualItemsLeft > 0)
            {
                lines.Add("Manual Intake queue: left " + manualItemsLeft + " unmatched image(s) untouched.");
            }
            else
            {
                lines.Add("Manual Intake queue: no unmatched image(s) waiting.");
            }
            return lines;
        }

        void ShowImportSummaryWindow(string title, string meta, IEnumerable<string> lines)
        {
            var owner = ResolveStatusWindowOwner();
            var summaryWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " " + title,
                Width = 900,
                Height = 580,
                MinWidth = 780,
                MinHeight = 520,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };
            if (owner != null) summaryWindow.Owner = owner;

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var summaryTitle = new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var summaryMeta = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(meta) ? "Import work completed." : meta,
                Foreground = Brush("#B7C6C0"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            var summaryLog = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Background = Brush("#12191E"),
                Foreground = Brush("#F1E9DA"),
                BorderBrush = Brush("#2B3A44"),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Cascadia Mono"),
                Text = string.Join(Environment.NewLine, (lines ?? Enumerable.Empty<string>()).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray())
            };
            var closeButton = Btn("Close", null, "#334249", Brushes.White);
            closeButton.Margin = new Thickness(0);
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.Click += delegate { summaryWindow.Close(); };

            root.Children.Add(summaryTitle);
            Grid.SetRow(summaryMeta, 1);
            root.Children.Add(summaryMeta);

            var logBorder = new Border
            {
                Background = Brush("#12191E"),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                BorderBrush = Brush("#26363F"),
                BorderThickness = new Thickness(1),
                Child = summaryLog
            };
            Grid.SetRow(logBorder, 2);
            root.Children.Add(logBorder);

            Grid.SetRow(closeButton, 3);
            root.Children.Add(closeButton);

            summaryWindow.Content = root;
            summaryWindow.ShowDialog();
        }

        TextBox SettingsTextBox(Grid panel, int row, string label, string value)
        {
            while (panel.RowDefinitions.Count <= row) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, Margin = new Thickness(0, row == 0 ? 0 : 12, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            panel.Children.Add(text);
            var box = new TextBox { Text = value, Margin = new Thickness(0, row == 0 ? 0 : 12, 12, 0), Padding = new Thickness(8) };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            panel.Children.Add(box);
            return box;
        }

        void SettingsBrowseButton(Grid panel, int row, RoutedEventHandler click, string label = "Browse")
        {
            var button = Btn(label, click, null, Brushes.Black);
            button.Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, 2);
            panel.Children.Add(button);
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

        void ClearImageCache()
        {
            lock (imageCacheSync)
            {
                imageCache.Clear();
                imageCacheOrder.Clear();
                imageCacheOrderNodes.Clear();
            }
        }

        void RemoveCachedImageEntries(IEnumerable<string> sourcePaths)
        {
            var normalizedPaths = new HashSet<string>(
                (sourcePaths ?? Enumerable.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path =>
                    {
                        try
                        {
                            return Path.GetFullPath(path);
                        }
                        catch
                        {
                            return path;
                        }
                    }),
                StringComparer.OrdinalIgnoreCase);
            if (normalizedPaths.Count == 0) return;
            lock (imageCacheSync)
            {
                var keysToRemove = imageCache.Keys
                    .Where(cacheKey =>
                    {
                        if (string.IsNullOrWhiteSpace(cacheKey)) return false;
                        var separatorIndex = cacheKey.IndexOf('|');
                        var cachedPath = separatorIndex >= 0 ? cacheKey.Substring(0, separatorIndex) : cacheKey;
                        return normalizedPaths.Contains(cachedPath);
                    })
                    .ToList();
                foreach (var cacheKey in keysToRemove)
                {
                    imageCache.Remove(cacheKey);
                    LinkedListNode<string> node;
                    if (imageCacheOrderNodes.TryGetValue(cacheKey, out node) && node != null)
                    {
                        imageCacheOrder.Remove(node);
                    }
                    imageCacheOrderNodes.Remove(cacheKey);
                }
            }
        }

        void RemoveCachedFolderListings(IEnumerable<string> folderPaths)
        {
            libraryWorkspace.RemoveFolderImageListings(folderPaths);
        }

        void RemoveCachedFileTagEntries(IEnumerable<string> files)
        {
            libraryWorkspace.RemoveCachedFileTagEntries(files);
        }

        BitmapImage TryGetCachedImage(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)) return null;
            lock (imageCacheSync)
            {
                BitmapImage cached;
                if (!imageCache.TryGetValue(cacheKey, out cached)) return null;
                LinkedListNode<string> node;
                if (imageCacheOrderNodes.TryGetValue(cacheKey, out node) && node != null)
                {
                    imageCacheOrder.Remove(node);
                    imageCacheOrder.AddLast(node);
                }
                return cached;
            }
        }

        void StoreCachedImage(string cacheKey, BitmapImage image)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || image == null) return;
            lock (imageCacheSync)
            {
                LinkedListNode<string> existingNode;
                if (imageCache.ContainsKey(cacheKey))
                {
                    imageCache[cacheKey] = image;
                    if (imageCacheOrderNodes.TryGetValue(cacheKey, out existingNode) && existingNode != null)
                    {
                        imageCacheOrder.Remove(existingNode);
                        imageCacheOrder.AddLast(existingNode);
                    }
                    return;
                }
                imageCache[cacheKey] = image;
                var node = new LinkedListNode<string>(cacheKey);
                imageCacheOrder.AddLast(node);
                imageCacheOrderNodes[cacheKey] = node;
                while (imageCache.Count > MaxImageCacheEntries && imageCacheOrder.Count > 0)
                {
                    var firstNode = imageCacheOrder.First;
                    if (firstNode == null) break;
                    imageCacheOrder.RemoveFirst();
                    var oldest = firstNode.Value;
                    if (string.IsNullOrWhiteSpace(oldest)) continue;
                    imageCache.Remove(oldest);
                    imageCacheOrderNodes.Remove(oldest);
                }
            }
        }

        void QueueImageLoad(Image imageControl, string sourcePath, int decodePixelWidth, Action<BitmapImage> onLoaded, bool prioritize = false, Func<bool> shouldLoad = null)
        {
            if (imageControl == null)
            {
                return;
            }

            var requestToken = Guid.NewGuid().ToString("N");
            var hadSource = imageControl.Source != null;
            var limiter = prioritize ? priorityImageLoadLimiter : imageLoadLimiter;
            imageControl.Uid = requestToken;
            var immediate = TryLoadCachedVisualImmediate(sourcePath, decodePixelWidth);
            if (immediate != null)
            {
                if (shouldLoad != null && !shouldLoad())
                {
                    if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                    return;
                }
                if (onLoaded != null)
                {
                    onLoaded(immediate);
                    imageControl.Visibility = Visibility.Visible;
                }
                else
                {
                    imageControl.Source = immediate;
                    imageControl.Visibility = Visibility.Visible;
                }
                imageControl.InvalidateMeasure();
                imageControl.InvalidateVisual();
                hadSource = true;
                return;
            }
            if (!hadSource)
            {
                imageControl.Visibility = Visibility.Collapsed;
            }
            Task.Run(delegate
            {
                limiter.Wait();
                try
                {
                    if (shouldLoad != null && !shouldLoad()) return null;
                    return LoadImageSource(sourcePath, decodePixelWidth);
                }
                finally
                {
                    limiter.Release();
                }
            }).ContinueWith(delegate(Task<BitmapImage> task)
            {
                imageControl.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!string.Equals(imageControl.Uid, requestToken, StringComparison.Ordinal)) return;
                    if (shouldLoad != null && !shouldLoad())
                    {
                        if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (task.Result == null)
                    {
                        if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (onLoaded != null) onLoaded(task.Result);
                    else imageControl.Source = task.Result;
                    imageControl.Visibility = Visibility.Visible;
                    imageControl.InvalidateMeasure();
                    imageControl.InvalidateVisual();
                }), DispatcherPriority.Render);
            }, TaskScheduler.Default);
        }

        Border CreateAsyncImageTile(string sourcePath, int decodePixelWidth, double tileWidth, double tileHeight, Stretch stretch, string fallbackText, Brush fallbackForeground, Thickness margin, Thickness padding, Brush background, CornerRadius cornerRadius, Brush borderBrush, Thickness borderThickness)
        {
            var tile = new Border
            {
                Width = tileWidth,
                Height = tileHeight,
                Margin = margin,
                Padding = padding,
                Background = background,
                CornerRadius = cornerRadius,
                BorderBrush = borderBrush,
                BorderThickness = borderThickness
            };
            var presenter = new Grid();
            var placeholder = new TextBlock
            {
                Text = fallbackText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8),
                Foreground = fallbackForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            var image = new Image
            {
                Width = tileWidth,
                Height = tileHeight,
                Stretch = stretch,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            presenter.Children.Add(placeholder);
            presenter.Children.Add(image);
            tile.Child = presenter;
            QueueImageLoad(image, sourcePath, decodePixelWidth, delegate(BitmapImage loaded)
            {
                image.Source = loaded;
                image.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
            });
            return tile;
        }

        void OpenSourceFolders()
        {
            foreach (var root in GetSourceRoots()) OpenFolder(root);
        }
        void LoadIntakePreviewSummaryAsync(bool recurseRename, CancellationToken cancellationToken, Action<IntakePreviewSummary> onSuccess, Action<Exception> onError)
        {
            Task.Factory.StartNew(delegate
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                var summary = BuildIntakePreviewSummary(recurseRename, cancellationToken);
                stopwatch.Stop();
                LogPerformanceSample("IntakePreviewBuild", stopwatch, "recurseRename=" + recurseRename + "; topLevel=" + summary.TopLevelMediaCount + "; reviewItems=" + summary.MetadataCandidateCount + "; manualItems=" + summary.ManualItemCount + "; conflicts=" + summary.ConflictCount, 40);
                return summary;
            }, cancellationToken).ContinueWith(delegate(Task<IntakePreviewSummary> summaryTask)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (summaryTask.IsFaulted)
                    {
                        var flattened = summaryTask.Exception == null ? null : summaryTask.Exception.Flatten();
                        var error = flattened == null ? new Exception("Preview failed.") : flattened.InnerExceptions.First();
                        if (onError != null) onError(error);
                        return;
                    }
                    if (summaryTask.IsCanceled)
                    {
                        if (onError != null) onError(new OperationCanceledException("Preview refresh cancelled."));
                        return;
                    }
                    if (onSuccess != null) onSuccess(summaryTask.Result);
                }));
            }, TaskScheduler.Default);
        }
        IntakePreviewSummary BuildIntakePreviewSummary(bool recurseRename, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSourceFolders();
            var inventory = importService.BuildSourceInventory(recurseRename);
            cancellationToken.ThrowIfCancellationRequested();
            var rename = inventory.RenameScopeFiles;
            var move = inventory.TopLevelMediaFiles;
            var previewAnalysis = AnalyzeIntakePreviewFiles(move, cancellationToken);
            var reviewItems = BuildReviewItems(move, previewAnalysis, cancellationToken);
            var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
            var manualItems = BuildManualMetadataItems(move, recognizedPaths, previewAnalysis, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
            var moveCandidates = move.Where(f => !manualPaths.Contains(f)).ToList();
            return new IntakePreviewSummary
            {
                SourceRoots = GetSourceRoots(),
                RenameScopeCount = rename.Count,
                RenameCandidateCount = rename.Count(f => !string.IsNullOrWhiteSpace(GuessSteamAppIdFromFileName(f))),
                TopLevelMediaCount = move.Count,
                MetadataCandidateCount = reviewItems.Count,
                MoveCandidateCount = moveCandidates.Count,
                ManualItemCount = manualItems.Count,
                ConflictCount = Directory.Exists(destinationRoot) ? moveCandidates.Count(f => File.Exists(Path.Combine(destinationRoot, Path.GetFileName(f)))) : 0,
                ReviewItems = reviewItems,
                ManualItems = manualItems
            };
        }
        void LogPreviewSummary(IntakePreviewSummary summary)
        {
            if (summary == null) return;
            Log("Preview refreshed. Sources=" + (summary.SourceRoots.Count == 0 ? "(none)" : string.Join(" | ", summary.SourceRoots.ToArray())) + "; RenameCandidates=" + summary.RenameCandidateCount + "; MetadataCandidates=" + summary.MetadataCandidateCount + "; MoveCandidates=" + summary.MoveCandidateCount + "; ManualCandidates=" + summary.ManualItemCount + ".");
        }
        void RefreshPreview()
        {
            var refreshVersion = ++previewRefreshVersion;
            var recurseRename = recurseBox != null && recurseBox.IsChecked == true;
            if (previewRefreshCancellation != null)
            {
                previewRefreshCancellation.Cancel();
                previewRefreshCancellation.Dispose();
            }
            previewRefreshCancellation = new CancellationTokenSource();
            var refreshCancellationToken = previewRefreshCancellation.Token;
            status.Text = "Refreshing preview";
            RenderPreviewLoading("Refreshing upload preview...");
            LoadIntakePreviewSummaryAsync(recurseRename, refreshCancellationToken, delegate(IntakePreviewSummary summary)
            {
                if (refreshVersion != previewRefreshVersion) return;
                RenderPreview(summary);
                status.Text = "Preview ready";
                LogPreviewSummary(summary);
            }, delegate(Exception ex)
            {
                if (refreshVersion != previewRefreshVersion) return;
                if (ex is OperationCanceledException) return;
                RenderPreviewError(ex.Message);
                status.Text = "Preview failed";
                Log(ex.Message);
            });
        }

        void ShowIntakePreviewWindow(bool recurseRename)
        {
            IntakePreviewWindow.Show(this, AppVersion, recurseRename, new IntakePreviewServices
            {
                LoadSummaryAsync = LoadIntakePreviewSummaryAsync,
                OpenSourceFolders = OpenSourceFolders,
                OpenManualIntake = OpenManualIntakeWindow,
                SyncSettingsDocument = RenderPreview,
                SyncSettingsDocumentError = RenderPreviewError,
                SetStatus = delegate(string text) { if (status != null) status.Text = text; },
                Log = Log,
                LogSummary = LogPreviewSummary,
                CreateButton = Btn,
                PreviewBadge = PreviewBadgeBrush,
                PlatformOrder = PlatformGroupOrder,
                FormatTimestamp = FormatFriendlyTimestamp,
                FilenameGuess = FilenameGuessLabel
            });
        }


        void RenderPreview(IntakePreviewSummary summary)
        {
            if (previewBox == null) return;
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono"), FontSize = 13.5, Background = Brushes.White };
            doc.Blocks.Add(new Paragraph(new Run("Queue: " + summary.TopLevelMediaCount + " top-level media item(s) waiting")) { Margin = new Thickness(0), FontWeight = FontWeights.SemiBold });
            doc.Blocks.Add(new Paragraph(new Run("Rename: " + summary.RenameCandidateCount + " candidate(s) out of " + summary.RenameScopeCount)) { Margin = new Thickness(0, 2, 0, 0) });
            doc.Blocks.Add(new Paragraph(new Run("Auto-ready: " + summary.MoveCandidateCount + " candidate(s)")) { Margin = new Thickness(0, 2, 0, 0) });
            doc.Blocks.Add(new Paragraph(new Run("Manual Intake: " + summary.ManualItemCount + " unmatched item(s) waiting")) { Margin = new Thickness(0, 2, 0, 0) });
            doc.Blocks.Add(new Paragraph(new Run("Move conflicts: " + summary.ConflictCount)) { Margin = new Thickness(0, 2, 0, 10) });
            doc.Blocks.Add(new Paragraph(new Run("Files by console:")) { Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.SemiBold });
            if (summary.ReviewItems.Count == 0)
            {
                doc.Blocks.Add(new Paragraph(new Run("No auto-ready media files found.")) { Margin = new Thickness(0) });
            }
            else
            {
                var grouped = summary.ReviewItems.GroupBy(item => item.PlatformLabel).OrderBy(group => PlatformGroupOrder(group.Key)).ThenBy(group => group.Key);
                foreach (var group in grouped)
                {
                    doc.Blocks.Add(new Paragraph(new Run(group.Key + " (" + group.Count() + ")")) { Margin = new Thickness(0, 6, 0, 4), FontWeight = FontWeights.SemiBold, Foreground = PreviewBadgeBrush(group.Key) });
                    foreach (var item in group)
                    {
                        doc.Blocks.Add(new Paragraph(new Run(item.FileName) { Foreground = Brush("#1F2A30") }) { Margin = new Thickness(12, 0, 0, 2) });
                    }
                }
            }
            if (summary.ManualItems.Count > 0)
            {
                doc.Blocks.Add(new Paragraph(new Run("Unmatched files waiting for Manual Intake:")) { Margin = new Thickness(0, 12, 0, 6), FontWeight = FontWeights.SemiBold, Foreground = Brush("#A16C2E") });
                foreach (var item in summary.ManualItems.OrderBy(i => i.FileName))
                {
                    doc.Blocks.Add(new Paragraph(new Run(item.FileName) { Foreground = Brush("#5B5048") }) { Margin = new Thickness(12, 0, 0, 2) });
                }
            }
            previewBox.Document = doc;
        }

        void RenderPreviewLoading(string message)
        {
            if (previewBox == null) return;
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono"), FontSize = 14, Background = Brushes.White };
            doc.Blocks.Add(new Paragraph(new Run(message)) { Margin = new Thickness(0) });
            previewBox.Document = doc;
        }

        void RenderPreviewError(string message)
        {
            if (previewBox == null) return;
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono"), FontSize = 14, Background = Brushes.White };
            doc.Blocks.Add(new Paragraph(new Run(message)) { Margin = new Thickness(0) });
            previewBox.Document = doc;
        }

        int CalculateLibraryFolderArtDecodeWidth(int tileWidth)
        {
            return Math.Min(640, Math.Max(320, tileWidth + 96));
        }

        int CalculateLibraryBannerArtDecodeWidth()
        {
            return 384;
        }

        int CalculateLibraryDetailTileDecodeWidth(int tileWidth)
        {
            return Math.Min(640, Math.Max(384, tileWidth + 96));
        }

        sealed class IntakePreviewFileAnalysis
        {
            public string FilePath = string.Empty;
            public string FileName = string.Empty;
            public FilenameParseResult Parsed = new FilenameParseResult();
            public bool CanUpdateMetadata;
            public bool PreserveFileTimes;
            public DateTime CaptureTime;
        }

        Dictionary<string, IntakePreviewFileAnalysis> AnalyzeIntakePreviewFiles(IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default(CancellationToken))
        {
            var analysis = new Dictionary<string, IntakePreviewFileAnalysis>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var parsed = ParseFilename(fileName);
                var platformTags = parsed.PlatformTags ?? new string[0];
                var isVideo = IsVideo(file);
                var preserveFileTimes = parsed.PreserveFileTimes || platformTags.Contains("Xbox") || isVideo;
                var canUpdateMetadata = !(parsed.RoutesToManualWhenMissingSteamAppId && string.IsNullOrWhiteSpace(parsed.SteamAppId))
                    && (isVideo || platformTags.Contains("Xbox") || parsed.CaptureTime.HasValue);
                analysis[file] = new IntakePreviewFileAnalysis
                {
                    FilePath = file,
                    FileName = fileName,
                    Parsed = parsed,
                    CanUpdateMetadata = canUpdateMetadata,
                    PreserveFileTimes = preserveFileTimes,
                    CaptureTime = preserveFileTimes ? GetLibraryDate(file) : (parsed.CaptureTime ?? GetLibraryDate(file))
                };
            }
            return analysis;
        }

        List<ReviewItem> BuildReviewItems()
        {
            return BuildReviewItems(importService.BuildSourceInventory(false).TopLevelMediaFiles);
        }

        List<ReviewItem> BuildReviewItems(IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default(CancellationToken))
        {
            return BuildReviewItems(sourceFiles, AnalyzeIntakePreviewFiles(sourceFiles, cancellationToken), cancellationToken);
        }

        List<ReviewItem> BuildReviewItems(IEnumerable<string> sourceFiles, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ReviewItem>();
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null || !fileAnalysis.CanUpdateMetadata) continue;
                var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
                var platformTags = parsed.PlatformTags ?? new string[0];
                items.Add(new ReviewItem
                {
                    FilePath = file,
                    FileName = fileAnalysis.FileName,
                    PlatformLabel = parsed.PlatformLabel,
                    PlatformTags = platformTags,
                    CaptureTime = fileAnalysis.CaptureTime,
                    PreserveFileTimes = fileAnalysis.PreserveFileTimes,
                    Comment = string.Empty,
                    AddPhotographyTag = false,
                    DeleteBeforeProcessing = false
                });
            }
            return items
                .OrderBy(i => PlatformGroupOrder(i.PlatformLabel))
                .ThenBy(i => i.CaptureTime)
                .ThenBy(i => i.FileName)
                .ToList();
        }

        List<ManualMetadataItem> BuildManualMetadataItems(HashSet<string> recognizedPaths)
        {
            return BuildManualMetadataItems(importService.BuildSourceInventory(false).TopLevelMediaFiles, recognizedPaths);
        }

        List<ManualMetadataItem> BuildManualMetadataItems(IEnumerable<string> sourceFiles, HashSet<string> recognizedPaths, CancellationToken cancellationToken = default(CancellationToken))
        {
            return BuildManualMetadataItems(sourceFiles, recognizedPaths, AnalyzeIntakePreviewFiles(sourceFiles, cancellationToken), cancellationToken);
        }

        List<ManualMetadataItem> BuildManualMetadataItems(IEnumerable<string> sourceFiles, HashSet<string> recognizedPaths, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ManualMetadataItem>();
            var known = recognizedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (known.Contains(file)) continue;
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null || fileAnalysis.CanUpdateMetadata) continue;
                var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
                var captureTime = fileAnalysis.CaptureTime;
                if (!parsed.MatchedConvention)
                {
                    indexPersistenceService.RecordFilenameConventionSample(libraryRoot, fileAnalysis.FileName, parsed);
                }
                var titleHint = parsed.GameTitleHint ?? string.Empty;
                bool tagSteam, tagPc, tagPs5, tagXbox, tagOther;
                string customPlatformTag;
                ApplyFilenameParseResultToManualPlatformFlags(parsed, out tagSteam, out tagPc, out tagPs5, out tagXbox, out tagOther, out customPlatformTag);
                items.Add(new ManualMetadataItem
                {
                    GameId = string.Empty,
                    SteamAppId = parsed.SteamAppId,
                    FilePath = file,
                    FileName = fileAnalysis.FileName,
                    OriginalFileName = fileAnalysis.FileName,
                    CaptureTime = captureTime,
                    UseCustomCaptureTime = false,
                    GameName = titleHint,
                    Comment = string.Empty,
                    TagText = string.Empty,
                    AddPhotographyTag = false,
                    TagSteam = tagSteam,
                    TagPs5 = tagPs5,
                    TagXbox = tagXbox,
                    TagPc = tagPc,
                    TagOther = tagOther,
                    CustomPlatformTag = customPlatformTag,
                    OriginalGameId = string.Empty,
                    OriginalSteamAppId = parsed.SteamAppId,
                    OriginalCaptureTime = captureTime,
                    OriginalUseCustomCaptureTime = false,
                    OriginalGameName = titleHint,
                    OriginalComment = string.Empty,
                    OriginalTagText = string.Empty,
                    OriginalAddPhotographyTag = false,
                    OriginalTagSteam = tagSteam,
                    OriginalTagPc = tagPc,
                    OriginalTagPs5 = tagPs5,
                    OriginalTagXbox = tagXbox,
                    OriginalTagOther = tagOther,
                    OriginalCustomPlatformTag = customPlatformTag
                });
            }
            return items.OrderBy(i => i.CaptureTime).ThenBy(i => i.FileName).ToList();
        }

        /// <summary>All top-level upload files as manual-editor rows (rule-matched and manual-intake).</summary>
        List<ManualMetadataItem> BuildImportAndEditMetadataItems(IEnumerable<string> sourceFiles, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ManualMetadataItem>();
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null) continue;
                var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
                var captureTime = fileAnalysis.CaptureTime;
                if (!parsed.MatchedConvention)
                {
                    indexPersistenceService.RecordFilenameConventionSample(libraryRoot, fileAnalysis.FileName, parsed);
                }
                var titleHint = parsed.GameTitleHint ?? string.Empty;
                bool tagSteam, tagPc, tagPs5, tagXbox, tagOther;
                string customPlatformTag;
                ApplyFilenameParseResultToManualPlatformFlags(parsed, out tagSteam, out tagPc, out tagPs5, out tagXbox, out tagOther, out customPlatformTag);
                var ruleMatched = fileAnalysis.CanUpdateMetadata;
                items.Add(new ManualMetadataItem
                {
                    GameId = string.Empty,
                    SteamAppId = parsed.SteamAppId,
                    FilePath = file,
                    FileName = fileAnalysis.FileName,
                    OriginalFileName = fileAnalysis.FileName,
                    CaptureTime = captureTime,
                    UseCustomCaptureTime = false,
                    GameName = titleHint,
                    Comment = string.Empty,
                    TagText = string.Empty,
                    AddPhotographyTag = false,
                    TagSteam = tagSteam,
                    TagPs5 = tagPs5,
                    TagXbox = tagXbox,
                    TagPc = tagPc,
                    TagOther = tagOther,
                    CustomPlatformTag = customPlatformTag,
                    OriginalGameId = string.Empty,
                    OriginalSteamAppId = parsed.SteamAppId,
                    OriginalCaptureTime = captureTime,
                    OriginalUseCustomCaptureTime = false,
                    OriginalGameName = titleHint,
                    OriginalComment = string.Empty,
                    OriginalTagText = string.Empty,
                    OriginalAddPhotographyTag = false,
                    OriginalTagSteam = tagSteam,
                    OriginalTagPc = tagPc,
                    OriginalTagPs5 = tagPs5,
                    OriginalTagXbox = tagXbox,
                    OriginalTagOther = tagOther,
                    OriginalCustomPlatformTag = customPlatformTag,
                    IntakeRuleMatched = ruleMatched,
                    DeleteBeforeProcessing = false
                });
            }
            return items
                .OrderBy(i => PlatformGroupOrder(DetermineManualMetadataPlatformLabel(i)))
                .ThenBy(i => i.CaptureTime)
                .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        bool ShowMetadataReviewWindow(List<ReviewItem> items)
        {
            return MetadataReviewWindow.Show(this, AppVersion, items, new MetadataReviewServices
            {
                CreateButton = Btn,
                PreviewBadge = PreviewBadgeBrush,
                LoadImageSource = LoadImageSource,
                GamePhotographyTag = GamePhotographyTag
            });
        }

        void ClearLibraryImageCaches()
        {
            libraryWorkspace.ClearFolderImageListings();
            libraryWorkspace.ClearFileTagCache();
            ClearImageCache();
        }
        void RunLibraryMetadataWorkflowWithProgress(LibraryFolderInfo folder, List<ManualMetadataItem> items, Action refreshLibrary)
        {
            var originalSavedGameIndexRow = folder == null ? null : FindSavedGameIndexRow(LoadSavedGameIndexRows(libraryRoot), folder);
            var totalPerStage = Math.Max(items.Count, 1);
            var totalWork = totalPerStage * 3;
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
            appendProgress("Starting library metadata apply for " + items.Count + " capture(s) in " + folder.Name + ".");
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
                libraryScanner.UpsertLibraryMetadataIndexEntries(items, libraryRoot);
                PreserveLibraryMetadataEditGameIndex(libraryRoot, folder, originalSavedGameIndexRow, items);
                progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    progressFinished = true;
                    updateProgress(totalWork, "Library metadata apply complete for " + folder.Name + ". Edited " + items.Count + " capture(s); reorganized " + moved + ".");
                    status.Text = moved > 0 ? "Library metadata updated and organized" : "Library metadata updated";

                    if (refreshLibrary != null) refreshLibrary();
                    Log("Library metadata apply complete for " + folder.Name + ". Edited " + items.Count + " capture(s); reorganized " + moved + ".");
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
                    Log(error.ToString());
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

        string GetGameNameFromFileName(string baseName)
        {
            return filenameParserService.GetGameTitleHint(baseName, libraryRoot);
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

        List<string> GetTaggedImagesCached(string root, bool forceRefresh, params string[] tagCandidates)
        {
            var stamp = BuildImageInventoryStamp(root);
            if (!forceRefresh)
            {
                var cached = LoadTaggedImageCache(root, stamp);
                if (cached != null)
                {
                    Log("Photography gallery cache hit.");
                    return cached;
                }
            }
            Log("Refreshing photography gallery cache.");
            var fresh = FindTaggedImages(root, tagCandidates);
            SaveTaggedImageCache(root, stamp, fresh);
            return fresh;
        }

        string BuildImageInventoryStamp(string root)
        {
            long latestTicks = 0;
            int count = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(IsMedia))
            {
                count++;
                var ticks = MetadataCacheStamp(file);
                if (ticks > latestTicks) latestTicks = ticks;
            }
            return count + "|" + latestTicks;
        }

        string TaggedImageCachePath(string root)
        {
            return Path.Combine(cacheRoot, "photography-gallery-" + SafeCacheName(root) + ".cache");
        }

        List<string> LoadTaggedImageCache(string root, string stamp)
        {
            var path = TaggedImageCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(lines[1], stamp, StringComparison.Ordinal)) return null;
            return lines.Skip(2).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        void SaveTaggedImageCache(string root, string stamp, List<string> files)
        {
            var path = TaggedImageCachePath(root);
            var lines = new List<string>();
            lines.Add(root);
            lines.Add(stamp);
            lines.AddRange(files.Distinct(StringComparer.OrdinalIgnoreCase));
            File.WriteAllLines(path, lines.ToArray());
        }

        List<string> FindTaggedImages(string root, params string[] tagCandidates)
        {
            var tags = tagCandidates.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tags.Count == 0) return new List<string>();
            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(IsMedia)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tagMap = ReadEmbeddedKeywordTagsBatch(files);
            return files
                .Where(file => tagMap.ContainsKey(file) && tagMap[file].Any(tag => tags.Any(candidate => string.Equals(tag, candidate, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        List<LibraryFolderInfo> LoadLibraryFoldersCached(string root, bool forceRefresh)
        {
            return libraryScanner.LoadLibraryFoldersCached(root, forceRefresh);
        }

        void OpenLibraryFolderIdEditor(LibraryFolderInfo folder, Action refreshLibrary)
        {
            if (folder == null)
            {
                MessageBox.Show("Choose a library folder first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                MessageBox.Show("Library folder not found. Check Settings before editing IDs.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var savedRows = LoadSavedGameIndexRows(libraryRoot);
            var savedRow = FindSavedGameIndexRow(savedRows, folder);
            var appIdBox = new TextBox
            {
                Text = savedRow == null ? DisplayExternalIdValue(folder.SteamAppId ?? string.Empty) : DisplayExternalIdValue(savedRow.SteamAppId ?? string.Empty),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };
            var steamGridDbIdBox = new TextBox
            {
                Text = savedRow == null ? DisplayExternalIdValue(folder.SteamGridDbId ?? string.Empty) : DisplayExternalIdValue(savedRow.SteamGridDbId ?? string.Empty),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };

            var editorWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " Edit IDs",
                Width = 560,
                Height = 430,
                MinWidth = 540,
                MinHeight = 410,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#F3EEE4"),
                ResizeMode = ResizeMode.NoResize
            };

            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            header.Children.Add(new TextBlock { Text = folder.Name ?? "Selected Folder", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), TextWrapping = TextWrapping.Wrap });
            header.Children.Add(new TextBlock { Text = NormalizeConsoleLabel(folder.PlatformLabel), Margin = new Thickness(0, 6, 0, 0), Foreground = Brush("#5F6970"), FontSize = 13 });
            header.Children.Add(new TextBlock { Text = "Update the saved Steam App ID and SteamGridDB ID for this game record without leaving the Library view.", Margin = new Thickness(0, 10, 0, 0), Foreground = Brush("#5F6970"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(header);

            var form = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(form, 1);
            root.Children.Add(form);

            var appIdStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            appIdStack.Children.Add(new TextBlock { Text = "Steam App ID", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            appIdStack.Children.Add(appIdBox);
            form.Children.Add(appIdStack);

            var steamGridDbIdStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            steamGridDbIdStack.Children.Add(new TextBlock { Text = "SteamGridDB ID", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            steamGridDbIdStack.Children.Add(steamGridDbIdBox);
            Grid.SetRow(steamGridDbIdStack, 1);
            form.Children.Add(steamGridDbIdStack);

            var helperText = new TextBlock
            {
                Text = "Leave a field blank if you want to clear the saved value.",
                Foreground = Brush("#5F6970"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(helperText, 2);
            form.Children.Add(helperText);

            var actions = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cancelButton = Btn("Cancel", null, "#EEF2F5", Brush("#33424D"));
            cancelButton.Width = 138;
            cancelButton.Height = 44;
            cancelButton.Margin = new Thickness(0, 0, 10, 0);
            cancelButton.VerticalAlignment = VerticalAlignment.Top;
            var saveButton = Btn("Save", null, "#275D47", Brushes.White);
            saveButton.Width = 138;
            saveButton.Height = 44;
            saveButton.Margin = new Thickness(0);
            saveButton.VerticalAlignment = VerticalAlignment.Top;
            actions.Children.Add(cancelButton);
            Grid.SetColumn(saveButton, 1);
            actions.Children.Add(saveButton);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);

            cancelButton.Click += delegate { editorWindow.Close(); };
            saveButton.Click += delegate
            {
                try
                {
                    var steamAppId = CleanTag(appIdBox.Text);
                    var steamGridDbId = CleanTag(steamGridDbIdBox.Text);
                    var rows = LoadSavedGameIndexRows(libraryRoot);
                    var row = FindSavedGameIndexRow(rows, folder);
                    if (row == null)
                    {
                        if (string.IsNullOrWhiteSpace(steamAppId) && string.IsNullOrWhiteSpace(steamGridDbId))
                        {
                            editorWindow.Close();
                            return;
                        }
                        row = EnsureGameIndexRowForAssignment(rows, folder.Name, folder.PlatformLabel, folder.GameId);
                    }
                    row.Name = NormalizeGameIndexName(string.IsNullOrWhiteSpace(row.Name) ? folder.Name : row.Name, folder.FolderPath);
                    row.PlatformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.PlatformLabel) ? folder.PlatformLabel : row.PlatformLabel);
                    row.FolderPath = string.IsNullOrWhiteSpace(folder.FolderPath) ? (row.FolderPath ?? string.Empty) : folder.FolderPath;
                    row.FileCount = folder.FileCount > 0 ? folder.FileCount : row.FileCount;
                    row.PreviewImagePath = string.IsNullOrWhiteSpace(folder.PreviewImagePath) ? (row.PreviewImagePath ?? string.Empty) : folder.PreviewImagePath;
                    row.FilePaths = folder.FilePaths == null || folder.FilePaths.Length == 0 ? (row.FilePaths ?? new string[0]) : folder.FilePaths;
                    var previousSteamAppId = row.SteamAppId;
                    var previousSteamGridDbId = row.SteamGridDbId;
                    var previousSuppressSteamAppId = row.SuppressSteamAppIdAutoResolve;
                    var previousSuppressSteamGridDbId = row.SuppressSteamGridDbIdAutoResolve;
                    row.SteamAppId = steamAppId;
                    row.SteamGridDbId = steamGridDbId;
                    row.SuppressSteamAppIdAutoResolve = ShouldSuppressExternalIdAutoResolve(steamAppId, previousSteamAppId, previousSuppressSteamAppId);
                    row.SuppressSteamGridDbIdAutoResolve = ShouldSuppressExternalIdAutoResolve(steamGridDbId, previousSteamGridDbId, previousSuppressSteamGridDbId);
                    SaveGameIndexEditorRows(libraryRoot, rows);
                    folder.SteamAppId = steamAppId;
                    folder.SteamGridDbId = steamGridDbId;
                    folder.SuppressSteamAppIdAutoResolve = row.SuppressSteamAppIdAutoResolve;
                    folder.SuppressSteamGridDbIdAutoResolve = row.SuppressSteamGridDbIdAutoResolve;
                    status.Text = "Folder IDs saved";
                    Log("Updated IDs for " + (folder.Name ?? "folder") + " | " + NormalizeConsoleLabel(folder.PlatformLabel) + " | AppID=" + (string.IsNullOrWhiteSpace(steamAppId) ? "(blank)" : steamAppId) + (row.SuppressSteamAppIdAutoResolve ? " [manual clear]" : string.Empty) + " | STID=" + (string.IsNullOrWhiteSpace(steamGridDbId) ? "(blank)" : steamGridDbId) + (row.SuppressSteamGridDbIdAutoResolve ? " [manual clear]" : string.Empty));
                    if (refreshLibrary != null) refreshLibrary();
                    editorWindow.Close();
                }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                    MessageBox.Show("Could not save the folder IDs." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            editorWindow.Content = root;
            editorWindow.ShowDialog();
        }


        void ClearLibraryFolderCache(string root)
        {
            var path = LibraryFolderCachePath(root);
            if (File.Exists(path)) File.Delete(path);
        }

        bool HasLibraryFolderCacheSnapshot(string root)
        {
            return LoadLibraryFolderCacheSnapshot(root) != null;
        }

        bool LibraryFolderCacheLooksIncomplete(string root, List<LibraryFolderInfo> folders)
        {
            if (string.IsNullOrWhiteSpace(root) || folders == null || folders.Count != 1) return false;
            try
            {
                return Directory.Exists(root) && Directory.EnumerateDirectories(root).Skip(1).Any();
            }
            catch
            {
                return false;
            }
        }

        List<LibraryFolderInfo> LoadLibraryFolderCache(string root, string stamp)
        {
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(lines[1], stamp, StringComparison.Ordinal)) return null;
            var parsed = ParseLibraryFolderCacheLines(root, lines);
            if (LibraryFolderCacheLooksIncomplete(root, parsed))
            {
                Log("Library folder cache snapshot looked incomplete for " + root + ". Ignoring cached folder list.");
                return null;
            }
            return parsed;
        }

        List<LibraryFolderInfo> LoadLibraryFolderCacheSnapshot(string root)
        {
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            var parsed = ParseLibraryFolderCacheLines(root, lines);
            if (LibraryFolderCacheLooksIncomplete(root, parsed))
            {
                Log("Library folder cache snapshot looked incomplete for " + root + ". Skipping startup prefill.");
                return null;
            }
            return parsed;
        }

        List<LibraryFolderInfo> ParseLibraryFolderCacheLines(string root, string[] lines)
        {
            var aliasMap = BuildSavedGameIdAliasMapFromFile(root);
            var list = new List<LibraryFolderInfo>();
            foreach (var line in lines.Skip(2))
            {
                var parts = line.Split('\t');
                if (parts.Length < 5) continue;
                if (parts.Length >= 9)
                {
                    list.Add(new LibraryFolderInfo
                    {
                        GameId = !string.IsNullOrWhiteSpace(NormalizeGameId(parts[0])) && aliasMap.ContainsKey(NormalizeGameId(parts[0])) ? aliasMap[NormalizeGameId(parts[0])] : parts[0],
                        FolderPath = parts[1],
                        Name = parts[2],
                        FileCount = ParseInt(parts[3]),
                        PreviewImagePath = parts[4],
                        PlatformLabel = parts[5],
                        FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : new string[0],
                        NewestCaptureUtcTicks = parts.Length > 9 ? ParseLong(parts[9]) : 0,
                        SteamAppId = parts.Length > 7 ? parts[7] : string.Empty,
                        SteamGridDbId = parts.Length > 8 ? parts[8] : string.Empty
                    });
                }
                else if (parts.Length >= 8)
                {
                    list.Add(new LibraryFolderInfo
                    {
                        GameId = !string.IsNullOrWhiteSpace(NormalizeGameId(parts[0])) && aliasMap.ContainsKey(NormalizeGameId(parts[0])) ? aliasMap[NormalizeGameId(parts[0])] : parts[0],
                        FolderPath = parts[1],
                        Name = parts[2],
                        FileCount = ParseInt(parts[3]),
                        PreviewImagePath = parts[4],
                        PlatformLabel = parts[5],
                        FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : new string[0],
                        NewestCaptureUtcTicks = 0,
                        SteamAppId = parts.Length > 7 ? parts[7] : string.Empty,
                        SteamGridDbId = string.Empty
                    });
                }
                else
                {
                    list.Add(new LibraryFolderInfo
                    {
                        GameId = string.Empty,
                        FolderPath = parts[0],
                        Name = parts[1],
                        FileCount = ParseInt(parts[2]),
                        PreviewImagePath = parts[3],
                        PlatformLabel = parts[4],
                        FilePaths = parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5])
                            ? parts[5].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                            : new string[0],
                        NewestCaptureUtcTicks = 0,
                        SteamAppId = parts.Length > 6 ? parts[6] : string.Empty,
                        SteamGridDbId = string.Empty
                    });
                }
            }
            return list;
        }

        void SaveLibraryFolderCache(string root, string stamp, List<LibraryFolderInfo> folders)
        {
            var path = LibraryFolderCachePath(root);
            var lines = new List<string>();
            lines.Add(root);
            lines.Add(stamp);
            foreach (var folder in folders)
            {
                lines.Add(string.Join("\t", new[]
                {
                    NormalizeGameId(folder.GameId),
                    folder.FolderPath ?? string.Empty,
                    folder.Name ?? string.Empty,
                    folder.FileCount.ToString(),
                    folder.PreviewImagePath ?? string.Empty,
                    folder.PlatformLabel ?? string.Empty,
                    string.Join("|", (folder.FilePaths ?? new string[0]).Where(File.Exists)),
                    folder.SteamAppId ?? string.Empty,
                    folder.SteamGridDbId ?? string.Empty,
                    folder.NewestCaptureUtcTicks > 0 ? folder.NewestCaptureUtcTicks.ToString() : string.Empty
                }));
            }
            File.WriteAllLines(path, lines.ToArray());
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
            var saved = FindSavedGameIndexRow(LoadSavedGameIndexRows(root), folder);
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
            var saved = FindSavedGameIndexRow(LoadSavedGameIndexRows(root), folder);
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

        BitmapImage LoadImageSource(string path, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path.Trim());
                }
                catch
                {
                    return null;
                }

                if (!File.Exists(fullPath)) return null;
                var sourcePath = fullPath;
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                if (IsVideo(fullPath))
                {
                    var poster = EnsureVideoPoster(fullPath, normalizedDecodePixelWidth);
                    if (!string.IsNullOrWhiteSpace(poster) && File.Exists(poster)) fullPath = poster;
                }
                var info = new FileInfo(fullPath);
                var cacheKey = fullPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + normalizedDecodePixelWidth;
                var cached = TryGetCachedImage(cacheKey);
                if (cached != null) return cached;

                BitmapImage image = null;
                var thumbnailPath = IsVideo(sourcePath) ? null : ThumbnailCachePath(fullPath, normalizedDecodePixelWidth);
                if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    image = LoadFrozenBitmap(thumbnailPath, 0);
                }
                if (image == null)
                {
                    image = LoadFrozenBitmap(fullPath, normalizedDecodePixelWidth);
                    if (image != null && !string.IsNullOrWhiteSpace(thumbnailPath) && !File.Exists(thumbnailPath))
                    {
                        SaveThumbnailCache(image, thumbnailPath);
                    }
                }
                StoreCachedImage(cacheKey, image);
                return image;
            }
            catch
            {
                return null;
            }
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
                case "Multiple Tags": return 3;
                case "Other": return 4;
                default: return 5;
            }
        }

        Brush PreviewBadgeBrush(string label)
        {
            switch (label)
            {
                case "Xbox": return Brush("#2E8B57");
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

        int NormalizeThumbnailDecodeWidth(int decodePixelWidth)
        {
            if (decodePixelWidth <= 0) return 0;
            if (decodePixelWidth <= 160) return 160;
            if (decodePixelWidth <= 256) return 256;
            if (decodePixelWidth <= 384) return 384;
            if (decodePixelWidth <= 512) return 512;
            if (decodePixelWidth <= 640) return 640;
            if (decodePixelWidth <= 768) return 768;
            if (decodePixelWidth <= 960) return 960;
            if (decodePixelWidth <= 1280) return 1280;
            return 1600;
        }

        string ThumbnailCachePath(string sourcePath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
            if (normalizedDecodePixelWidth <= 0 || normalizedDecodePixelWidth > 1600) return null;
            try
            {
                var info = new FileInfo(sourcePath);
                var key = sourcePath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + normalizedDecodePixelWidth;
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                    var name = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                    return Path.Combine(thumbsRoot, name + ".png");
                }
            }
            catch
            {
                return null;
            }
        }

        string ExistingVideoPosterPath(string videoPath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return null;
            try
            {
                var info = new FileInfo(videoPath);
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                var width = Math.Max(320, normalizedDecodePixelWidth > 0 ? normalizedDecodePixelWidth : 720);
                var keySource = videoPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + width;
                string hash;
                using (var md5 = MD5.Create())
                {
                    hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(keySource))).Replace("-", string.Empty).ToLowerInvariant();
                }
                var framePosterPath = Path.Combine(thumbsRoot, "video-" + hash + "-frame.png");
                if (File.Exists(framePosterPath)) return framePosterPath;
                var fallbackPosterPath = Path.Combine(thumbsRoot, "video-" + hash + "-fallback.png");
                return File.Exists(fallbackPosterPath) ? fallbackPosterPath : null;
            }
            catch
            {
                return null;
            }
        }

        BitmapImage TryLoadCachedVisualImmediate(string sourcePath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(sourcePath.Trim());
            }
            catch
            {
                return null;
            }

            if (!File.Exists(fullPath)) return null;
            try
            {
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                if (IsVideo(fullPath))
                {
                    var posterPath = ExistingVideoPosterPath(fullPath, normalizedDecodePixelWidth);
                    return string.IsNullOrWhiteSpace(posterPath) ? null : LoadFrozenBitmap(posterPath, 0);
                }
                var thumbnailPath = ThumbnailCachePath(fullPath, normalizedDecodePixelWidth);
                return string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath)
                    ? null
                    : LoadFrozenBitmap(thumbnailPath, 0);
            }
            catch (Exception ex)
            {
                Log("TryLoadCachedVisualImmediate: " + fullPath + " — " + ex.Message);
                return null;
            }
        }

        BitmapImage LoadFrozenBitmap(string path, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim());
            }
            catch
            {
                return null;
            }

            if (!File.Exists(fullPath)) return null;

            try
            {
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception ex)
            {
                Log("LoadFrozenBitmap: " + fullPath + " — " + ex.Message);
                return null;
            }
        }

        void SaveThumbnailCache(BitmapSource source, string destinationPath)
        {
            if (source == null || string.IsNullOrWhiteSpace(destinationPath)) return;
            try
            {
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                var tempPath = destinationPath + ".tmp";
                using (var stream = File.Create(tempPath))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);
                }
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(tempPath, destinationPath);
            }
            catch (Exception ex)
            {
                Log("SaveThumbnailCache failed for " + destinationPath + ". " + ex.Message);
                try
                {
                    if (File.Exists(destinationPath + ".tmp")) File.Delete(destinationPath + ".tmp");
                }
                catch (Exception inner)
                {
                    Log("SaveThumbnailCache: could not remove temp file. " + inner.Message);
                }
            }
        }
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
                        Log(err.ToString());
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
                        Log("Failed to open game index. " + ex.Message);
                        MessageBox.Show("Could not open the game index." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault");
                    }
                }));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        void OpenWithShell(string path) { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }

        AppSettings CaptureAppSettings()
        {
            return new AppSettings
            {
                SourceRootsSerialized = sourceRoot ?? string.Empty,
                DestinationRoot = destinationRoot ?? string.Empty,
                LibraryRoot = libraryRoot ?? string.Empty,
                ExifToolPath = exifToolPath ?? string.Empty,
                FfmpegPath = ffmpegPath ?? string.Empty,
                SteamGridDbApiToken = steamGridDbApiToken ?? string.Empty,
                LibraryFolderTileSize = libraryFolderTileSize,
                LibraryFolderSortMode = libraryFolderSortMode ?? "platform",
                LibraryGroupingMode = libraryGroupingMode ?? "all",
                LibraryBrowserSearchText = _libraryBrowserPersistedSearch ?? string.Empty,
                LibraryBrowserLastViewKey = _libraryBrowserPersistedLastViewKey ?? string.Empty,
                LibraryBrowserFolderScroll = Math.Max(0, _libraryBrowserPersistedFolderScroll),
                LibraryBrowserDetailScroll = Math.Max(0, _libraryBrowserPersistedDetailScroll)
            };
        }

        void ApplyAppSettings(AppSettings s)
        {
            if (s == null) return;
            sourceRoot = s.SourceRootsSerialized ?? string.Empty;
            destinationRoot = s.DestinationRoot ?? string.Empty;
            libraryRoot = s.LibraryRoot ?? string.Empty;
            exifToolPath = s.ExifToolPath ?? string.Empty;
            ffmpegPath = s.FfmpegPath ?? string.Empty;
            steamGridDbApiToken = s.SteamGridDbApiToken ?? string.Empty;
            libraryFolderTileSize = s.LibraryFolderTileSize;
            libraryFolderSortMode = s.LibraryFolderSortMode ?? "platform";
            libraryGroupingMode = s.LibraryGroupingMode ?? "all";
            _libraryBrowserPersistedSearch = s.LibraryBrowserSearchText ?? string.Empty;
            _libraryBrowserPersistedLastViewKey = s.LibraryBrowserLastViewKey ?? string.Empty;
            _libraryBrowserPersistedFolderScroll = Math.Max(0, s.LibraryBrowserFolderScroll);
            _libraryBrowserPersistedDetailScroll = Math.Max(0, s.LibraryBrowserDetailScroll);
        }

        void LoadSettings()
        {
            var merged = settingsService.LoadFromIni(
                settingsPath,
                CaptureAppSettings(),
                appRoot,
                () => FindExecutableOnPath("ffmpeg.exe") ?? string.Empty,
                SettingsService.FindSteamGridDbApiTokenInEnvironment);
            ApplyAppSettings(merged);
        }

        void SaveSettings()
        {
            settingsService.SaveToIni(settingsPath, CaptureAppSettings());
        }

        string LogFilePath() { return Path.Combine(logsRoot, "PixelVault-native.log"); }
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
        void AppendLogFileLine(string line)
        {
            var path = LogFilePath();
            Directory.CreateDirectory(logsRoot);
            lock (logFileSync)
            {
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
        void LoadLogView()
        {
            if (logBox == null) return;
            var content = TryReadLogFile();
            logBox.Text = content;
            logBox.ScrollToEnd();
        }
        void Log(string message)
        {
            var line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
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
    }
}



















































































































