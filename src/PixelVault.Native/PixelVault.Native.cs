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
        const string AppVersion = "0.832";
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
        Action<bool> activeLibraryFolderRefresh;
        LibraryFolderInfo activeSelectedLibraryFolder;

        RichTextBox previewBox;
        TextBox logBox;
        TextBlock status;
        CheckBox recurseBox, keywordsBox;
        ComboBox conflictBox;
        Window photoIndexEditorWindow;
        Window gameIndexEditorWindow;
        Window filenameConventionEditorWindow;
        int previewRefreshVersion;
        CancellationTokenSource previewRefreshCancellation;
        readonly ICoverService coverService;
        readonly IFilenameParserService filenameParserService;
        readonly IFilenameRulesService filenameRulesService;
        readonly IIndexPersistenceService indexPersistenceService;
        readonly IMetadataService metadataService;

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
            coverService = new CoverService(new CoverServiceDependencies
            {
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
                ClearImageCache = delegate { ClearImageCache(); }
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
            libraryWorkspace = new LibraryWorkspaceContext(this);
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
            catch { }

            Title = "PixelVault " + AppVersion;
            Width = PreferredLibraryWindowWidth();
            Height = PreferredLibraryWindowHeight();
            MinWidth = 1200;
            MinHeight = 780;
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
            var available = Math.Max(1200, SystemParameters.WorkArea.Width - 24);
            return Math.Min(available, 2560);
        }
        double PreferredLibraryWindowHeight()
        {
            var available = Math.Max(780, SystemParameters.WorkArea.Height - 24);
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
        string FindSteamGridDbApiTokenInEnvironment()
        {
            foreach (var key in new[] { "PIXELVAULT_STEAMGRIDDB_TOKEN", "STEAMGRIDDB_API_KEY", "STEAMGRIDDB_TOKEN" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }
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
        int NormalizeLibraryFolderTileSize(int value)
        {
            if (value < 140) return 140;
            if (value > 340) return 340;
            return value;
        }
        (int Columns, int TileSize) CalculateResponsiveLibraryFolderLayout(ScrollViewer scrollViewer)
        {
            var viewportWidth = scrollViewer == null ? 0 : scrollViewer.ViewportWidth;
            if (viewportWidth <= 0 && scrollViewer != null) viewportWidth = scrollViewer.ActualWidth;
            viewportWidth = Math.Max(280, viewportWidth - 18);
            // Four columns once the pane is wide enough for ~4× min tile + gutters (default splitter favors the browser pane).
            var columns = viewportWidth >= 700 ? 4 : (viewportWidth >= 600 ? 3 : (viewportWidth >= 400 ? 2 : 1));
            var tileWidth = (int)Math.Floor((viewportWidth - ((columns - 1) * 14)) / columns);
            tileWidth = Math.Max(140, Math.Min(360, tileWidth));
            tileWidth = Math.Max(140, Math.Min(360, (int)(Math.Round(tileWidth / 16d) * 16)));
            return (columns, tileWidth);
        }
        (int Columns, int TileSize) CalculateResponsiveLibraryDetailLayout(ScrollViewer scrollViewer)
        {
            var viewportWidth = scrollViewer == null ? 0 : scrollViewer.ViewportWidth;
            if (viewportWidth <= 0 && scrollViewer != null) viewportWidth = scrollViewer.ActualWidth;
            viewportWidth = Math.Max(320, viewportWidth - 64);
            var columns = viewportWidth >= 1450 ? 3 : (viewportWidth >= 780 ? 2 : 1);
            var tileWidth = (int)Math.Floor((viewportWidth - ((columns - 1) * 8)) / columns);
            tileWidth = Math.Max(180, Math.Min(680, tileWidth));
            tileWidth = Math.Max(180, Math.Min(680, (int)(Math.Round(tileWidth / 24d) * 24)));
            return (columns, tileWidth);
        }
        string NormalizeLibraryFolderSortMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "recent" || normalized == "recently added" || normalized == "recently-added") return "recent";
            if (normalized == "photos" || normalized == "most photos" || normalized == "photo count") return "photos";
            return "platform";
        }
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

        void ShowSettingsWindow()
        {
            var window = new Window
            {
                Title = "PixelVault " + AppVersion + " Settings",
                Width = 1460,
                Height = PreferredSettingsWindowHeight(),
                MinWidth = 1220,
                MinHeight = 820,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White
            };

            var previousStatus = status;
            var previousPreviewBox = previewBox;
            var previousLogBox = logBox;
            var previousRecurseBox = recurseBox;
            var previousKeywordsBox = keywordsBox;
            var previousConflictBox = conflictBox;
            window.Content = BuildUi();
            recurseBox.IsChecked = true;
            keywordsBox.IsChecked = true;
            conflictBox.SelectedIndex = 0;
            LoadLogView();
            RefreshPreview();
            window.Closed += delegate
            {
                status = previousStatus;
                previewBox = previousPreviewBox;
                logBox = previousLogBox;
                recurseBox = previousRecurseBox;
                keywordsBox = previousKeywordsBox;
                conflictBox = previousConflictBox;
            };
            window.ShowDialog();
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
                System.Threading.Tasks.Task.Factory.StartNew(delegate
                {
                    return ScanLibraryMetadataIndex(root, capturedFolderPath, capturedForceRescan, delegate(int currentCount, int totalCount, string detail)
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
                    }, scanCancellation.Token);
                }).ContinueWith(delegate(System.Threading.Tasks.Task<int> scanTask)
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

        string SerializeSourceRoots(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            return string.Join(";", raw
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

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
                }));
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
            var inventory = BuildSourceInventory(recurseRename);
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
            return BuildReviewItems(BuildSourceInventory(false).TopLevelMediaFiles);
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
            return BuildManualMetadataItems(BuildSourceInventory(false).TopLevelMediaFiles, recognizedPaths);
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

        bool ShowManualMetadataWindow(List<ManualMetadataItem> items, bool libraryMode, string contextName, bool importAndEditMode = false)
        {
            if (items == null || items.Count == 0) return true;

            var useFlexiblePreview = libraryMode || importAndEditMode;
            var contextLabel = string.IsNullOrWhiteSpace(contextName) ? "selected folder" : contextName;
            var windowLabel = importAndEditMode ? "Import and Edit" : (libraryMode ? "Edit Library Metadata" : "Missing Data");
            var headerTitleText = importAndEditMode ? "Import and edit captures" : (libraryMode ? "Edit library metadata" : "Add missing metadata");
            var headerDescriptionText = importAndEditMode
                ? items.Count + " file(s) in the upload folder (rule-matched and manual intake). Select files to include in this import, adjust metadata, then continue. Files not selected stay in the upload folder."
                : libraryMode
                    ? items.Count + " capture(s) from " + contextLabel + " are ready for metadata edits. Select one or more files, update the game title prefix, tags, one console tag, an optional capture date/time, and an optional comment. Files can also be reorganized into the proper game folder when the title changes."
                    : items.Count + " capture(s) were left in intake because they did not match a known format. Select one or more files, add a game title prefix, tags, one console tag, an optional capture date/time, and an optional comment before sending them to the destination.";
            var leaveButtonText = libraryMode || importAndEditMode ? "Close" : "Leave Unchanged";
            var finishButtonText = importAndEditMode ? "Continue Import" : (libraryMode ? "Apply Changes" : "Apply and Send");
            var emptySelectionText = libraryMode ? "Choose one or more library images to edit." : importAndEditMode ? "Choose one or more upload files to include in this import." : "Choose unmatched intake images to add metadata.";
            var defaultStatusText = libraryMode ? "Update the game title prefix, tags, one console tag, optional date/time, and an optional comment." : importAndEditMode ? "Adjust metadata for selected files, then continue import." : "Add a game title prefix, tags, one console tag, optional date/time, and an optional comment.";
            var singleSelectionMetaPrefix = libraryMode ? "Library capture time | " : "Filesystem time | ";
            var confirmCaption = libraryMode ? "Library Metadata" : importAndEditMode ? "Import and Edit" : "Manual Intake";
            var confirmMessage = libraryMode
                ? items.Count + " image(s) will be renamed if needed, updated with metadata, and reorganized in the library if their title changes.\n\nApply changes now?"
                : importAndEditMode
                    ? string.Empty
                    : items.Count + " image(s) will be renamed if needed, tagged, updated with metadata, and moved to the destination.\n\nApply changes and send them now?";

            var manualWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " " + windowLabel,
                Width = 1220,
                Height = 1040,
                MinWidth = 1040,
                MinHeight = 920,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var banner = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(18), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 16) };
            var bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bannerStack = new StackPanel();
            bannerStack.Children.Add(new TextBlock { Text = headerTitleText, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            bannerStack.Children.Add(new TextBlock { Text = headerDescriptionText, Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            bannerGrid.Children.Add(bannerStack);
            var leaveButton = Btn(leaveButtonText, null, "#334249", Brushes.White);
            leaveButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(leaveButton, 1);
            bannerGrid.Children.Add(leaveButton);
            var finishButton = Btn(finishButtonText, null, "#275D47", Brushes.White);
            finishButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(finishButton, 2);
            bannerGrid.Children.Add(finishButton);
            banner.Child = bannerGrid;
            root.Children.Add(banner);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);

            var listCard = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(8), Margin = new Thickness(0, 0, 16, 0) };
            var fileList = new ListBox { Background = Brush("#12191E"), BorderThickness = new Thickness(0), Foreground = Brushes.White, Padding = new Thickness(10), HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Extended };
            listCard.Child = fileList;
            main.Children.Add(listCard);

            var detailCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            Grid.SetColumn(detailCard, 1);
            main.Children.Add(detailCard);

            var detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var detailStack = new StackPanel();
            detailScroll.Content = detailStack;
            detailCard.Child = detailScroll;

            var selectedTitle = new TextBlock { Text = "Select one or more captures", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), TextWrapping = TextWrapping.Wrap };
            var selectedMeta = new TextBlock { Text = emptySelectionText, Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
            var previewBorder = new Border { Background = Brush("#EAF0F5"), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            var previewImage = new Image { Stretch = Stretch.Uniform };
            if (useFlexiblePreview)
            {
                previewBorder.MinHeight = 200;
                previewImage.MinHeight = 200;
                previewImage.MaxHeight = 420;
                manualWindow.SizeChanged += delegate
                {
                    if (!manualWindow.IsLoaded) return;
                    var h = Math.Max(220, (manualWindow.ActualHeight - 460) * 0.45);
                    previewImage.MaxHeight = h;
                };
            }
            else previewImage.Height = 320;
            var guessCallout = new Border { Background = Brush("#F4F7F9"), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 16) };
            var guessText = new TextBlock { Text = "Best Guess | No confident match", FontSize = 13, Foreground = Brush("#8B98A3"), TextWrapping = TextWrapping.Wrap };
            guessCallout.Child = guessText;
            var steamLookupLabel = new TextBlock { Text = "Steam lookup", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") };
            var steamLookupGrid = new Grid { Margin = new Thickness(0, 8, 0, 14) };
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            var steamSearchBox = new TextBox { Margin = new Thickness(0, 0, 12, 0), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), FontSize = 14 };
            var steamSearchButton = Btn("Search Steam", null, "#174A73", Brushes.White);
            steamSearchButton.Width = 150;
            steamSearchButton.Height = 40;
            steamSearchButton.Margin = new Thickness(0, 0, 12, 0);
            var steamAppIdBox = new TextBox { Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), FontSize = 14 };
            steamLookupGrid.Children.Add(steamSearchBox);
            Grid.SetColumn(steamSearchButton, 1);
            steamLookupGrid.Children.Add(steamSearchButton);
            Grid.SetColumn(steamAppIdBox, 2);
            steamLookupGrid.Children.Add(steamAppIdBox);
            var steamLookupStatus = new TextBlock { Text = "Search by game title or numeric Steam AppID (Search looks up the store name), or paste an AppID in the box on the right.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap };
            var knownGameChoices = new List<string>();
            var knownGameChoiceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownGameChoiceNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var gameNameBox = new ComboBox
            {
                Margin = new Thickness(0, 8, 0, 14),
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 14,
                FontFamily = new FontFamily("Cascadia Mono"),
                IsEditable = true,
                IsTextSearchEnabled = true,
                StaysOpenOnEdit = true
            };
            var tagsBox = new TextBox { Margin = new Thickness(0, 8, 0, 14), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 14 };
            var photographyBox = new CheckBox { Content = "Add Game Photography tag", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 14, 10), IsThreeState = true };
            var tagSeparator = new Border { Width = 1, Height = 20, Background = Brush("#D7E1E8"), Margin = new Thickness(2, 2, 16, 10), VerticalAlignment = VerticalAlignment.Center };
            var steamBox = new CheckBox { Content = "Steam", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var pcBox = new CheckBox { Content = "PC", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var ps5Box = new CheckBox { Content = "PS5", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var xboxBox = new CheckBox { Content = "Xbox", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var otherBox = new CheckBox { Content = "Other", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 12, 10), IsThreeState = true };
            var otherPlatformBox = new TextBox { Width = 190, Margin = new Thickness(0, 0, 0, 10), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), FontSize = 13, IsEnabled = false };
            var tagToggleRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            tagToggleRow.Children.Add(photographyBox);
            tagToggleRow.Children.Add(tagSeparator);
            tagToggleRow.Children.Add(steamBox);
            tagToggleRow.Children.Add(pcBox);
            tagToggleRow.Children.Add(ps5Box);
            tagToggleRow.Children.Add(xboxBox);
            tagToggleRow.Children.Add(otherBox);
            tagToggleRow.Children.Add(otherPlatformBox);
            var useCustomTimeBox = new CheckBox { Content = "Use custom date/time", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 8), IsThreeState = true };
            var dateRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var captureDatePicker = new DatePicker { Width = 170 };
            var hourBox = new ComboBox { Width = 68, Margin = new Thickness(12, 0, 0, 0) };
            for (int hour = 1; hour <= 12; hour++) hourBox.Items.Add(hour.ToString());
            var minuteBox = new ComboBox { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            for (int minute = 0; minute < 60; minute++) minuteBox.Items.Add(minute.ToString("00"));
            var ampmBox = new ComboBox { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            ampmBox.Items.Add("AM");
            ampmBox.Items.Add("PM");
            dateRow.Children.Add(captureDatePicker);
            dateRow.Children.Add(hourBox);
            dateRow.Children.Add(minuteBox);
            dateRow.Children.Add(ampmBox);
            var commentBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120, Margin = new Thickness(0, 8, 0, 0), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 14 };
            var statusText = new TextBlock { Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

            detailStack.Children.Add(selectedTitle);
            detailStack.Children.Add(selectedMeta);
            detailStack.Children.Add(previewBorder);
            detailStack.Children.Add(guessCallout);
            detailStack.Children.Add(steamLookupLabel);
            detailStack.Children.Add(steamLookupGrid);
            detailStack.Children.Add(steamLookupStatus);
            detailStack.Children.Add(new TextBlock { Text = "Game title to prepend", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(gameNameBox);
            detailStack.Children.Add(new TextBlock { Text = "Additional tags", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(tagsBox);
            detailStack.Children.Add(tagToggleRow);
            detailStack.Children.Add(new TextBlock { Text = "Capture date and time", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(useCustomTimeBox);
            detailStack.Children.Add(new TextBlock { Text = "If left off, PixelVault uses the existing filesystem timestamp.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            detailStack.Children.Add(dateRow);
            var deleteBeforeBox = new CheckBox
            {
                Content = "Delete selected file(s) before import (skipped for metadata and move)",
                Foreground = Brush("#9B2C2C"),
                Margin = new Thickness(0, 0, 0, 12),
                Visibility = importAndEditMode ? Visibility.Visible : Visibility.Collapsed
            };
            detailStack.Children.Add(deleteBeforeBox);
            detailStack.Children.Add(new TextBlock { Text = "Comment for Immich description", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(commentBox);
            detailStack.Children.Add(statusText);

            bool suppressSync = false;
            bool dialogReady = false;
            CancellationTokenSource steamSearchCancellation = null;
            int steamSearchRequestVersion = 0;
            var badgeBlocks = new Dictionary<ManualMetadataItem, TextBlock>();
            var tileBorders = new Dictionary<ManualMetadataItem, Border>();
            var selectedItems = new List<ManualMetadataItem>();
            Action refreshSelectionStatus = null;
            Action syncSelectedSteamAppIds = null;
            Action refreshGameTitleChoices = delegate
            {
                var currentText = gameNameBox.Text;
                var rows = LoadSavedGameIndexRows(libraryRoot);
                if (rows.Count == 0) rows = LoadGameIndexEditorRows(libraryRoot);
                var loadedChoices = rows
                    .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                    .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.PlatformLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(row => BuildGameTitleChoiceLabel(row.Name, row.PlatformLabel))
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var extraChoice in knownGameChoices.Where(label => !string.IsNullOrWhiteSpace(label)))
                {
                    if (!loadedChoices.Contains(extraChoice, StringComparer.OrdinalIgnoreCase)) loadedChoices.Add(extraChoice);
                }
                knownGameChoices = loadedChoices;
                knownGameChoiceSet = new HashSet<string>(knownGameChoices, StringComparer.OrdinalIgnoreCase);
                knownGameChoiceNameMap = rows
                    .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                    .Select(row => new { Label = BuildGameTitleChoiceLabel(row.Name, row.PlatformLabel), Name = NormalizeGameIndexName(row.Name, row.FolderPath) })
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Label) && !string.IsNullOrWhiteSpace(entry.Name))
                    .GroupBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => CleanTag(group.Key), group => group.First().Name, StringComparer.OrdinalIgnoreCase);
                foreach (var extraChoice in knownGameChoices)
                {
                    var normalizedChoice = CleanTag(extraChoice);
                    if (knownGameChoiceNameMap.ContainsKey(normalizedChoice)) continue;
                    var extraName = ExtractGameNameFromChoiceLabel(extraChoice);
                    if (!string.IsNullOrWhiteSpace(extraName)) knownGameChoiceNameMap[normalizedChoice] = extraName;
                }
                gameNameBox.ItemsSource = null;
                gameNameBox.ItemsSource = knownGameChoices;
                gameNameBox.Text = currentText;
            };
            Action syncSelectedGameNames = delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                var selectedTitleText = CleanTag(gameNameBox.Text);
                string mappedName;
                if (knownGameChoiceNameMap.TryGetValue(selectedTitleText, out mappedName)) selectedTitleText = mappedName;
                else selectedTitleText = ExtractGameNameFromChoiceLabel(selectedTitleText);
                foreach (var item in selectedItems) item.GameName = selectedTitleText;
                refreshSelectionStatus();
            };
            syncSelectedSteamAppIds = delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                var cleanedAppId = Regex.Replace((steamAppIdBox.Text ?? string.Empty).Trim(), @"[^\d]", string.Empty);
                if (!string.Equals(steamAppIdBox.Text ?? string.Empty, cleanedAppId, StringComparison.Ordinal))
                {
                    suppressSync = true;
                    steamAppIdBox.Text = cleanedAppId;
                    steamAppIdBox.SelectionStart = steamAppIdBox.Text.Length;
                    suppressSync = false;
                }
                foreach (var item in selectedItems) item.SteamAppId = cleanedAppId;
                if (!string.IsNullOrWhiteSpace(cleanedAppId))
                {
                    foreach (var item in selectedItems)
                    {
                        item.TagSteam = true;
                        item.TagPc = false;
                        item.TagPs5 = false;
                        item.TagXbox = false;
                        item.TagOther = false;
                        item.CustomPlatformTag = string.Empty;
                        item.ForceTagMetadataWrite = true;
                    }
                }
                refreshSelectionStatus();
            };

            Func<ManualMetadataItem, string> manualBadgeLabel = delegate(ManualMetadataItem item)
            {
                if (item != null && item.IntakeRuleMatched) return "Auto";
                if (item.TagSteam) return "Steam";
                if (item.TagPc) return "PC";
                if (item.TagPs5) return "PS5";
                if (item.TagXbox) return "Xbox";
                if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) return CleanTag(item.CustomPlatformTag);
                return "Manual";
            };

            Func<string, Brush> manualBadgeBrush = delegate(string label)
            {
                if (string.Equals(label, "Auto", StringComparison.OrdinalIgnoreCase)) return Brush("#5CB88A");
                if (string.Equals(label, "Steam", StringComparison.OrdinalIgnoreCase)) return Brush("#69A7FF");
                if (string.Equals(label, "PC", StringComparison.OrdinalIgnoreCase)) return Brush("#7F8EA3");
                if (string.Equals(label, "PS5", StringComparison.OrdinalIgnoreCase)) return Brush("#4F83FF");
                if (string.Equals(label, "Xbox", StringComparison.OrdinalIgnoreCase)) return Brush("#66C47A");
                return Brush("#D0A15F");
            };

            Func<IEnumerable<ManualMetadataItem>, Func<ManualMetadataItem, string>, string> sharedText = delegate(IEnumerable<ManualMetadataItem> selection, Func<ManualMetadataItem, string> getter)
            {
                var values = selection.Select(getter).Select(v => (v ?? string.Empty).Trim()).Distinct(StringComparer.Ordinal).ToArray();
                return values.Length == 1 ? values[0] : string.Empty;
            };

            Func<IEnumerable<ManualMetadataItem>, Func<ManualMetadataItem, bool>, bool?> sharedBool = delegate(IEnumerable<ManualMetadataItem> selection, Func<ManualMetadataItem, bool> getter)
            {
                var values = selection.Select(getter).Distinct().ToArray();
                return values.Length == 1 ? (bool?)values[0] : null;
            };

            Func<IEnumerable<ManualMetadataItem>, string> sharedFilenameGuess = delegate(IEnumerable<ManualMetadataItem> selection)
            {
                var guesses = selection.Select(item => FilenameGuessLabel(item == null ? string.Empty : item.FileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (guesses.Length == 0) return "Best Guess | No confident match";
                if (guesses.Length == 1) return "Best Guess | " + guesses[0];
                return "Best Guess | Mixed guesses";
            };

            Func<int, UIElement> buildMultiPreview = delegate(int count)
            {
                var multiH = useFlexiblePreview ? Math.Min(400, Math.Max(240, previewImage.MaxHeight)) : 320;
                var grid = new Grid { Height = multiH };
                var art = new Grid { Width = 260, Height = 190, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var back = new Border { Width = 136, Height = 104, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(64, -44, 0, 0) };
                var mid = new Border { Width = 148, Height = 112, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, -20, 0, 0) };
                var front = new Border { Width = 160, Height = 120, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
                var frontGrid = new Grid();                frontGrid.Children.Add(new Border { Width = 78, Height = 78, Background = Brush("#161C20"), CornerRadius = new CornerRadius(39), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } });
                front.Child = frontGrid;
                art.Children.Add(back);
                art.Children.Add(mid);
                art.Children.Add(front);
                grid.Children.Add(art);
                return grid;
            };

            Action refreshDateControls = delegate
            {
                var enabled = useCustomTimeBox.IsChecked == true;
                captureDatePicker.IsEnabled = enabled;
                hourBox.IsEnabled = enabled;
                minuteBox.IsEnabled = enabled;
                ampmBox.IsEnabled = enabled;
                otherPlatformBox.IsEnabled = otherBox.IsChecked == true;
            };

            Action<IEnumerable<ManualMetadataItem>, string> applyConsoleSelection = delegate(IEnumerable<ManualMetadataItem> selection, string platform)
            {
                foreach (var item in selection)
                {
                    item.TagSteam = string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase);
                    item.TagPc = string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase);
                    item.TagPs5 = string.Equals(platform, "PS5", StringComparison.OrdinalIgnoreCase);
                    item.TagXbox = string.Equals(platform, "Xbox", StringComparison.OrdinalIgnoreCase);
                    item.TagOther = string.Equals(platform, "Other", StringComparison.OrdinalIgnoreCase);
                    if (!item.TagOther) item.CustomPlatformTag = string.Empty;
                    item.ForceTagMetadataWrite = true;
                }
            };

            Action refreshTileBadges = delegate
            {
                foreach (var pair in badgeBlocks)
                {
                    var label = manualBadgeLabel(pair.Key);
                    pair.Value.Text = "[" + label + "]";
                    pair.Value.Foreground = manualBadgeBrush(label);
                }
            };

            Action refreshTileSelectionState = delegate
            {
                foreach (var pair in tileBorders)
                {
                    var isSelected = selectedItems.Contains(pair.Key);
                    pair.Value.Background = isSelected ? Brush("#24323C") : Brush("#1A2329");
                    pair.Value.BorderBrush = isSelected ? Brush("#69A7FF") : Brush("#1A2329");
                    pair.Value.BorderThickness = new Thickness(isSelected ? 2 : 1);
                }
            };

            Action saveSelectedDateTime = delegate
            {
                if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return;
                var fallback = selectedItems[0].CaptureTime;
                var date = captureDatePicker.SelectedDate ?? fallback.Date;
                int hour12 = ParseInt(Convert.ToString(hourBox.SelectedItem));
                if (hour12 < 1 || hour12 > 12)
                {
                    var fallbackHour = fallback.Hour % 12;
                    hour12 = fallbackHour == 0 ? 12 : fallbackHour;
                }
                int minute = ParseInt(Convert.ToString(minuteBox.SelectedItem));
                var ampm = Convert.ToString(ampmBox.SelectedItem);
                if (string.IsNullOrWhiteSpace(ampm)) ampm = fallback.Hour >= 12 ? "PM" : "AM";
                int hour24 = hour12 % 12;
                if (string.Equals(ampm, "PM", StringComparison.OrdinalIgnoreCase)) hour24 += 12;
                var newTime = new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0);
                foreach (var item in selectedItems) item.CaptureTime = newTime;
            };
            refreshSelectionStatus = delegate
            {
                var notes = new List<string>();
                if (selectedItems.Count > 1) notes.Add(selectedItems.Count + " files selected");
                if (!string.IsNullOrWhiteSpace(gameNameBox.Text)) notes.Add("rename prefix ready");
                if (!string.IsNullOrWhiteSpace(steamAppIdBox.Text)) notes.Add("Steam AppID " + steamAppIdBox.Text);
                if (!string.IsNullOrWhiteSpace(commentBox.Text)) notes.Add("comment saved");
                var tagCount = ParseTagText(tagsBox.Text).Length;
                if (tagCount > 0) notes.Add(tagCount + " extra tag(s)");
                if (useCustomTimeBox.IsChecked == true) notes.Add("custom capture time");
                if (photographyBox.IsChecked == true) notes.Add(GamePhotographyTag + " tag enabled");
                if (steamBox.IsChecked == true) notes.Add("platform tag: Steam");
                else if (pcBox.IsChecked == true) notes.Add("platform tag: PC");
                else if (ps5Box.IsChecked == true) notes.Add("platform tag: PS5");
                else if (xboxBox.IsChecked == true) notes.Add("platform tag: Xbox");
                else                 if (otherBox.IsChecked == true) notes.Add("platform tag: " + CleanTag(otherPlatformBox.Text));
                if (importAndEditMode && selectedItems.Count > 0)
                {
                    var del = selectedItems.Count(i => i.DeleteBeforeProcessing);
                    if (del > 0) notes.Add(del + " marked for delete before import");
                }
                statusText.Text = notes.Count == 0 ? defaultStatusText : string.Join(" | ", notes.ToArray()) + ".";
            };
            refreshGameTitleChoices();


            Action refreshSelectionUi = delegate
            {
                if (selectedItems.Count == 0)
                {
                    selectedTitle.Text = "Select one or more captures";
                    selectedMeta.Text = emptySelectionText;
                    guessText.Text = "Best Guess | No confident match";
                    previewBorder.Child = buildMultiPreview(0);
                    suppressSync = true;
                    steamSearchBox.Text = string.Empty;
                    steamAppIdBox.Text = string.Empty;
                    steamLookupStatus.Text = "Search by game title or numeric Steam AppID, or paste an AppID in the box on the right.";
                    gameNameBox.Text = string.Empty;
                    tagsBox.Text = string.Empty;
                    commentBox.Text = string.Empty;
                    photographyBox.IsChecked = false;
                    steamBox.IsChecked = false;
                    ps5Box.IsChecked = false;
                    xboxBox.IsChecked = false;
                    pcBox.IsChecked = false;
                    otherBox.IsChecked = false;
                    otherPlatformBox.Text = string.Empty;
                    useCustomTimeBox.IsChecked = false;
                    captureDatePicker.SelectedDate = null;
                    hourBox.SelectedIndex = -1;
                    minuteBox.SelectedIndex = -1;
                    ampmBox.SelectedIndex = -1;
                    if (importAndEditMode) deleteBeforeBox.IsChecked = false;
                    suppressSync = false;
                    refreshDateControls();
                    statusText.Text = defaultStatusText;
                    refreshTileSelectionState();
                    return;
                }

                suppressSync = true;
                if (selectedItems.Count == 1)
                {
                    var item = selectedItems[0];
                    selectedTitle.Text = item.FileName;
                    selectedMeta.Text = singleSelectionMetaPrefix + FormatFriendlyTimestamp(GetLibraryDate(item.FilePath));
                    guessText.Text = sharedFilenameGuess(selectedItems);
                    steamLookupStatus.Text = string.IsNullOrWhiteSpace(item.SteamAppId)
                        ? (IsSteamManualExportWithoutAppId(item.FileName) ? "Steam-style export detected. Search the game name to attach its AppID before import." : "Search by game name to fetch a Steam AppID, or paste one directly.")
                        : "Steam AppID " + item.SteamAppId + " will be saved with this import.";
                    previewBorder.Child = previewImage;
                    previewImage.Source = null;
                    QueueImageLoad(
                        previewImage,
                        item.FilePath,
                        1600,
                        delegate(BitmapImage loaded)
                        {
                            previewImage.Source = loaded;
                        },
                        true,
                        delegate
                        {
                            return manualWindow.IsLoaded
                                && previewBorder.Child == previewImage
                                && selectedItems.Count == 1
                                && ReferenceEquals(selectedItems[0], item);
                        });
                }
                else
                {
                    selectedTitle.Text = selectedItems.Count + " captures selected";
                    selectedMeta.Text = "Edits here apply to all selected files. Mixed values show as blank or indeterminate.";
                    guessText.Text = sharedFilenameGuess(selectedItems);
                    steamLookupStatus.Text = "Search by title or Steam AppID to apply one AppID to the selected captures, or paste it directly.";
                    previewBorder.Child = buildMultiPreview(selectedItems.Count);
                    previewImage.Source = null;
                }

                steamSearchBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item)
                {
                    return item.GameName;
                });
                steamAppIdBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.SteamAppId; });
                gameNameBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item)
                {
                    var displayLabel = BuildGameTitleChoiceLabel(item.GameName, DetermineManualMetadataPlatformLabel(item));
                    return knownGameChoiceSet.Contains(displayLabel) ? displayLabel : item.GameName;
                });
                tagsBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.TagText; });
                commentBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.Comment; });
                photographyBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.AddPhotographyTag; });
                useCustomTimeBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.UseCustomCaptureTime; });
                steamBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagSteam; });
                pcBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagPc; });
                ps5Box.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagPs5; });
                xboxBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagXbox; });
                otherBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagOther; });
                otherPlatformBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.CustomPlatformTag; });
                if (importAndEditMode) deleteBeforeBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.DeleteBeforeProcessing; });

                var first = selectedItems[0];
                captureDatePicker.SelectedDate = first.CaptureTime.Date;
                var hour12 = first.CaptureTime.Hour % 12;
                if (hour12 == 0) hour12 = 12;
                hourBox.SelectedItem = hour12.ToString();
                minuteBox.SelectedItem = first.CaptureTime.Minute.ToString("00");
                ampmBox.SelectedItem = first.CaptureTime.Hour >= 12 ? "PM" : "AM";
                suppressSync = false;
                refreshDateControls();

                refreshSelectionStatus();
                refreshTileSelectionState();
            };

            foreach (var item in items)
            {
                var tile = new ListBoxItem { Tag = item, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                var tileBorder = new Border { Background = Brush("#1A2329"), BorderBrush = Brush("#1A2329"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10) };
                tileBorders[item] = tileBorder;
                var tileStack = new StackPanel();
                var badge = new TextBlock { Text = "[Manual]", Foreground = Brush("#D0A15F"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
                badgeBlocks[item] = badge;
                tileStack.Children.Add(badge);
                tileStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
                tileStack.Children.Add(new TextBlock { Text = FormatFriendlyTimestamp(item.CaptureTime), Foreground = Brush("#B7C6C0"), Margin = new Thickness(0, 6, 0, 0) });
                tileBorder.Child = tileStack;
                tile.Content = tileBorder;
                fileList.Items.Add(tile);
            }
            refreshTileBadges();

            fileList.SelectionChanged += delegate
            {
                selectedItems = fileList.SelectedItems.Cast<ListBoxItem>().Where(i => i.Tag is ManualMetadataItem).Select(i => (ManualMetadataItem)i.Tag).ToList();
                refreshSelectionUi();
            };

            gameNameBox.SelectionChanged += delegate { syncSelectedGameNames(); };
            gameNameBox.LostKeyboardFocus += delegate { syncSelectedGameNames(); };
            gameNameBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(delegate(object sender, TextChangedEventArgs e)
            {
                syncSelectedGameNames();
            }));
            steamAppIdBox.TextChanged += delegate
            {
                syncSelectedSteamAppIds();
            };
            steamAppIdBox.LostKeyboardFocus += delegate
            {
                syncSelectedSteamAppIds();
            };
            steamSearchButton.Click += delegate
            {
                if (steamSearchCancellation != null)
                {
                    steamLookupStatus.Text = "Canceling Steam search...";
                    steamSearchCancellation.Cancel();
                    return;
                }
                if (selectedItems.Count == 0)
                {
                    MessageBox.Show("Select one or more captures before searching Steam.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var query = CleanTag(steamSearchBox.Text);
                string mappedName;
                if (knownGameChoiceNameMap.TryGetValue(query, out mappedName)) query = mappedName;
                else query = ExtractGameNameFromChoiceLabel(query);
                if (string.IsNullOrWhiteSpace(query))
                {
                    query = CleanTag(gameNameBox.Text);
                    if (knownGameChoiceNameMap.TryGetValue(query, out mappedName)) query = mappedName;
                    else query = ExtractGameNameFromChoiceLabel(query);
                }
                if (string.IsNullOrWhiteSpace(query))
                {
                    var firstItem = selectedItems[0];
                    if (firstItem != null && !string.IsNullOrWhiteSpace(firstItem.GameName)) query = CleanTag(firstItem.GameName);
                }
                if (string.IsNullOrWhiteSpace(query))
                {
                    MessageBox.Show("Enter a game title or a numeric Steam AppID in the search box, then click Search Steam.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                steamLookupStatus.Text = "Searching Steam for \"" + query + "\"...";
                var searchQuery = query;
                var searchCancellation = new CancellationTokenSource();
                steamSearchCancellation = searchCancellation;
                var searchVersion = ++steamSearchRequestVersion;
                steamSearchButton.Content = "Cancel Search";
                Task.Factory.StartNew(delegate
                {
                    searchCancellation.Token.ThrowIfCancellationRequested();
                    return Tuple.Create(searchQuery, SearchSteamAppMatches(searchQuery, searchCancellation.Token));
                }, searchCancellation.Token).ContinueWith(delegate(Task<Tuple<string, List<Tuple<string, string>>>> searchTask)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (ReferenceEquals(steamSearchCancellation, searchCancellation))
                        {
                            steamSearchCancellation.Dispose();
                            steamSearchCancellation = null;
                        }
                        steamSearchButton.Content = "Search Steam";
                        if (!manualWindow.IsLoaded || searchVersion != steamSearchRequestVersion) return;
                        if (searchTask.IsCanceled || searchCancellation.IsCancellationRequested)
                        {
                            steamLookupStatus.Text = "Steam search canceled. Pick a match or paste the AppID directly.";
                            return;
                        }
                        if (searchTask.IsFaulted)
                        {
                            steamLookupStatus.Text = "Steam lookup failed. Try again or paste the AppID directly.";
                            return;
                        }
                        var result = searchTask.Result;
                        var matches = result == null || result.Item2 == null ? new List<Tuple<string, string>>() : result.Item2;
                        if (matches.Count == 0)
                        {
                            steamLookupStatus.Text = "No Steam AppID match found for \"" + searchQuery + "\".";
                            return;
                        }

                        var chosenMatch = matches.Count == 1 ? matches[0] : ShowSteamAppMatchWindow(searchQuery, matches);
                        if (chosenMatch == null)
                        {
                            steamLookupStatus.Text = "Steam search canceled. Pick a match or paste the AppID directly.";
                            return;
                        }

                        var resolvedAppId = chosenMatch.Item1 ?? string.Empty;
                        var resolvedTitle = chosenMatch.Item2 ?? string.Empty;

                        suppressSync = true;
                        steamSearchBox.Text = string.IsNullOrWhiteSpace(resolvedTitle) ? searchQuery : resolvedTitle;
                        steamAppIdBox.Text = resolvedAppId;
                        suppressSync = false;
                        foreach (var item in selectedItems)
                        {
                            item.SteamAppId = resolvedAppId;
                            if (!string.IsNullOrWhiteSpace(resolvedTitle)) item.GameName = resolvedTitle;
                        }
                        applyConsoleSelection(selectedItems, "Steam");
                        refreshTileBadges();
                        if (!string.IsNullOrWhiteSpace(resolvedTitle))
                        {
                            if (knownGameChoiceSet.Add(BuildGameTitleChoiceLabel(resolvedTitle, "Steam"))) knownGameChoices.Add(BuildGameTitleChoiceLabel(resolvedTitle, "Steam"));
                            refreshGameTitleChoices();
                            suppressSync = true;
                            gameNameBox.Text = resolvedTitle;
                            suppressSync = false;
                            syncSelectedGameNames();
                        }
                        else
                        {
                            steamLookupStatus.Text = "Selected Steam AppID " + resolvedAppId + ".";
                            refreshSelectionStatus();
                        }
                        refreshSelectionUi();
                    }));
                }, TaskScheduler.Default);
            };
            tagsBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.TagText = tagsBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionStatus();
            };
            commentBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.Comment = commentBox.Text;
                refreshSelectionStatus();
            };
            photographyBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.AddPhotographyTag = true;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionUi();
            };
            photographyBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.AddPhotographyTag = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionUi();
            };
            steamBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "Steam");
                refreshTileBadges();
                refreshSelectionUi();
            };
            steamBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (steamBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagSteam = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            pcBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "PC");
                refreshTileBadges();
                refreshSelectionUi();
            };
            pcBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (pcBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagPc = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            ps5Box.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "PS5");
                refreshTileBadges();
                refreshSelectionUi();
            };
            ps5Box.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (ps5Box.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagPs5 = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            xboxBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "Xbox");
                refreshTileBadges();
                refreshSelectionUi();
            };
            xboxBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (xboxBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagXbox = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherBox.Checked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "Other");
                foreach (var item in selectedItems)
                {
                    item.CustomPlatformTag = otherPlatformBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherBox.Unchecked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                if (otherBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagOther = false;
                    item.CustomPlatformTag = string.Empty;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherPlatformBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.CustomPlatformTag = otherPlatformBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionStatus();
            };
            useCustomTimeBox.Checked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.UseCustomCaptureTime = true;
                saveSelectedDateTime();
                refreshSelectionUi();
            };
            useCustomTimeBox.Unchecked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.UseCustomCaptureTime = false;
                refreshSelectionUi();
            };
            captureDatePicker.SelectedDateChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            hourBox.SelectionChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            minuteBox.SelectionChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            ampmBox.SelectionChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };

            deleteBeforeBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0 || !importAndEditMode) return;
                foreach (var item in selectedItems) item.DeleteBeforeProcessing = true;
                refreshSelectionStatus();
            };
            deleteBeforeBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0 || !importAndEditMode) return;
                foreach (var item in selectedItems) item.DeleteBeforeProcessing = false;
                refreshSelectionStatus();
            };

            finishButton.Click += delegate
            {
                if (!dialogReady || !manualWindow.IsLoaded) return;
                var pendingItems = selectedItems.Distinct().ToList();
                if (pendingItems.Count == 0)
                {
                    var emptyMsg = libraryMode
                        ? "Select at least one library image to update."
                        : importAndEditMode
                            ? "Select at least one upload file to include in this import."
                            : "Select at least one unmatched image to send.";
                    MessageBox.Show(emptyMsg, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (useCustomTimeBox.IsChecked == true) saveSelectedDateTime();
                foreach (var item in pendingItems)
                {
                    var tagNames = new HashSet<string>(ParseTagText(item.TagText), StringComparer.OrdinalIgnoreCase);
                    if (tagNames.Contains("Steam")) applyConsoleSelection(new[] { item }, "Steam");
                    else if (tagNames.Contains("PC")) applyConsoleSelection(new[] { item }, "PC");
                    else if (tagNames.Contains("PS5") || tagNames.Contains("PlayStation")) applyConsoleSelection(new[] { item }, "PS5");
                    else if (tagNames.Contains("Xbox")) applyConsoleSelection(new[] { item }, "Xbox");
                }
                if (pendingItems.Any(item => item.TagOther && string.IsNullOrWhiteSpace(CleanTag(item.CustomPlatformTag))))
                {
                    MessageBox.Show("Enter a platform name in the Other box before applying changes.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (importAndEditMode)
                {
                    ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchanged(pendingItems.Where(i => i != null && !i.DeleteBeforeProcessing));
                }
                var gameRows = LoadSavedGameIndexRows(libraryRoot);
                var unresolvedMasterRecords = pendingItems
                    .Where(item => item != null && !item.DeleteBeforeProcessing)
                    .Select(item => new
                    {
                        Item = item,
                        Name = NormalizeGameIndexName(
                            string.IsNullOrWhiteSpace(item.GameName)
                                ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                                : item.GameName),
                        PlatformLabel = DetermineManualMetadataPlatformLabel(item),
                        PreferredGameId = ManualMetadataChangesGroupingIdentity(item) ? string.Empty : item.GameId
                    })
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                    .Where(entry => FindSavedGameIndexRowByIdentity(gameRows, entry.Name, entry.PlatformLabel) == null
                        && FindSavedGameIndexRowById(gameRows, entry.PreferredGameId) == null)
                    .Select(entry => BuildGameTitleChoiceLabel(entry.Name, entry.PlatformLabel))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (unresolvedMasterRecords.Count > 0)
                {
                    var preview = string.Join(Environment.NewLine, unresolvedMasterRecords.Take(8).Select(title => "- " + title).ToArray());
                    if (unresolvedMasterRecords.Count > 8) preview += Environment.NewLine + "- ...";
                    var addChoice = MessageBox.Show(
                        "These game record" + (unresolvedMasterRecords.Count == 1 ? " is" : "s are") + " not in the game index yet:" + Environment.NewLine + Environment.NewLine +
                        preview + Environment.NewLine + Environment.NewLine +
                        "Add " + (unresolvedMasterRecords.Count == 1 ? "it" : "them") + " as new master game record" + (unresolvedMasterRecords.Count == 1 ? "" : "s") + "?",
                        "Add New Game",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question);
                    if (addChoice != MessageBoxResult.OK) return;
                    foreach (var title in unresolvedMasterRecords.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (knownGameChoiceSet.Add(title)) knownGameChoices.Add(title);
                    }
                    refreshGameTitleChoices();
                    foreach (var item in pendingItems)
                    {
                        if (item.DeleteBeforeProcessing) continue;
                        var resolvedName = NormalizeGameIndexName(
                            string.IsNullOrWhiteSpace(item.GameName)
                                ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                                : item.GameName);
                        var resolvedPlatform = DetermineManualMetadataPlatformLabel(item);
                        var preferredGameId = ManualMetadataChangesGroupingIdentity(item) ? string.Empty : item.GameId;
                        if (FindSavedGameIndexRowByIdentity(gameRows, resolvedName, resolvedPlatform) != null) continue;
                        if (!string.IsNullOrWhiteSpace(preferredGameId) && FindSavedGameIndexRowById(gameRows, preferredGameId) != null) continue;
                        EnsureGameIndexRowForAssignment(gameRows, resolvedName, resolvedPlatform, preferredGameId);
                    }
                }
                var confirmText = libraryMode
                    ? pendingItems.Count + " selected image(s) will be renamed if needed, updated with metadata, and reorganized in the library if their title changes.\n\nApply changes now?"
                    : importAndEditMode
                        ? pendingItems.Count + " selected file(s) will continue through import (Steam rename if applicable, optional delete, metadata, move to destination).\n\nFiles not selected stay in the upload folder.\n\nContinue?"
                        : pendingItems.Count + " image(s) will be renamed if needed, tagged, updated with metadata, and moved to the destination.\n\nApply changes and send them now?";
                var confirm = MessageBox.Show(confirmText, confirmCaption, MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.OK) return;
                foreach (var item in pendingItems)
                {
                    if (item.DeleteBeforeProcessing) continue;
                    var resolvedName = NormalizeGameIndexName(
                        string.IsNullOrWhiteSpace(item.GameName)
                            ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                            : item.GameName);
                    if (!string.IsNullOrWhiteSpace(resolvedName)) item.GameName = resolvedName;
                    var preferredGameId = ManualMetadataChangesGroupingIdentity(item) ? string.Empty : item.GameId;
                    var resolvedRow = ResolveExistingGameIndexRowForAssignment(gameRows, item.GameName, DetermineManualMetadataPlatformLabel(item), preferredGameId);
                    item.GameId = resolvedRow == null ? string.Empty : resolvedRow.GameId;
                    if (resolvedRow != null)
                    {
                        if (!string.IsNullOrWhiteSpace(resolvedRow.Name)) item.GameName = resolvedRow.Name;
                        if (!string.IsNullOrWhiteSpace(item.SteamAppId))
                        {
                            resolvedRow.SteamAppId = item.SteamAppId;
                            resolvedRow.SuppressSteamAppIdAutoResolve = false;
                        }
                    }
                }
                SaveSavedGameIndexRows(libraryRoot, gameRows);
                items.Clear();
                items.AddRange(pendingItems);
                manualWindow.DialogResult = true;
                manualWindow.Close();
            };
            leaveButton.Click += delegate
            {
                if (!dialogReady || !manualWindow.IsLoaded) return;
                manualWindow.DialogResult = false;
                manualWindow.Close();
            };

            manualWindow.Content = root;
            manualWindow.Closing += delegate
            {
                if (steamSearchCancellation != null && !steamSearchCancellation.IsCancellationRequested)
                {
                    steamSearchCancellation.Cancel();
                }
            };
            manualWindow.Loaded += delegate
            {
                dialogReady = true;
                if (items.Count > 0)
                {
                    if (libraryMode && !importAndEditMode)
                    {
                        var firstEntry = fileList.Items.Cast<ListBoxItem>().FirstOrDefault();
                        if (firstEntry != null) firstEntry.IsSelected = true;
                    }
                    else
                    {
                        foreach (ListBoxItem entry in fileList.Items) entry.IsSelected = true;
                    }
                }
                else
                {
                    refreshSelectionUi();
                }
            };
            var result = manualWindow.ShowDialog();
            return result == true;
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
                RunManualRename(items, delegate(int current, int total, string detail)
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
                UpsertLibraryMetadataIndexEntries(items, libraryRoot);
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
            lock (libraryMaintenanceSync)
            {
                var stopwatch = Stopwatch.StartNew();
                var stamp = BuildLibraryFolderInventoryStamp(root);
                if (!forceRefresh)
                {
                    var cached = LoadLibraryFolderCache(root, stamp);
                    if (cached != null)
                    {
                        var cacheUpdated = PopulateMissingLibraryFolderSortKeys(cached);
                        if (ApplySavedGameIndexRows(root, cached))
                        {
                            cacheUpdated = true;
                        }
                        if (cacheUpdated)
                        {
                            SaveLibraryFolderCache(root, stamp, cached);
                        }
                        Log("Library folder cache hit.");
                        stopwatch.Stop();
                        LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=hit; folders=" + cached.Count + "; forceRefresh=" + forceRefresh, 40);
                        return cached;
                    }
                }
                Log("Refreshing library folder cache.");
                var fresh = LoadLibraryFolders(root);
                ApplySavedGameIndexRows(root, fresh);
                SaveLibraryFolderCache(root, stamp, fresh);
                stopwatch.Stop();
                LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=rebuild; folders=" + fresh.Count + "; forceRefresh=" + forceRefresh, 40);
                return fresh;
            }
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

        string TryResolveSteamGridDbIdBySteamAppId(string steamAppId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return coverService.TryResolveSteamGridDbIdBySteamAppId(steamAppId, cancellationToken);
        }

        string TryResolveSteamGridDbIdByName(string title, CancellationToken cancellationToken = default(CancellationToken))
        {
            return coverService.TryResolveSteamGridDbIdByName(title, cancellationToken);
        }

        string ResolveBestLibraryFolderSteamGridDbId(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
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
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder, true, cancellationToken);
                steamGridDbId = !string.IsNullOrWhiteSpace(appId)
                    ? TryResolveSteamGridDbIdBySteamAppId(appId, cancellationToken)
                    : null;
            }
            if (string.IsNullOrWhiteSpace(steamGridDbId))
            {
                steamGridDbId = TryResolveSteamGridDbIdByName(folder.Name, cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(steamGridDbId))
            {
                folder.SteamGridDbId = steamGridDbId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamGridDbId ?? string.Empty;
        }

        string ResolveBestLibraryFolderSteamAppId(string root, LibraryFolderInfo folder, bool allowLookup = true, CancellationToken cancellationToken = default(CancellationToken))
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
            if (string.IsNullOrWhiteSpace(appId)) appId = TryResolveSteamAppId(folder.Name, cancellationToken);
            if (!string.IsNullOrWhiteSpace(appId))
            {
                folder.SteamAppId = appId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamAppId ?? string.Empty;
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
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder);
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

        string ForceRefreshLibraryArt(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
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
                    File.Copy(existingCached, backupPath, true);
                }

                var steamDownloaded = TryDownloadSteamCover(folder, cancellationToken);
                if (!string.IsNullOrWhiteSpace(steamDownloaded) && File.Exists(steamDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamDownloaded;
                }

                DeleteCachedCover(folder.Name);
                var steamGridDbDownloaded = TryDownloadSteamGridDbCover(folder, cancellationToken);
                if (!string.IsNullOrWhiteSpace(steamGridDbDownloaded) && File.Exists(steamGridDbDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamGridDbDownloaded;
                }

                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !string.IsNullOrWhiteSpace(existingCached))
                {
                    if (File.Exists(existingCached)) File.Delete(existingCached);
                    File.Move(backupPath, existingCached);
                    ClearImageCache();
                    return existingCached;
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !string.IsNullOrWhiteSpace(existingCached))
                {
                    try
                    {
                        if (File.Exists(existingCached)) File.Delete(existingCached);
                        File.Move(backupPath, existingCached);
                        ClearImageCache();
                        return existingCached;
                    }
                    catch { }
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    try { File.Delete(backupPath); } catch { }
                }
            }

            return CachedCoverPath(folder.Name);
        }

        void RefreshLibraryCovers(string root, List<LibraryFolderInfo> libraryFolders, List<LibraryFolderInfo> requestedFolders, Action<int, int, string> progress, CancellationToken cancellationToken, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh, out int resolvedIds, out int coversReady)
        {
            resolvedIds = 0;
            coversReady = 0;
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
                return;
            }
            var completed = 0;
            for (int i = 0; i < targetFolders.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = targetFolders[i];
                var itemLabel = "Game " + (i + 1) + " of " + targetFolders.Count + " | " + folder.Name;
                var hadAppId = !string.IsNullOrWhiteSpace(folder.SteamAppId);
                var hadSteamGridDbId = !string.IsNullOrWhiteSpace(folder.SteamGridDbId);
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder, true, cancellationToken);
                var steamGridDbId = ResolveBestLibraryFolderSteamGridDbId(root, folder, cancellationToken);
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
                    var refreshedCover = ForceRefreshLibraryArt(folder, cancellationToken);
                    coverReady = !string.IsNullOrWhiteSpace(refreshedCover) && File.Exists(refreshedCover);
                    coverDetail = coverReady ? "cover refreshed" : "cover refresh not available";
                }
                else if (forceRefreshExistingCovers && hasCustomCover)
                {
                    coverDetail = "custom cover preserved";
                }
                else if (!coverReady)
                {
                    ResolveLibraryArt(folder, true, cancellationToken);
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
                return;
            }
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null || cached.Count == 0)
            {
                SaveLibraryFolderCache(root, stamp, allFolders);
                return;
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
        }

        string ResolveLibraryArt(LibraryFolderInfo folder, bool allowDownload, CancellationToken cancellationToken = default(CancellationToken))
        {
            var custom = CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            var cached = CachedCoverPath(folder.Name);
            if (cached != null) return cached;
            if (allowDownload)
            {
                var downloaded = TryDownloadSteamCover(folder, cancellationToken);
                if (downloaded != null) return downloaded;
                var steamGridDbDownloaded = TryDownloadSteamGridDbCover(folder, cancellationToken);
                if (steamGridDbDownloaded != null) return steamGridDbDownloaded;
            }
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

        string TryDownloadSteamCover(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            try
            {
                var appId = ResolveBestLibraryFolderSteamAppId(libraryRoot, folder, true, cancellationToken);
                var downloaded = coverService.TryDownloadSteamCover(folder.Name, appId, cancellationToken);
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
            catch { }
            return null;
        }

        string TryDownloadSteamGridDbCover(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || !HasSteamGridDbApiToken()) return null;
            try
            {
                var steamGridDbId = ResolveBestLibraryFolderSteamGridDbId(libraryRoot, folder, cancellationToken);
                var downloaded = coverService.TryDownloadSteamGridDbCover(folder.Name, steamGridDbId, cancellationToken);
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

        string TryResolveSteamAppId(string title, CancellationToken cancellationToken = default(CancellationToken))
        {
            return coverService.TryResolveSteamAppId(title, cancellationToken);
        }

        List<Tuple<string, string>> SearchSteamAppMatches(string title, CancellationToken cancellationToken = default(CancellationToken))
        {
            return coverService.SearchSteamAppMatches(title, cancellationToken) ?? new List<Tuple<string, string>>();
        }

        BitmapImage LoadImageSource(string path, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var sourcePath = path;
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                if (IsVideo(path))
                {
                    var poster = EnsureVideoPoster(path, normalizedDecodePixelWidth);
                    if (!string.IsNullOrWhiteSpace(poster) && File.Exists(poster)) path = poster;
                }
                var info = new FileInfo(path);
                var cacheKey = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + normalizedDecodePixelWidth;
                var cached = TryGetCachedImage(cacheKey);
                if (cached != null) return cached;

                BitmapImage image = null;
                var thumbnailPath = IsVideo(sourcePath) ? null : ThumbnailCachePath(path, normalizedDecodePixelWidth);
                if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    image = LoadFrozenBitmap(thumbnailPath, 0);
                }
                if (image == null)
                {
                    image = LoadFrozenBitmap(path, normalizedDecodePixelWidth);
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

        string SteamName(string appId, CancellationToken cancellationToken = default(CancellationToken))
        {
            return coverService.SteamName(appId, cancellationToken);
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
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            try
            {
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                if (IsVideo(sourcePath))
                {
                    var posterPath = ExistingVideoPosterPath(sourcePath, normalizedDecodePixelWidth);
                    return string.IsNullOrWhiteSpace(posterPath) ? null : LoadFrozenBitmap(posterPath, 0);
                }
                var thumbnailPath = ThumbnailCachePath(sourcePath, normalizedDecodePixelWidth);
                return string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath)
                    ? null
                    : LoadFrozenBitmap(thumbnailPath, 0);
            }
            catch
            {
                return null;
            }
        }

        BitmapImage LoadFrozenBitmap(string path, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
            image.UriSource = new UriBuilder { Scheme = Uri.UriSchemeFile, Host = string.Empty, Path = fullPath }.Uri;
            image.EndInit();
            image.Freeze();
            return image;
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
            catch
            {
                try
                {
                    if (File.Exists(destinationPath + ".tmp")) File.Delete(destinationPath + ".tmp");
                }
                catch { }
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
            catch { }
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
            File.Copy(sourcePath, destinationPath, true);
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
                File.Copy(sourceFile, destinationFile, true);
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
                File.Copy(sourceFile, destinationFile, false);
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
            catch
            {
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
            catch { }
        }

        void OpenSavedCoversFolder()
        {
            try
            {
                Directory.CreateDirectory(savedCoversRoot);
                EnsureSavedCoversReadme();
            }
            catch { }
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
                        LoadRows = LoadPhotoIndexEditorRows,
                        SaveRows = SavePhotoIndexEditorRows,
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

            try
            {
                GameIndexEditorHost.Show(
                    this,
                    AppVersion,
                    libraryRoot,
                    w => gameIndexEditorWindow = w,
                    w => { if (ReferenceEquals(gameIndexEditorWindow, w)) gameIndexEditorWindow = null; },
                    new GameIndexEditorServices
                    {
                        SetStatus = delegate(string text) { if (status != null) status.Text = text; },
                        Log = Log,
                        CreateButton = Btn,
                        LoadRows = LoadGameIndexEditorRows,
                        SaveRows = SaveGameIndexEditorRows,
                        CreateGameId = CreateGameId,
                        NormalizeGameIndexName = NormalizeGameIndexName,
                        NormalizeConsoleLabel = NormalizeConsoleLabel,
                        MergeRows = delegate(List<GameIndexEditorRow> rows) { return MergeGameIndexRows(rows); },
                        BuildMergeKey = BuildGameIndexMergeKey,
                        RunBackgroundWorkflowIntArray = RunBackgroundWorkflowWithProgress<int[]>,
                        ThrowIfWorkflowCancellationRequested = ThrowIfWorkflowCancellationRequested,
                        ResolveMissingSteamAppIds = ResolveMissingGameIndexSteamAppIds,
                        ResolveMissingSteamGridDbIds = ResolveMissingGameIndexSteamGridDbIds,
                        OpenFolder = OpenFolder
                    });
            }
            catch (Exception ex)
            {
                status.Text = "Game index unavailable";
                Log("Failed to open game index. " + ex.Message);
                MessageBox.Show("Could not open the game index." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault");
            }
        }
        void OpenWithShell(string path) { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }

        void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            foreach (var line in File.ReadAllLines(settingsPath))
            {
                if (string.IsNullOrWhiteSpace(line) || !line.Contains("=")) continue;
                var index = line.IndexOf('=');
                var key = line.Substring(0, index);
                var value = line.Substring(index + 1);
                if (key == "source") sourceRoot = SerializeSourceRoots(value);
                else if (key == "destination") destinationRoot = value;
                else if (key == "library") libraryRoot = value;
                else if (key == "exiftool" && !string.IsNullOrWhiteSpace(value)) exifToolPath = value;
                else if (key == "ffmpeg" && !string.IsNullOrWhiteSpace(value)) ffmpegPath = value;
                else if (key == "steamgriddb_token") steamGridDbApiToken = value;
                else if (key == "library_folder_tile_size")
                {
                    int parsedSize;
                    if (int.TryParse(value, out parsedSize)) libraryFolderTileSize = NormalizeLibraryFolderTileSize(parsedSize);
                }
                else if (key == "library_folder_sort_mode") libraryFolderSortMode = NormalizeLibraryFolderSortMode(value);
            }

            var bundledExifTool = Path.Combine(appRoot, "tools", "exiftool.exe");
            if (!File.Exists(exifToolPath) && File.Exists(bundledExifTool))
            {
                exifToolPath = bundledExifTool;
            }

            var bundledFfmpeg = Path.Combine(appRoot, "tools", "ffmpeg.exe");
            if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                ffmpegPath = File.Exists(bundledFfmpeg) ? bundledFfmpeg : (FindExecutableOnPath("ffmpeg.exe") ?? string.Empty);
            }
            var envSteamGridDbToken = FindSteamGridDbApiTokenInEnvironment();
            if (!string.IsNullOrWhiteSpace(envSteamGridDbToken))
            {
                steamGridDbApiToken = envSteamGridDbToken;
            }
        }

        void SaveSettings()
        {
            File.WriteAllLines(settingsPath, new[]
            {
                "source=" + SerializeSourceRoots(sourceRoot),
                "destination=" + destinationRoot,
                "library=" + libraryRoot,
                "exiftool=" + exifToolPath,
                "ffmpeg=" + (ffmpegPath ?? string.Empty),
                "steamgriddb_token=" + (steamGridDbApiToken ?? string.Empty),
                "library_folder_tile_size=" + NormalizeLibraryFolderTileSize(libraryFolderTileSize),
                "library_folder_sort_mode=" + NormalizeLibraryFolderSortMode(libraryFolderSortMode)
            });
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



















































































































