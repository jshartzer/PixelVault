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
        const string AppVersion = "0.781";
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
        readonly string settingsPath;
        readonly string changelogPath;
        readonly string undoManifestPath;
        readonly Dictionary<string, BitmapImage> imageCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<string> imageCacheOrder = new LinkedList<string>();
        readonly Dictionary<string, LinkedListNode<string>> imageCacheOrderNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        readonly object imageCacheSync = new object();
        readonly SemaphoreSlim imageLoadLimiter = new SemaphoreSlim(Math.Max(2, Math.Min(Environment.ProcessorCount, 6)));
        readonly SemaphoreSlim priorityImageLoadLimiter = new SemaphoreSlim(Math.Max(1, Math.Min(Environment.ProcessorCount, 3)));
        readonly HashSet<string> failedFfmpegPosterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, byte> activeVideoPreviewGenerations = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, byte> activeVideoInfoGenerations = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<string>> folderImageCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, long> folderImageCacheStamp = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string[]> fileTagCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, long> fileTagCacheStamp = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        readonly Dictionary<string, LibraryMetadataIndexEntry> libraryMetadataIndex = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
        string libraryMetadataIndexRoot;

        string sourceRoot;
        string destinationRoot;
        string libraryRoot;
        string exifToolPath;
        string ffmpegPath;
        string steamGridDbApiToken;
        int libraryFolderTileSize = 240;
        string libraryFolderSortMode = "platform";

        RichTextBox previewBox;
        TextBox logBox;
        TextBlock status;
        CheckBox recurseBox, keywordsBox;
        ComboBox conflictBox;
        Window photoIndexEditorWindow;
        Window gameIndexEditorWindow;
        int previewRefreshVersion;
        readonly ICoverService coverService;
        readonly IIndexPersistenceService indexPersistenceService;
        readonly IMetadataService metadataService;

        public MainWindow()
        {
            dataRoot = ResolvePersistentDataRoot(appRoot);
            logsRoot = Path.Combine(dataRoot, "logs");
            cacheRoot = Path.Combine(dataRoot, "cache");
            coversRoot = Path.Combine(cacheRoot, "covers");
            thumbsRoot = Path.Combine(cacheRoot, "thumbs");
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
                RunExeCapture = delegate(string file, string[] args, string cwd, bool logOutput) { return RunExeCapture(file, args, cwd, logOutput); }
            });
            Directory.CreateDirectory(dataRoot);
            Directory.CreateDirectory(logsRoot);
            Directory.CreateDirectory(cacheRoot);
            Directory.CreateDirectory(coversRoot);
            Directory.CreateDirectory(thumbsRoot);
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

        UIElement BuildUi()
        {
            var root = new Grid { Margin = new Thickness(24), Background = Brushes.White };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(20), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 16) };
            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition());
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hs = new StackPanel();
            hs.Children.Add(new TextBlock { Text = "PixelVault Settings", FontSize = 31, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            hs.Children.Add(new TextBlock { Text = "Configure paths, run intake tools, and manage the library without putting the browser itself in the way.", Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            status = new TextBlock { Text = "Ready", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            var headerRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var pathSettingsTopButton = Btn("Path Settings", delegate { ShowPathSettingsWindow(); }, "#2B3F47", Brushes.White);
            pathSettingsTopButton.Margin = new Thickness(0, 0, 12, 0);
            var viewLogsTopButton = Btn("View Logs", delegate { OpenFolder(logsRoot); }, "#2B3F47", Brushes.White);
            viewLogsTopButton.Margin = new Thickness(0, 0, 12, 0);
            var gameIndexTopButton = Btn("Game Index", delegate { OpenGameIndexEditor(); }, "#20343A", Brushes.White);
            gameIndexTopButton.Margin = new Thickness(0, 0, 12, 0);
            var photoIndexTopButton = Btn("Photo Index", delegate { OpenPhotoIndexEditor(); }, "#20343A", Brushes.White);
            photoIndexTopButton.Margin = new Thickness(0, 0, 12, 0);
            var changelogTopButton = Btn("Changelog", delegate { ShowChangelogWindow(); }, "#20343A", Brushes.White);
            changelogTopButton.Margin = new Thickness(0, 0, 12, 0);
            var sp = new Border { Child = status, Background = Brush("#20343A"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10) };
            headerRight.Children.Add(pathSettingsTopButton);
            headerRight.Children.Add(viewLogsTopButton);
            headerRight.Children.Add(gameIndexTopButton);
            headerRight.Children.Add(photoIndexTopButton);
            headerRight.Children.Add(changelogTopButton);
            headerRight.Children.Add(sp);
            hg.Children.Add(hs);
            Grid.SetColumn(headerRight, 1);
            hg.Children.Add(headerRight);
            header.Child = hg;
            root.Children.Add(header);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.62, GridUnitType.Star) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);

            var left = Card();
            left.Margin = new Thickness(0, 0, 16, 0);
            var leftGrid = new Grid();
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.Child = leftGrid;

            var leftStack = new StackPanel();
            leftStack.Children.Add(TitleBlock("Control Center"));
            leftStack.Children.Add(new TextBlock { Text = "Use Path Settings for locations and tools, then run imports or maintenance from here whenever you need them.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap });
            leftStack.Children.Add(BuildSettingsSummary());

            leftStack.Children.Add(new TextBlock { Text = "Import options", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 16, 0, 8) });
            recurseBox = new CheckBox { Content = "Search subfolders for rename", Margin = new Thickness(0, 0, 0, 8) };
            keywordsBox = new CheckBox { Content = "Add Game Capture keywords", Margin = new Thickness(0, 0, 0, 8) };
            leftStack.Children.Add(recurseBox);
            leftStack.Children.Add(keywordsBox);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(new TextBlock { Text = "Move conflicts", Width = 110, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#4C463F") });
            conflictBox = new ComboBox { Width = 140 };
            conflictBox.Items.Add("Rename");
            conflictBox.Items.Add("Overwrite");
            conflictBox.Items.Add("Skip");
            row.Children.Add(conflictBox);
            leftStack.Children.Add(row);

            leftStack.Children.Add(new TextBlock { Text = "Import actions", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            leftStack.Children.Add(new TextBlock { Text = "Choose the fastest path for intake, or open manual review only when you need it.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var processRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), ItemHeight = 48 };
            processRow.Children.Add(Btn("Process", delegate { RunWorkflow(false); }, "#275D47", Brushes.White));
            processRow.Children.Add(Btn("Process with Comments", delegate { RunWorkflow(true); }, "#2F7A59", Brushes.White));
            processRow.Children.Add(Btn("Manual Intake", delegate { OpenManualIntakeWindow(); }, null, Brushes.Black));
            leftStack.Children.Add(processRow);

            leftStack.Children.Add(new TextBlock { Text = "Library maintenance", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            leftStack.Children.Add(new TextBlock { Text = "Organize the destination, reverse the most recent import, and manage supporting library data from the top buttons.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var libraryRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), ItemHeight = 48 };
            libraryRow.Children.Add(Btn("Sort Destination", delegate { SortDestinationFolders(); }, "#E9EEF3", Brush("#33424D")));
            libraryRow.Children.Add(Btn("Undo Last Import", delegate { UndoLastImport(); }, "#FFF1E2", Brush("#7A4B12")));
            leftStack.Children.Add(libraryRow);

            leftStack.Children.Add(new TextBlock { Text = "Utility actions", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            leftStack.Children.Add(new TextBlock { Text = "Preview the next run and jump directly to the intake or destination folders when you need them.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var openRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), ItemHeight = 48 };
            openRow.Children.Add(Btn("Preview Intake", delegate { ShowIntakePreviewWindow(recurseBox != null && recurseBox.IsChecked == true); }, "#DCEEFF", Brush("#174A73")));
            openRow.Children.Add(Btn("Open Sources", delegate { OpenSourceFolders(); }, "#EEF2F5", Brush("#33424D")));
            openRow.Children.Add(Btn("Open Destination", delegate { OpenFolder(destinationRoot); }, "#EEF2F5", Brush("#33424D")));
            leftStack.Children.Add(openRow);
            leftGrid.Children.Add(leftStack);

            previewBox = new RichTextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 4, 0, 0),
                MinHeight = 320,
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                FontFamily = new FontFamily("Cascadia Mono"),
                IsDocumentEnabled = false
            };
            Grid.SetRow(previewBox, 1);
            leftGrid.Children.Add(previewBox);
            main.Children.Add(left);

            var right = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(14) };
            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.Children.Add(new TextBlock { Text = "Run history", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F1E9DA"), Margin = new Thickness(0, 0, 0, 8) });
            logBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Background = Brush("#12191E"),
                Foreground = Brush("#F1E9DA"),
                FontFamily = new FontFamily("Cascadia Mono")
            };
            Grid.SetRow(logBox, 1);
            rightGrid.Children.Add(logBox);
            right.Child = rightGrid;
            Grid.SetColumn(right, 1);
            main.Children.Add(right);

            return root;
        }
        Border Card() { return new Border { Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Effect = new DropShadowEffect { Color = Color.FromArgb(20, 17, 27, 35), BlurRadius = 18, ShadowDepth = 2, Direction = 270, Opacity = 0.4 } }; }
        TextBlock TitleBlock(string t) { return new TextBlock { Text = t, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14), Foreground = Brush("#1F2A30") }; }
        SolidColorBrush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
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
        string IntakeBadgeCountText(int count)
        {
            if (count <= 0) return string.Empty;
            if (count > 99) return "99+";
            return count.ToString();
        }
        Border BuildIntakeMetricCard(string label, string value, string detail, string backgroundHex, string borderHex, string valueHex)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#A7B5BD"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = value, Foreground = Brush(valueHex), FontSize = 28, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(detail))
            {
                stack.Children.Add(new TextBlock { Text = detail, Foreground = Brush("#C9D4DB"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            }
            return new Border
            {
                Background = Brush(backgroundHex),
                BorderBrush = Brush(borderHex),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 14, 0),
                Child = stack
            };
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
            var available = Math.Max(980, SystemParameters.WorkArea.Height - 24);
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
            return count == 1 ? "folder" : "folders";
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
            var files = (folder.FilePaths ?? new string[0]).Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).ToArray();
            DateTime newest = DateTime.MinValue;
            if (files.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(folder.PreviewImagePath) && File.Exists(folder.PreviewImagePath)) newest = GetLibraryDate(folder.PreviewImagePath);
            }
            else
            {
                newest = files.Select(GetLibraryDate).DefaultIfEmpty(DateTime.MinValue).Max();
            }
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
        FrameworkElement BuildLibrarySectionHeader(string platformLabel, int folderCount)
        {
            var resolvedLabel = NormalizeConsoleLabel(platformLabel);
            var accent = LibrarySectionAccentBrush(resolvedLabel);
            var iconPath = ResolveLibrarySectionIconPath(resolvedLabel);

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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
            Grid.SetColumn(titleStack, 1);
            headerGrid.Children.Add(titleStack);

            var countStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 74
            };
            countStack.Children.Add(new TextBlock
            {
                Text = folderCount.ToString(),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right
            });
            countStack.Children.Add(new TextBlock
            {
                Text = LibrarySectionCountLabel(folderCount),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brush("#9AAAB4"),
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, -2, 0, 0)
            });
            Grid.SetColumn(countStack, 2);
            headerGrid.Children.Add(countStack);

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

        Border BuildSettingsSummary()
        {
            var border = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Current paths", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Sources: " + SourceRootsSummary(), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Destination: " + destinationRoot, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Library: " + libraryRoot, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "ExifTool: " + exifToolPath, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "FFmpeg: " + (string.IsNullOrWhiteSpace(ffmpegPath) ? "(not configured)" : ffmpegPath), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "SteamGridDB: " + (HasSteamGridDbApiToken() ? "token configured" : "(token not configured)"), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F") });
            border.Child = stack;
            return border;
        }

        void RefreshMainUi()
        {
            ShowLibraryBrowser(true);
        }

        void ShowPathSettingsWindow()
        {
            var window = new Window
            {
                Title = "PixelVault " + AppVersion + " Path Settings",
                Width = 760,
                Height = 620,
                MinWidth = 680,
                MinHeight = 700,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#F3EEE4")
            };

            var root = new Grid { Margin = new Thickness(24), Background = Brushes.White };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sourceBox = SettingsTextBox(panel, 0, "Source folders", SourceRootsEditorText());
            sourceBox.AcceptsReturn = true;
            sourceBox.TextWrapping = TextWrapping.Wrap;
            sourceBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sourceBox.Height = 96;
            var destinationBox = SettingsTextBox(panel, 1, "Destination folder", destinationRoot);
            var libraryBox = SettingsTextBox(panel, 2, "Library folder", libraryRoot);
            var exifBox = SettingsTextBox(panel, 3, "ExifTool path", exifToolPath);
            var ffmpegBox = SettingsTextBox(panel, 4, "FFmpeg path", ffmpegPath);
            var steamGridDbTokenBox = SettingsTextBox(panel, 5, "SteamGridDB token", steamGridDbApiToken);
            steamGridDbTokenBox.ToolTip = "Stored locally in PixelVault.settings.ini. Environment variables can also override it.";

            SettingsBrowseButton(panel, 0, delegate { var picked = PickFolder(PrimarySourceRoot()); if (!string.IsNullOrWhiteSpace(picked)) sourceBox.Text = AppendSourceRoot(sourceBox.Text, picked); }, "Add Folder");
            SettingsBrowseButton(panel, 1, delegate { var picked = PickFolder(destinationBox.Text); if (!string.IsNullOrWhiteSpace(picked)) destinationBox.Text = picked; });
            SettingsBrowseButton(panel, 2, delegate { var picked = PickFolder(libraryBox.Text); if (!string.IsNullOrWhiteSpace(picked)) libraryBox.Text = picked; });
            SettingsBrowseButton(panel, 3, delegate { var picked = PickFile(exifBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*"); if (!string.IsNullOrWhiteSpace(picked)) exifBox.Text = picked; });
            SettingsBrowseButton(panel, 4, delegate { var picked = PickFile(ffmpegBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*"); if (!string.IsNullOrWhiteSpace(picked)) ffmpegBox.Text = picked; });

            root.Children.Add(panel);
            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            var save = Btn("Save Settings", null, "#275D47", Brushes.White);
            var cancel = Btn("Cancel", null, null, Brushes.Black);
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            window.Content = root;

            save.Click += delegate
            {
                sourceRoot = SerializeSourceRoots(sourceBox.Text);
                destinationRoot = destinationBox.Text;
                libraryRoot = libraryBox.Text;
                exifToolPath = exifBox.Text;
                ffmpegPath = ffmpegBox.Text;
                failedFfmpegPosterKeys.Clear();
                steamGridDbApiToken = (steamGridDbTokenBox.Text ?? string.Empty).Trim();
                SaveSettings();
                RefreshMainUi();
                window.Close();
                Log("Settings saved.");
            };
            cancel.Click += delegate { window.Close(); };
            window.ShowDialog();
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

        void ShowChangelogWindow()
        {
            var window = new Window
            {
                Title = "PixelVault " + AppVersion + " Changelog",
                Width = 780,
                Height = 700,
                MinWidth = 680,
                MinHeight = 520,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#F5F8FC")
            };

            var root = new Grid { Margin = new Thickness(24), Background = Brushes.White };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            header.Children.Add(new TextBlock { Text = "PixelVault changelog", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            header.Children.Add(new TextBlock { Text = "Recent release notes, fixes, and workflow updates.", Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#5F6970") });
            root.Children.Add(header);

            var viewer = new RichTextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                FontFamily = new FontFamily("Segoe UI")
            };
            var doc = new FlowDocument { PagePadding = new Thickness(18), Background = Brushes.White };
            var lines = File.Exists(changelogPath) ? File.ReadAllLines(changelogPath) : new[] { "# PixelVault Changelog", "", "No changelog entries yet." };
            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                if (line.StartsWith("## "))
                {
                    doc.Blocks.Add(new Paragraph(new Run(line.Substring(3))) { Margin = new Thickness(0, 14, 0, 6), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
                }
                else if (line.StartsWith("# "))
                {
                    doc.Blocks.Add(new Paragraph(new Run(line.Substring(2))) { Margin = new Thickness(0, 0, 0, 10), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brush("#1F2A30") });
                }
                else if (line.StartsWith("- "))
                {
                    doc.Blocks.Add(new Paragraph(new Run("• " + line.Substring(2))) { Margin = new Thickness(0, 0, 0, 8), FontSize = 14, Foreground = Brush("#3B4650") });
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    doc.Blocks.Add(new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0, 2, 0, 2) });
                }
                else
                {
                    doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 8), FontSize = 14, Foreground = Brush("#3B4650") });
                }
            }
            viewer.Document = doc;
            Grid.SetRow(viewer, 1);
            root.Children.Add(viewer);

            var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            var close = Btn("Close", null, "#20343A", Brushes.White);
            close.Click += delegate { window.Close(); };
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            window.Content = root;
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

        List<string> BuildImportSummaryLines(string workflowLabel, bool usedReview, RenameStepResult renameResult, DeleteStepResult deleteResult, MetadataStepResult metadataResult, MoveStepResult moveResult, SortStepResult sortResult, int manualItemsLeft)
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
            if (manualItemsLeft > 0)
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

        string PickFile(string current, string filter)
        {
            using (var dialog = new Forms.OpenFileDialog())
            {
                dialog.Filter = filter;
                if (File.Exists(current)) dialog.FileName = current;
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
            var normalizedPaths = new HashSet<string>(
                (folderPaths ?? Enumerable.Empty<string>())
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
            foreach (var folderPath in normalizedPaths)
            {
                folderImageCache.Remove(folderPath);
                folderImageCacheStamp.Remove(folderPath);
            }
        }

        void RemoveCachedFileTagEntries(IEnumerable<string> files)
        {
            var normalizedPaths = new HashSet<string>(
                (files ?? Enumerable.Empty<string>())
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
            foreach (var file in normalizedPaths)
            {
                fileTagCache.Remove(file);
                fileTagCacheStamp.Remove(file);
            }
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
                if (onLoaded != null) onLoaded(immediate);
                else
                {
                    imageControl.Source = immediate;
                    imageControl.Visibility = Visibility.Visible;
                }
                hadSource = true;
            }
            if (!hadSource)
            {
                imageControl.Visibility = Visibility.Collapsed;
            }
            Task.Run(delegate
            {
                if (shouldLoad != null && !shouldLoad()) return null;
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
        void LoadIntakePreviewSummaryAsync(bool recurseRename, Action<IntakePreviewSummary> onSuccess, Action<Exception> onError)
        {
            Task.Factory.StartNew(delegate
            {
                var stopwatch = Stopwatch.StartNew();
                var summary = BuildIntakePreviewSummary(recurseRename);
                stopwatch.Stop();
                LogPerformanceSample("IntakePreviewBuild", stopwatch, "recurseRename=" + recurseRename + "; topLevel=" + summary.TopLevelMediaCount + "; reviewItems=" + summary.MetadataCandidateCount + "; manualItems=" + summary.ManualItemCount + "; conflicts=" + summary.ConflictCount, 40);
                return summary;
            }).ContinueWith(delegate(Task<IntakePreviewSummary> summaryTask)
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
        IntakePreviewSummary BuildIntakePreviewSummary(bool recurseRename)
        {
            EnsureSourceFolders();
            var inventory = BuildSourceInventory(recurseRename);
            var rename = inventory.RenameScopeFiles;
            var move = inventory.TopLevelMediaFiles;
            var reviewItems = BuildReviewItems(move);
            var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
            var manualItems = BuildManualMetadataItems(move, recognizedPaths);
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
            status.Text = "Refreshing preview";
            RenderPreviewLoading("Refreshing upload preview...");
            LoadIntakePreviewSummaryAsync(recurseRename, delegate(IntakePreviewSummary summary)
            {
                if (refreshVersion != previewRefreshVersion) return;
                RenderPreview(summary);
                status.Text = "Preview ready";
                LogPreviewSummary(summary);
            }, delegate(Exception ex)
            {
                if (refreshVersion != previewRefreshVersion) return;
                RenderPreviewError(ex.Message);
                status.Text = "Preview failed";
                Log(ex.Message);
            });
        }

        void ShowIntakePreviewWindow(bool recurseRename)
        {
            Window previewWindow = null;
            TextBlock headerMeta = null;
            Grid statsGrid = null;
            StackPanel autoReadyPanel = null;
            StackPanel sidePanel = null;
            Button manualButton = null;
            Button refreshButton = null;
            Action renderWindow = null;
            int previewWindowRefreshVersion = 0;
            try
            {
                previewWindow = new Window
                {
                    Title = "PixelVault " + AppVersion + " Intake Preview",
                    Width = 1400,
                    Height = 920,
                    MinWidth = 1160,
                    MinHeight = 760,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#081015")
                };

                var root = new Grid { Margin = new Thickness(20) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border
                {
                    Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#16222A"), (Color)ColorConverter.ConvertFromString("#0D161D"), new Point(0, 0), new Point(1, 1)),
                    BorderBrush = Brush("#2D3E48"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(24),
                    Padding = new Thickness(24),
                    Effect = new DropShadowEffect { Color = Color.FromArgb(48, 5, 10, 14), BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.55 }
                };
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var iconShell = new Border
                {
                    Width = 74,
                    Height = 74,
                    Background = Brush("#0E171C"),
                    BorderBrush = Brush("#344851"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(22),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 18, 0),
                    Child = BuildGamepadGlyph(Brush("#F5F7FA"), 2.2, 42, 28)
                };
                headerGrid.Children.Add(iconShell);
                var headerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                headerStack.Children.Add(new TextBlock { Text = "Upload queue preview", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4") });
                headerMeta = new TextBlock { Foreground = Brush("#AAB9C2"), FontSize = 14, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
                headerStack.Children.Add(headerMeta);
                Grid.SetColumn(headerStack, 1);
                headerGrid.Children.Add(headerStack);
                var actionRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var openSourcesButton = Btn("Open Uploads", null, "#20343A", Brushes.White);
                openSourcesButton.Width = 152;
                openSourcesButton.Height = 42;
                openSourcesButton.Margin = new Thickness(12, 0, 0, 0);
                openSourcesButton.Click += delegate { OpenSourceFolders(); };
                refreshButton = Btn("Refresh", null, "#275D47", Brushes.White);
                refreshButton.Width = 128;
                refreshButton.Height = 42;
                refreshButton.Margin = new Thickness(12, 0, 0, 0);
                manualButton = Btn("Manual Intake", null, "#7C5A34", Brushes.White);
                manualButton.Width = 154;
                manualButton.Height = 42;
                manualButton.Margin = new Thickness(12, 0, 0, 0);
                actionRow.Children.Add(openSourcesButton);
                actionRow.Children.Add(refreshButton);
                actionRow.Children.Add(manualButton);
                Grid.SetColumn(actionRow, 2);
                headerGrid.Children.Add(actionRow);
                header.Child = headerGrid;
                root.Children.Add(header);

                statsGrid = new Grid { Margin = new Thickness(0, 16, 0, 16) };
                for (int i = 0; i < 4; i++) statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(statsGrid, 1);
                root.Children.Add(statsGrid);

                var body = new Grid();
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(body, 2);
                root.Children.Add(body);

                var autoReadyCard = new Border { Background = Brush("#10181D"), BorderBrush = Brush("#24333D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 16, 0) };
                var autoReadyGrid = new Grid();
                autoReadyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                autoReadyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                autoReadyGrid.Children.Add(new TextBlock { Text = "Ready by console", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4"), Margin = new Thickness(0, 0, 0, 12) });
                var autoReadyScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                autoReadyPanel = new StackPanel();
                autoReadyScroll.Content = autoReadyPanel;
                Grid.SetRow(autoReadyScroll, 1);
                autoReadyGrid.Children.Add(autoReadyScroll);
                autoReadyCard.Child = autoReadyGrid;
                body.Children.Add(autoReadyCard);

                var sideScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Grid.SetColumn(sideScroll, 1);
                sidePanel = new StackPanel();
                sideScroll.Content = sidePanel;
                body.Children.Add(sideScroll);
                previewWindow.Content = root;

                renderWindow = delegate
                {
                    var refreshVersion = ++previewWindowRefreshVersion;
                    headerMeta.Text = "Refreshing the upload queue snapshot...";
                    statsGrid.Children.Clear();
                    for (int i = 0; i < 4; i++)
                    {
                        var loadingCard = BuildIntakeMetricCard(i == 0 ? "Queue" : (i == 1 ? "Auto-ready" : (i == 2 ? "Manual" : "Conflicts")), "...", "Refreshing preview data.", "#111B21", "#263842", "#F5EFE4");
                        if (i == 3) loadingCard.Margin = new Thickness(0);
                        Grid.SetColumn(loadingCard, i);
                        statsGrid.Children.Add(loadingCard);
                    }
                    autoReadyPanel.Children.Clear();
                    autoReadyPanel.Children.Add(new Border
                    {
                        Background = Brush("#121E24"),
                        BorderBrush = Brush("#243741"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(18),
                        Padding = new Thickness(20),
                        Child = new TextBlock { Text = "Refreshing queue snapshot...", Foreground = Brush("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                    });
                    sidePanel.Children.Clear();
                    sidePanel.Children.Add(new Border
                    {
                        Background = Brush("#11181D"),
                        BorderBrush = Brush("#243742"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(22),
                        Padding = new Thickness(18),
                        Child = new TextBlock { Text = "Gathering current upload folders, auto-ready items, and manual-intake candidates...", Foreground = Brush("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                    });
                    if (refreshButton != null) refreshButton.IsEnabled = false;
                    if (manualButton != null) manualButton.IsEnabled = false;
                    status.Text = "Refreshing preview";

                    LoadIntakePreviewSummaryAsync(recurseRename, delegate(IntakePreviewSummary summary)
                    {
                        if (previewWindow == null || !previewWindow.IsVisible) return;
                        if (refreshVersion != previewWindowRefreshVersion) return;
                        RenderPreview(summary);
                        status.Text = "Preview ready";
                        LogPreviewSummary(summary);

                        headerMeta.Text = summary.TopLevelMediaCount == 0
                            ? "No media files are waiting in the upload queue right now."
                            : summary.TopLevelMediaCount + " item(s) are waiting across " + summary.SourceRoots.Count + " upload folder(s). Automatic matches stay grouped by console, and anything unmatched stays in Manual Intake.";

                        statsGrid.Children.Clear();
                        var statCards = new[]
                        {
                            BuildIntakeMetricCard("Queue", summary.TopLevelMediaCount.ToString(), "Top-level media items currently waiting.", "#111B21", "#263842", "#F5EFE4"),
                            BuildIntakeMetricCard("Auto-ready", summary.MoveCandidateCount.ToString(), "Files that can move straight through metadata and import.", "#101923", "#244153", "#7DD3FC"),
                            BuildIntakeMetricCard("Manual", summary.ManualItemCount.ToString(), "Items that still need console or game context.", "#201912", "#5A3E24", "#F6C47A"),
                            BuildIntakeMetricCard("Conflicts", summary.ConflictCount.ToString(), "Destination filename collisions if moved right now.", "#1B1518", "#4A2A34", "#F88CA2")
                        };
                        for (int i = 0; i < statCards.Length; i++)
                        {
                            if (i == statCards.Length - 1) statCards[i].Margin = new Thickness(0);
                            Grid.SetColumn(statCards[i], i);
                            statsGrid.Children.Add(statCards[i]);
                        }

                        autoReadyPanel.Children.Clear();
                        if (summary.ReviewItems.Count == 0)
                        {
                            autoReadyPanel.Children.Add(new Border
                            {
                                Background = Brush("#121E24"),
                                BorderBrush = Brush("#243741"),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(18),
                                Padding = new Thickness(20),
                                Child = new TextBlock { Text = "No auto-ready captures are waiting. New uploads will show up here grouped by console.", Foreground = Brush("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                            });
                        }
                        else
                        {
                            var groupedReviewItems = summary.ReviewItems.GroupBy(item => item.PlatformLabel).OrderBy(group => PlatformGroupOrder(group.Key)).ThenBy(group => group.Key);
                            foreach (var group in groupedReviewItems)
                            {
                                var accent = PreviewBadgeBrush(group.Key);
                                var section = new Border { Background = Brush("#121B21"), BorderBrush = accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14) };
                                var sectionStack = new StackPanel();
                                var sectionHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                                sectionHeader.Children.Add(new TextBlock { Text = group.Key, Foreground = Brush("#F5EFE4"), FontSize = 17, FontWeight = FontWeights.SemiBold });
                                var pill = new Border
                                {
                                    Background = accent,
                                    CornerRadius = new CornerRadius(999),
                                    Padding = new Thickness(10, 4, 10, 4),
                                    HorizontalAlignment = HorizontalAlignment.Right,
                                    Child = new TextBlock { Text = group.Count() + " ready", Foreground = Brush("#081015"), FontWeight = FontWeights.SemiBold, FontSize = 11 }
                                };
                                DockPanel.SetDock(pill, Dock.Right);
                                sectionHeader.Children.Add(pill);
                                sectionStack.Children.Add(sectionHeader);
                                foreach (var item in group)
                                {
                                    var row = new Border { Background = Brush("#0D1419"), BorderBrush = Brush("#1C2B34"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8) };
                                    var rowGrid = new Grid();
                                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                                    var rowText = new StackPanel();
                                    rowText.Children.Add(new TextBlock { Text = item.FileName, Foreground = Brush("#F4F7FA"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                                    rowText.Children.Add(new TextBlock { Text = "Captured " + FormatFriendlyTimestamp(item.CaptureTime), Foreground = Brush("#8FA1AD"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
                                    rowGrid.Children.Add(rowText);
                                    var typePill = new Border
                                    {
                                        Background = item.PreserveFileTimes ? Brush("#1C2E39") : Brush("#173127"),
                                        BorderBrush = item.PreserveFileTimes ? Brush("#3E6C88") : Brush("#3B7F5C"),
                                        BorderThickness = new Thickness(1),
                                        CornerRadius = new CornerRadius(999),
                                        Padding = new Thickness(10, 5, 10, 5),
                                        Margin = new Thickness(12, 0, 0, 0),
                                        Child = new TextBlock { Text = item.PreserveFileTimes ? "Preserve file time" : "Auto metadata", Foreground = Brush("#E6EEF5"), FontSize = 11, FontWeight = FontWeights.SemiBold }
                                    };
                                    Grid.SetColumn(typePill, 1);
                                    rowGrid.Children.Add(typePill);
                                    row.Child = rowGrid;
                                    sectionStack.Children.Add(row);
                                }
                                section.Child = sectionStack;
                                autoReadyPanel.Children.Add(section);
                            }
                        }

                        sidePanel.Children.Clear();
                        var manualCard = new Border { Background = Brush("#1A1511"), BorderBrush = Brush("#5F4527"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14) };
                        var manualStack = new StackPanel();
                        manualStack.Children.Add(new TextBlock { Text = "Manual Intake", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F8E7CF") });
                        manualStack.Children.Add(new TextBlock { Text = summary.ManualItemCount == 0 ? "Nothing is blocked in manual review right now." : summary.ManualItemCount + " item(s) need a game or platform decision before import.", Foreground = Brush("#D8C2A0"), Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap });
                        if (summary.ManualItems.Count == 0)
                        {
                            manualStack.Children.Add(new Border
                            {
                                Background = Brush("#120F0C"),
                                BorderBrush = Brush("#4A3821"),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(16),
                                Padding = new Thickness(14),
                                Child = new TextBlock { Text = "Unmatched uploads will land here when PixelVault cannot confidently place them.", Foreground = Brush("#BFA98A"), TextWrapping = TextWrapping.Wrap }
                            });
                        }
                        else
                        {
                            foreach (var item in summary.ManualItems)
                            {
                                var row = new Border { Background = Brush("#120F0C"), BorderBrush = Brush("#4A3821"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8) };
                                var rowStack = new StackPanel();
                                rowStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = Brush("#FFF1DE"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                                rowStack.Children.Add(new TextBlock { Text = "Best guess: " + FilenameGuessLabel(item.FileName), Foreground = Brush("#D1B385"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
                                rowStack.Children.Add(new TextBlock { Text = "Captured " + FormatFriendlyTimestamp(item.CaptureTime), Foreground = Brush("#B59E81"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
                                row.Child = rowStack;
                                manualStack.Children.Add(row);
                            }
                        }
                        manualCard.Child = manualStack;
                        sidePanel.Children.Add(manualCard);

                        var notesCard = new Border { Background = Brush("#11181D"), BorderBrush = Brush("#243742"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14) };
                        var notesStack = new StackPanel();
                        notesStack.Children.Add(new TextBlock { Text = "Pipeline notes", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4") });
                        notesStack.Children.Add(new TextBlock { Text = "Rename scope: " + summary.RenameCandidateCount + " confident rename candidate(s) across " + summary.RenameScopeCount + " file(s) checked.", Foreground = Brush("#A7B5BD"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
                        notesStack.Children.Add(new TextBlock { Text = "Metadata-ready: " + summary.MetadataCandidateCount + " file(s) can carry tags and comments automatically.", Foreground = Brush("#A7B5BD"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
                        notesStack.Children.Add(new TextBlock
                        {
                            Text = summary.ConflictCount == 0 ? "Move conflicts: none detected in the destination library." : "Move conflicts: " + summary.ConflictCount + " filename collision(s) need the current conflict rule.",
                            Foreground = summary.ConflictCount == 0 ? Brush("#98C7A2") : Brush("#F3A9B8"),
                            Margin = new Thickness(0, 8, 0, 0),
                            TextWrapping = TextWrapping.Wrap
                        });
                        notesCard.Child = notesStack;
                        sidePanel.Children.Add(notesCard);

                        var sourcesCard = new Border { Background = Brush("#10181D"), BorderBrush = Brush("#243742"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18) };
                        var sourcesStack = new StackPanel();
                        sourcesStack.Children.Add(new TextBlock { Text = "Upload folders", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4") });
                        foreach (var rootPath in summary.SourceRoots)
                        {
                            sourcesStack.Children.Add(new Border
                            {
                                Background = Brush("#0C1317"),
                                BorderBrush = Brush("#1D2A32"),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(14),
                                Padding = new Thickness(12, 10, 12, 10),
                                Margin = new Thickness(0, 10, 0, 0),
                                Child = new TextBlock { Text = rootPath, Foreground = Brush("#B5C2CA"), TextWrapping = TextWrapping.Wrap }
                            });
                        }
                        sourcesCard.Child = sourcesStack;
                        sidePanel.Children.Add(sourcesCard);

                        if (refreshButton != null) refreshButton.IsEnabled = true;
                        manualButton.IsEnabled = summary.ManualItemCount > 0;
                    }, delegate(Exception ex)
                    {
                        if (previewWindow == null || !previewWindow.IsVisible) return;
                        if (refreshVersion != previewWindowRefreshVersion) return;
                        RenderPreviewError(ex.Message);
                        status.Text = "Preview failed";
                        Log(ex.Message);
                        headerMeta.Text = ex.Message;
                        statsGrid.Children.Clear();
                        autoReadyPanel.Children.Clear();
                        sidePanel.Children.Clear();
                        autoReadyPanel.Children.Add(new Border
                        {
                            Background = Brush("#231519"),
                            BorderBrush = Brush("#6B2E38"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(18),
                            Padding = new Thickness(20),
                            Child = new TextBlock { Text = ex.Message, Foreground = Brush("#F3B8C2"), TextWrapping = TextWrapping.Wrap }
                        });
                        sidePanel.Children.Add(new Border
                        {
                            Background = Brush("#11181D"),
                            BorderBrush = Brush("#243742"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(22),
                            Padding = new Thickness(18),
                            Child = new TextBlock { Text = "Preview refresh failed. You can try Refresh again after checking the source folders or settings.", Foreground = Brush("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                        });
                        if (refreshButton != null) refreshButton.IsEnabled = true;
                        if (manualButton != null) manualButton.IsEnabled = true;
                    });
                };

                refreshButton.Click += delegate { renderWindow(); };
                manualButton.Click += delegate
                {
                    OpenManualIntakeWindow();
                    renderWindow();
                };

                renderWindow();
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                if (previewWindow != null) previewWindow.Close();
                RenderPreviewError(ex.Message);
                status.Text = "Preview failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        List<ReviewItem> BuildReviewItems()
        {
            return BuildReviewItems(BuildSourceInventory(false).TopLevelMediaFiles);
        }

        List<ReviewItem> BuildReviewItems(IEnumerable<string> sourceFiles)
        {
            var items = new List<ReviewItem>();
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(file);
                if (!CanUpdateMetadata(fileName)) continue;
                var platformTags = DetectPlatformTags(fileName);
                bool preserveFileTimes = platformTags.Contains("Xbox") || IsVideo(file);
                DateTime dt = preserveFileTimes ? GetLibraryDate(file) : (ParseCaptureDate(fileName) ?? GetLibraryDate(file));
                items.Add(new ReviewItem
                {
                    FilePath = file,
                    FileName = fileName,
                    PlatformLabel = PrimaryPlatformLabel(fileName),
                    PlatformTags = platformTags,
                    CaptureTime = dt,
                    PreserveFileTimes = preserveFileTimes,
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

        List<ManualMetadataItem> BuildManualMetadataItems(IEnumerable<string> sourceFiles, HashSet<string> recognizedPaths)
        {
            var items = new List<ManualMetadataItem>();
            var known = recognizedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (known.Contains(file)) continue;
                var fileName = Path.GetFileName(file);
                if (CanUpdateMetadata(fileName)) continue;
                var captureTime = GetLibraryDate(file);
                items.Add(new ManualMetadataItem
                {
                    GameId = string.Empty,
                    FilePath = file,
                    FileName = fileName,
                    OriginalFileName = fileName,
                    CaptureTime = captureTime,
                    UseCustomCaptureTime = false,
                    GameName = string.Empty,
                    Comment = string.Empty,
                    TagText = string.Empty,
                    AddPhotographyTag = false,
                    TagSteam = false,
                    TagPs5 = false,
                    TagXbox = false,
                    TagPc = false,
                    TagOther = false,
                    CustomPlatformTag = string.Empty,
                    OriginalGameId = string.Empty,
                    OriginalCaptureTime = captureTime,
                    OriginalUseCustomCaptureTime = false,
                    OriginalGameName = string.Empty,
                    OriginalComment = string.Empty,
                    OriginalTagText = string.Empty,
                    OriginalAddPhotographyTag = false,
                    OriginalTagSteam = false,
                    OriginalTagPc = false,
                    OriginalTagPs5 = false,
                    OriginalTagXbox = false,
                    OriginalTagOther = false,
                    OriginalCustomPlatformTag = string.Empty
                });
            }
            return items.OrderBy(i => i.CaptureTime).ThenBy(i => i.FileName).ToList();
        }

        bool ShowMetadataReviewWindow(List<ReviewItem> items)
        {
            if (items == null || items.Count == 0) return true;
            var reviewWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " Review",
                Width = 1260,
                Height = 900,
                MinWidth = 1020,
                MinHeight = 760,
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
            bannerStack.Children.Add(new TextBlock { Text = "Review comments before finish", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            bannerStack.Children.Add(new TextBlock { Text = items.Count + " image(s) are queued for metadata and move. Add optional comments, game-photography tags, console tags, or mark files for deletion before finishing.", Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            bannerGrid.Children.Add(bannerStack);
            var cancelButton = Btn("Cancel Import", null, "#334249", Brushes.White);
            cancelButton.Margin = new Thickness(12, 0, 0, 0);
            cancelButton.Padding = new Thickness(22, 12, 22, 12);
            Grid.SetColumn(cancelButton, 1);
            bannerGrid.Children.Add(cancelButton);
            var finishButton = Btn("Finish", null, "#275D47", Brushes.White);
            finishButton.Margin = new Thickness(12, 0, 0, 0);
            finishButton.Padding = new Thickness(22, 12, 22, 12);
            Grid.SetColumn(finishButton, 2);
            bannerGrid.Children.Add(finishButton);
            banner.Child = bannerGrid;
            root.Children.Add(banner);
            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);
            var listCard = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Margin = new Thickness(0, 0, 16, 0) };
            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            listGrid.Children.Add(new TextBlock { Text = "Queued images", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });
            var fileList = new ListBox
            {
                Background = Brush("#12191E"),
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(fileList, 1);
            listGrid.Children.Add(fileList);
            listCard.Child = listGrid;
            main.Children.Add(listCard);
            var detailCard = new Border { Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(18) };
            Grid.SetColumn(detailCard, 1);
            var detailGrid = new Grid();
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var detailHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var selectedTitle = new TextBlock { Text = string.Empty, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), TextWrapping = TextWrapping.Wrap };
            var selectedMeta = new TextBlock { Text = string.Empty, Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            detailHeader.Children.Add(selectedTitle);
            detailHeader.Children.Add(selectedMeta);
            detailGrid.Children.Add(detailHeader);
            var previewBorder = new Border { Background = Brush("#F4F7FA"), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 14), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            var previewImage = new Image { Stretch = Stretch.Uniform };
            previewBorder.Child = previewImage;
            Grid.SetRow(previewBorder, 1);
            detailGrid.Children.Add(previewBorder);
            var commentHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            commentHeader.ColumnDefinitions.Add(new ColumnDefinition());
            commentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            commentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var commentLabel = new TextBlock { Text = "Comment for Immich description", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), VerticalAlignment = VerticalAlignment.Center };
            var photographyBox = new CheckBox { Content = "Add Game Photography tag", Foreground = Brush("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var steamBox = new CheckBox { Content = "Steam", Foreground = Brush("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var ps5Box = new CheckBox { Content = "PS5", Foreground = Brush("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var xboxBox = new CheckBox { Content = "Xbox", Foreground = Brush("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var deleteBox = new CheckBox { Content = "Delete before processing", Foreground = Brush("#8B2F2F"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var tagToggleRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            tagToggleRow.Children.Add(photographyBox);
            tagToggleRow.Children.Add(steamBox);
            tagToggleRow.Children.Add(ps5Box);
            tagToggleRow.Children.Add(xboxBox);
            commentHeader.Children.Add(commentLabel);
            Grid.SetColumn(tagToggleRow, 1);
            commentHeader.Children.Add(tagToggleRow);
            Grid.SetColumn(deleteBox, 2);
            commentHeader.Children.Add(deleteBox);
            Grid.SetRow(commentHeader, 2);
            detailGrid.Children.Add(commentHeader);
            var commentStack = new StackPanel();
            var commentBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 120,
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                FontSize = 14
            };
            var commentStatus = new TextBlock { Text = "Leave blank to process normally.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            commentStack.Children.Add(commentBox);
            commentStack.Children.Add(commentStatus);
            Grid.SetRow(commentStack, 3);
            detailGrid.Children.Add(commentStack);
            detailCard.Child = detailGrid;
            main.Children.Add(detailCard);
            reviewWindow.Content = root;
            bool suppressCommentSync = false;
            ReviewItem selectedItem = null;
            var reviewTileBorders = new Dictionary<ReviewItem, Border>();
            Action<ReviewItem> refreshReviewTile = delegate(ReviewItem item)
            {
                Border tileBorder;
                if (item == null || !reviewTileBorders.TryGetValue(item, out tileBorder)) return;
                if (item.DeleteBeforeProcessing)
                {
                    tileBorder.Background = Brush("#4A1F24");
                    tileBorder.BorderBrush = Brush("#C96A73");
                    tileBorder.BorderThickness = new Thickness(1);
                }
                else
                {
                    tileBorder.Background = Brush("#1A2329");
                    tileBorder.BorderBrush = Brush("#1A2329");
                    tileBorder.BorderThickness = new Thickness(1);
                }
            };
            Action refreshCommentStatus = delegate
            {
                if (selectedItem == null)
                {
                    commentStatus.Text = "Leave blank to process normally.";
                    return;
                }
                var notes = new List<string>();
                if (!string.IsNullOrWhiteSpace(selectedItem.Comment)) notes.Add("comment saved");
                if (selectedItem.AddPhotographyTag) notes.Add(GamePhotographyTag + " tag enabled");
                var consoleTags = new List<string>();
                if (selectedItem.TagSteam) consoleTags.Add("Steam");
                if (selectedItem.TagPs5) consoleTags.Add("PS5");
                if (selectedItem.TagXbox) consoleTags.Add("Xbox");
                if (consoleTags.Count > 0) notes.Add("platform tags: " + string.Join(", ", consoleTags.ToArray()));
                if (selectedItem.DeleteBeforeProcessing) notes.Add("marked for deletion");
                commentStatus.Text = notes.Count == 0 ? "Leave blank to process normally." : string.Join(" | ", notes.ToArray()) + ".";
            };
            Action<ReviewItem> showItem = delegate(ReviewItem item)
            {
                selectedItem = item;
                selectedTitle.Text = item.FileName;
                selectedMeta.Text = item.PlatformLabel + " | " + item.CaptureTime.ToString("MMMM d, yyyy h:mm:ss tt") + (item.PreserveFileTimes ? " | filesystem time preserved" : string.Empty);
                previewImage.Source = LoadImageSource(item.FilePath, 1600);
                suppressCommentSync = true;
                commentBox.Text = item.Comment ?? string.Empty;
                photographyBox.IsChecked = item.AddPhotographyTag;
                steamBox.IsChecked = item.TagSteam;
                ps5Box.IsChecked = item.TagPs5;
                xboxBox.IsChecked = item.TagXbox;
                deleteBox.IsChecked = item.DeleteBeforeProcessing;
                suppressCommentSync = false;
                refreshReviewTile(item);
                refreshCommentStatus();
            };
            foreach (var item in items)
            {
                var tile = new ListBoxItem { Tag = item, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                var tileBorder = new Border { Background = Brush("#1A2329"), BorderBrush = Brush("#1A2329"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10) };
                var tileStack = new StackPanel();
                tileStack.Children.Add(new TextBlock { Text = "[" + item.PlatformLabel + "]", Foreground = PreviewBadgeBrush(item.PlatformLabel), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                tileStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
                tileStack.Children.Add(new TextBlock { Text = item.CaptureTime.ToString("MMM d, yyyy h:mm tt"), Foreground = Brush("#B7C6C0"), Margin = new Thickness(0, 6, 0, 0) });
                tileBorder.Child = tileStack;
                tile.Content = tileBorder;
                reviewTileBorders[item] = tileBorder;
                refreshReviewTile(item);
                fileList.Items.Add(tile);
            }
            fileList.SelectionChanged += delegate
            {
                var entry = fileList.SelectedItem as ListBoxItem;
                if (entry != null && entry.Tag is ReviewItem) showItem((ReviewItem)entry.Tag);
            };
            commentBox.TextChanged += delegate
            {
                if (suppressCommentSync || selectedItem == null) return;
                selectedItem.Comment = commentBox.Text;
                refreshReviewTile(selectedItem);
                refreshCommentStatus();
            };
            photographyBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.AddPhotographyTag = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            photographyBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.AddPhotographyTag = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            steamBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagSteam = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            steamBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagSteam = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            ps5Box.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagPs5 = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            ps5Box.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagPs5 = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            xboxBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagXbox = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            xboxBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagXbox = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            deleteBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.DeleteBeforeProcessing = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            deleteBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.DeleteBeforeProcessing = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            finishButton.Click += delegate
            {
                if (!suppressCommentSync && selectedItem != null)
                {
                    selectedItem.Comment = commentBox.Text;
                    selectedItem.AddPhotographyTag = photographyBox.IsChecked == true;
                    selectedItem.TagSteam = steamBox.IsChecked == true;
                    selectedItem.TagPs5 = ps5Box.IsChecked == true;
                    selectedItem.TagXbox = xboxBox.IsChecked == true;
                    selectedItem.DeleteBeforeProcessing = deleteBox.IsChecked == true;
                }
                var deleteCount = items.Count(i => i.DeleteBeforeProcessing);
                var processCount = items.Count - deleteCount;
                if (deleteCount > 0)
                {
                    var deleteChoice = MessageBox.Show(deleteCount + " image(s) are marked for deletion.\n" + processCount + " image(s) will continue through metadata and move.\n\nYes = Finish and delete them\nNo = Finish without deleting\nCancel = Keep reviewing", "Finish Processing", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (deleteChoice == MessageBoxResult.Cancel) return;
                    if (deleteChoice == MessageBoxResult.No)
                    {
                        foreach (var item in items)
                        {
                            item.DeleteBeforeProcessing = false;
                            refreshReviewTile(item);
                        }
                        if (selectedItem != null)
                        {
                            suppressCommentSync = true;
                            deleteBox.IsChecked = false;
                            suppressCommentSync = false;
                            refreshCommentStatus();
                        }
                    }
                }
                else
                {
                    var confirm = MessageBox.Show(processCount + " image(s) will continue through metadata and move.\n\nFinish processing?", "Finish Processing", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.OK) return;
                }
                reviewWindow.DialogResult = true;
                reviewWindow.Close();
            };
            cancelButton.Click += delegate
            {
                reviewWindow.DialogResult = false;
                reviewWindow.Close();
            };
            if (items.Count > 0)
            {
                showItem(items[0]);
                fileList.SelectedIndex = 0;
            }
            var result = reviewWindow.ShowDialog();
            return result == true;
        }

        bool ShowManualMetadataWindow(List<ManualMetadataItem> items, bool libraryMode, string contextName)
        {
            if (items == null || items.Count == 0) return true;

            var contextLabel = string.IsNullOrWhiteSpace(contextName) ? "selected folder" : contextName;
            var windowLabel = libraryMode ? "Edit Library Metadata" : "Missing Data";
            var headerTitleText = libraryMode ? "Edit library metadata" : "Add missing metadata";
            var headerDescriptionText = libraryMode
                ? items.Count + " capture(s) from " + contextLabel + " are ready for metadata edits. Select one or more files, update the game title prefix, tags, one console tag, an optional capture date/time, and an optional comment. Files can also be reorganized into the proper game folder when the title changes."
                : items.Count + " capture(s) were left in intake because they did not match a known format. Select one or more files, add a game title prefix, tags, one console tag, an optional capture date/time, and an optional comment before sending them to the destination.";
            var leaveButtonText = libraryMode ? "Close" : "Leave Unchanged";
            var finishButtonText = libraryMode ? "Apply Changes" : "Apply and Send";
            var emptySelectionText = libraryMode ? "Choose one or more library images to edit." : "Choose unmatched intake images to add metadata.";
            var defaultStatusText = libraryMode ? "Update the game title prefix, tags, one console tag, optional date/time, and an optional comment." : "Add a game title prefix, tags, one console tag, optional date/time, and an optional comment.";
            var singleSelectionMetaPrefix = libraryMode ? "Library capture time | " : "Filesystem time | ";
            var confirmCaption = libraryMode ? "Library Metadata" : "Manual Intake";
            var confirmMessage = libraryMode
                ? items.Count + " image(s) will be renamed if needed, updated with metadata, and reorganized in the library if their title changes.\n\nApply changes now?"
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
            var previewImage = new Image { Stretch = Stretch.Uniform, Height = 320 };
            var guessCallout = new Border { Background = Brush("#F4F7F9"), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 16) };
            var guessText = new TextBlock { Text = "Best Guess | No confident match", FontSize = 13, Foreground = Brush("#8B98A3"), TextWrapping = TextWrapping.Wrap };
            guessCallout.Child = guessText;
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
            detailStack.Children.Add(new TextBlock { Text = "Game title to prepend", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(gameNameBox);
            detailStack.Children.Add(new TextBlock { Text = "Additional tags", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(tagsBox);
            detailStack.Children.Add(tagToggleRow);
            detailStack.Children.Add(new TextBlock { Text = "Capture date and time", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(useCustomTimeBox);
            detailStack.Children.Add(new TextBlock { Text = "If left off, PixelVault uses the existing filesystem timestamp.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            detailStack.Children.Add(dateRow);
            detailStack.Children.Add(new TextBlock { Text = "Comment for Immich description", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(commentBox);
            detailStack.Children.Add(statusText);

            bool suppressSync = false;
            var badgeBlocks = new Dictionary<ManualMetadataItem, TextBlock>();
            var tileBorders = new Dictionary<ManualMetadataItem, Border>();
            var selectedItems = new List<ManualMetadataItem>();
            Action refreshSelectionStatus = null;
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

            Func<ManualMetadataItem, string> manualBadgeLabel = delegate(ManualMetadataItem item)
            {
                if (item.TagSteam) return "Steam";
                if (item.TagPc) return "PC";
                if (item.TagPs5) return "PS5";
                if (item.TagXbox) return "Xbox";
                if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) return CleanTag(item.CustomPlatformTag);
                return "Manual";
            };

            Func<string, Brush> manualBadgeBrush = delegate(string label)
            {
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
                var grid = new Grid { Height = 320 };
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
                if (!string.IsNullOrWhiteSpace(commentBox.Text)) notes.Add("comment saved");
                var tagCount = ParseTagText(tagsBox.Text).Length;
                if (tagCount > 0) notes.Add(tagCount + " extra tag(s)");
                if (useCustomTimeBox.IsChecked == true) notes.Add("custom capture time");
                if (photographyBox.IsChecked == true) notes.Add(GamePhotographyTag + " tag enabled");
                if (steamBox.IsChecked == true) notes.Add("platform tag: Steam");
                else if (pcBox.IsChecked == true) notes.Add("platform tag: PC");
                else if (ps5Box.IsChecked == true) notes.Add("platform tag: PS5");
                else if (xboxBox.IsChecked == true) notes.Add("platform tag: Xbox");
                else if (otherBox.IsChecked == true) notes.Add("platform tag: " + CleanTag(otherPlatformBox.Text));
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
                    previewBorder.Child = previewImage;
                    previewImage.Source = LoadImageSource(item.FilePath, 1600);
                }
                else
                {
                    selectedTitle.Text = selectedItems.Count + " captures selected";
                    selectedMeta.Text = "Edits here apply to all selected files. Mixed values show as blank or indeterminate.";
                    guessText.Text = sharedFilenameGuess(selectedItems);
                    previewBorder.Child = buildMultiPreview(selectedItems.Count);
                }

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

            finishButton.Click += delegate
            {
                var pendingItems = selectedItems.Distinct().ToList();
                if (pendingItems.Count == 0)
                {
                    MessageBox.Show(libraryMode ? "Select at least one library image to update." : "Select at least one unmatched image to send.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
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
                var gameRows = LoadSavedGameIndexRows(libraryRoot);
                var unresolvedMasterRecords = pendingItems
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
                    : pendingItems.Count + " image(s) will be renamed if needed, tagged, updated with metadata, and moved to the destination.\n\nApply changes and send them now?";
                var confirm = MessageBox.Show(confirmText, confirmCaption, MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.OK) return;
                foreach (var item in pendingItems)
                {
                    var resolvedName = NormalizeGameIndexName(
                        string.IsNullOrWhiteSpace(item.GameName)
                            ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                            : item.GameName);
                    if (!string.IsNullOrWhiteSpace(resolvedName)) item.GameName = resolvedName;
                    var preferredGameId = ManualMetadataChangesGroupingIdentity(item) ? string.Empty : item.GameId;
                    var resolvedRow = ResolveExistingGameIndexRowForAssignment(gameRows, item.GameName, DetermineManualMetadataPlatformLabel(item), preferredGameId);
                    item.GameId = resolvedRow == null ? string.Empty : resolvedRow.GameId;
                    if (resolvedRow != null && !string.IsNullOrWhiteSpace(resolvedRow.Name)) item.GameName = resolvedRow.Name;
                }
                SaveSavedGameIndexRows(libraryRoot, gameRows);
                items.Clear();
                items.AddRange(pendingItems);
                manualWindow.DialogResult = true;
                manualWindow.Close();
            };
            leaveButton.Click += delegate { manualWindow.DialogResult = false; manualWindow.Close(); };

            manualWindow.Content = root;
            if (items.Count > 0)
            {
                if (libraryMode)
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
            var result = manualWindow.ShowDialog();
            return result == true;
        }
        void ClearLibraryImageCaches()
        {
            folderImageCache.Clear();
            folderImageCacheStamp.Clear();
            fileTagCache.Clear();
            fileTagCacheStamp.Clear();
            ClearImageCache();
        }
        void RunLibraryMetadataEdit(LibraryFolderInfo folder, Action refreshLibrary)
        {
            try
            {
                if (folder == null || string.IsNullOrWhiteSpace(folder.FolderPath) || !Directory.Exists(folder.FolderPath))
                {
                    MessageBox.Show("Choose a library folder first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                EnsureExifTool();
                var items = BuildLibraryMetadataItems(folder);
                if (items.Count == 0)
                {
                    status.Text = "No library captures to edit";
                    Log("Library metadata editor opened, but no media files were found in " + folder.FolderPath + ".");
                    MessageBox.Show("There are no capture files in this folder yet.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                status.Text = "Editing library metadata";
                Log("Opening library metadata editor for " + items.Count + " capture(s) in " + folder.Name + ".");
                if (!ShowManualMetadataWindow(items, true, folder.Name))
                {
                    status.Text = "Library metadata unchanged";
                    Log("Library metadata editor closed for " + folder.Name + ".");
                    return;
                }
                RunLibraryMetadataWorkflowWithProgress(folder, items, refreshLibrary);
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void RunLibraryMetadataWorkflowWithProgress(LibraryFolderInfo folder, List<ManualMetadataItem> items, Action refreshLibrary)
        {
            var originalSavedGameIndexRow = folder == null ? null : FindSavedGameIndexRow(LoadSavedGameIndexRows(libraryRoot), folder);
            var progressWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " Library Metadata Progress",
                Width = 900,
                Height = 580,
                MinWidth = 780,
                MinHeight = 520,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };
            var progressRoot = new Grid { Margin = new Thickness(18) };
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var progressTitle = new TextBlock { Text = "Applying library metadata", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
            var progressMeta = new TextBlock { Text = "Preparing " + items.Count + " capture(s)...", Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            var totalPerStage = Math.Max(items.Count, 1);
            var totalWork = totalPerStage * 3;
            var progressBar = new ProgressBar { Height = 18, Minimum = 0, Maximum = totalWork, Value = 0, IsIndeterminate = false, Margin = new Thickness(0, 0, 0, 14) };
            var progressLog = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Background = Brush("#12191E"), Foreground = Brush("#F1E9DA"), BorderBrush = Brush("#2B3A44"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Cascadia Mono") };
            var closeButton = Btn("Close", null, "#334249", Brushes.White);
            closeButton.Margin = new Thickness(0);
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            closeButton.IsEnabled = false;
            var progressLines = new List<string>();
            bool progressFinished = false;
            Action<string> appendProgress = delegate(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                progressLines.Add(line);
                while (progressLines.Count > 200) progressLines.RemoveAt(0);
                progressLog.Text = string.Join(Environment.NewLine, progressLines.ToArray());
                progressLog.ScrollToEnd();
            };
            closeButton.Click += delegate
            {
                if (!progressFinished) return;
                progressWindow.Close();
            };
            progressRoot.Children.Add(progressTitle);
            Grid.SetRow(progressMeta, 1);
            progressRoot.Children.Add(progressMeta);
            var centerPanel = new Grid();
            centerPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            centerPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            centerPanel.Children.Add(progressBar);
            var logBorder = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), BorderBrush = Brush("#26363F"), BorderThickness = new Thickness(1), Child = progressLog, Margin = new Thickness(0, 14, 0, 0) };
            Grid.SetRow(logBorder, 1);
            centerPanel.Children.Add(logBorder);
            Grid.SetRow(centerPanel, 2);
            progressRoot.Children.Add(centerPanel);
            Grid.SetRow(closeButton, 3);
            progressRoot.Children.Add(closeButton);
            progressWindow.Content = progressRoot;

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

        string GetGameNameFromFileName(string baseName)
        {
            var match = Regex.Match(baseName, "^(?<game>.+?)_(?<ts>\\d{14})(?:[_-]\\d+)?$");
            if (match.Success) return match.Groups["game"].Value;

            match = Regex.Match(baseName, "^(?<game>.+?)_(?<ts>\\d{8,})(?:[_-]\\d+)?$");
            if (match.Success) return match.Groups["game"].Value;

            match = Regex.Match(baseName, "^(?<game>.+?)-(?<year>20\\d{2})[_-](?<mon>\\d{2})[_-](?<day>\\d{2}).*$");
            if (match.Success) return match.Groups["game"].Value;

            if (baseName.Contains("_")) return baseName.Split('_')[0];

            match = Regex.Match(baseName, "^(?<game>.+?)-20\\d{2}.*$");
            if (match.Success) return match.Groups["game"].Value;

            return baseName;
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
        void ShowLibraryBrowser(bool reuseMainWindow = false)
        {
            try
            {
                EnsureDir(libraryRoot, "Library folder");
                var folders = LoadLibraryFoldersCached(libraryRoot, false);
                Button intakeReviewButton = null;
                Border intakeReviewBadge = null;
                TextBlock intakeReviewBadgeText = null;
                Action refreshIntakeReviewBadge = null;
                var libraryWindow = reuseMainWindow
                    ? this
                    : new Window
                    {
                        Title = "PixelVault " + AppVersion + " Library",
                        Width = PreferredLibraryWindowWidth(),
                        Height = PreferredLibraryWindowHeight(),
                        MinWidth = 1200,
                        MinHeight = 780,
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Background = Brush("#0F1519")
                    };
                libraryWindow.Title = "PixelVault " + AppVersion + " Library";
                libraryWindow.Width = PreferredLibraryWindowWidth();
                libraryWindow.Height = PreferredLibraryWindowHeight();
                libraryWindow.MinWidth = 1200;
                libraryWindow.MinHeight = 780;
                libraryWindow.Background = Brush("#0F1519");

                var root = new Grid { Margin = new Thickness(18) };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var left = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Margin = new Thickness(0, 0, 16, 0) };
                var leftGrid = new Grid();
                leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var leftHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var importButton = Btn("Import", null, "#2B7A52", Brushes.White);
                importButton.Width = 198;
                importButton.Height = 56;
                importButton.FontSize = 16;
                importButton.Margin = new Thickness(0, 0, 10, 0);
                var importCommentsButton = Btn("Import and Comment", null, "#355F93", Brushes.White);
                importCommentsButton.Width = 188;
                importCommentsButton.Margin = new Thickness(0, 0, 10, 0);
                var manualImportButton = Btn("Manual Import", null, "#7C5A34", Brushes.White);
                manualImportButton.Width = 170;
                manualImportButton.Margin = new Thickness(0, 0, 16, 0);
                var importActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                importActions.Children.Add(importButton);
                importActions.Children.Add(importCommentsButton);
                importActions.Children.Add(manualImportButton);
                Grid.SetColumn(importActions, 0);
                leftHeader.Children.Add(importActions);
                var settingsButton = Btn("Settings", null, "#20343A", Brushes.White);
                settingsButton.Width = 126;
                settingsButton.Height = 42;
                settingsButton.FontSize = 13;
                settingsButton.Margin = new Thickness(0, 0, 12, 0);
                Grid.SetColumn(settingsButton, 1);
                leftHeader.Children.Add(settingsButton);
                var refreshButton = Btn("Refresh", null, "#20343A", Brushes.White);
                var rebuildLibraryButton = Btn("Rebuild", null, "#2E4751", Brushes.White);
                var fetchButton = Btn("Fetch Covers", null, "#275D47", Brushes.White);
                refreshButton.Width = 146;
                rebuildLibraryButton.Width = 146;
                fetchButton.Width = 158;
                refreshButton.Margin = new Thickness(8, 0, 0, 0);
                rebuildLibraryButton.Margin = new Thickness(8, 0, 0, 0);
                fetchButton.Margin = new Thickness(8, 0, 0, 0);
                intakeReviewButton = Btn(string.Empty, null, "#152028", Brushes.White);
                intakeReviewButton.Width = 68;
                intakeReviewButton.Height = 48;
                intakeReviewButton.Padding = new Thickness(10, 6, 10, 6);
                intakeReviewButton.Margin = new Thickness(8, 0, 0, 0);
                intakeReviewButton.ToolTip = "Preview upload queue";
                var intakeReviewContent = new Grid();
                intakeReviewContent.Children.Add(BuildGamepadGlyph(Brush("#F5F7FA"), 2.15, 34, 22));
                intakeReviewBadgeText = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                intakeReviewBadge = new Border
                {
                    MinWidth = 22,
                    Height = 22,
                    Background = Brush("#FF453A"),
                    BorderBrush = Brush("#FFD6D2"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -6, -8, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    Visibility = Visibility.Collapsed,
                    Child = intakeReviewBadgeText
                };
                intakeReviewContent.Children.Add(intakeReviewBadge);
                intakeReviewButton.Content = intakeReviewContent;
                var headerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                headerActions.Children.Add(refreshButton);
                headerActions.Children.Add(rebuildLibraryButton);
                headerActions.Children.Add(fetchButton);
                headerActions.Children.Add(intakeReviewButton);
                Grid.SetColumn(headerActions, 2);
                leftHeader.Children.Add(headerActions);
                leftGrid.Children.Add(leftHeader);
                status = new TextBlock { Text = "Ready", Foreground = Brush("#8EA0AA"), FontSize = 11.5, Margin = new Thickness(2, 12, 0, 0), TextWrapping = TextWrapping.Wrap };

                var filterGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var searchPanel = new StackPanel { Width = 320 };
                searchPanel.Children.Add(new TextBlock { Text = "Search", Foreground = Brush("#A7B5BD"), Margin = new Thickness(0, 0, 0, 6) });
                var searchBox = new TextBox { Padding = new Thickness(10, 6, 10, 6), Background = Brush("#182129"), Foreground = Brush("#F1E9DA"), BorderBrush = Brush("#2D3A43"), BorderThickness = new Thickness(1), FontSize = 13 };
                var searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                searchPanel.Children.Add(searchBox);
                Grid.SetColumn(searchPanel, 0);
                filterGrid.Children.Add(searchPanel);
                var gameIndexButton = Btn("Game Index", null, "#C7B6F4", Brush("#2E2547"));
                gameIndexButton.Width = 114;
                gameIndexButton.Height = 36;
                gameIndexButton.FontSize = 12;
                gameIndexButton.Margin = new Thickness(0, 0, 10, 0);
                var photoIndexButton = Btn("Photo Index", null, "#D7C8FB", Brush("#2E2547"));
                photoIndexButton.Width = 114;
                photoIndexButton.Height = 36;
                photoIndexButton.FontSize = 12;
                photoIndexButton.Margin = new Thickness(0);
                var indexButtonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(18, 0, 18, 0)
                };
                indexButtonsPanel.Children.Add(gameIndexButton);
                indexButtonsPanel.Children.Add(photoIndexButton);
                Grid.SetColumn(indexButtonsPanel, 1);
                filterGrid.Children.Add(indexButtonsPanel);
                var sortPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 0, 0) };
                sortPanel.Children.Add(new TextBlock { Text = "Sort", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                var sortModeBoxShell = new Border
                {
                    Width = 182,
                    Height = 40,
                    Background = Brush("#F6F8FA"),
                    BorderBrush = Brush("#C9D4DD"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 2, 10, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var sortModeBox = new ComboBox
                {
                    Background = Brushes.Transparent,
                    Foreground = Brush("#1F2A30"),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    ItemsSource = new[] { "Platform", "Recently Added", "Most Photos" },
                    SelectedItem = LibraryFolderSortModeLabel(libraryFolderSortMode)
                };
                var sortModeItemStyle = new Style(typeof(ComboBoxItem));
                sortModeItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
                sortModeItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
                sortModeBox.ItemContainerStyle = sortModeItemStyle;
                sortModeBoxShell.Child = sortModeBox;
                sortPanel.Children.Add(sortModeBoxShell);
                Grid.SetColumn(sortPanel, 2);
                filterGrid.Children.Add(sortPanel);
                var sizePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18, 0, 0, 0) };
                sizePanel.Children.Add(new TextBlock { Text = "Folder size", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                var folderTileSizeSlider = new Slider { Minimum = 140, Maximum = 340, Value = NormalizeLibraryFolderTileSize(libraryFolderTileSize), Width = 170, TickFrequency = 10, IsSnapToTickEnabled = true };
                folderTileSizeSlider.VerticalAlignment = VerticalAlignment.Center;
                folderTileSizeSlider.Margin = new Thickness(0, 6, 0, 0);
                sizePanel.Children.Add(folderTileSizeSlider);
                var folderTileSizeValue = new TextBlock { Text = NormalizeLibraryFolderTileSize(libraryFolderTileSize).ToString(), Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 40 };
                sizePanel.Children.Add(folderTileSizeValue);
                Grid.SetColumn(sizePanel, 3);
                filterGrid.Children.Add(sizePanel);
                Grid.SetRow(filterGrid, 1);
                leftGrid.Children.Add(filterGrid);

                var tileRows = CreateVirtualizedRowHost(new Thickness(0, 16, 0, 0), null);
                var tileScroll = tileRows.ScrollViewer;
                Grid.SetRow(tileScroll, 2);
                leftGrid.Children.Add(tileScroll);
                Grid.SetRow(status, 3);
                leftGrid.Children.Add(status);
                left.Child = leftGrid;
                root.Children.Add(left);

                var right = new Border { Background = Brush("#0F151A"), CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = Brush("#28353D"), BorderThickness = new Thickness(1) };
                Grid.SetColumn(right, 1);
                var rightGrid = new Grid();
                rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var banner = new Border { Background = Brush("#182129"), CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14), BorderBrush = Brush("#2D3A43"), BorderThickness = new Thickness(1) };
                var bannerGrid = new Grid();
                bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var previewImage = new Image { Width = 210, Height = 315, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 16, 0) };
                bannerGrid.Children.Add(previewImage);
                var textStack = new StackPanel();
                var detailTitle = new TextBlock { Text = "Select a folder", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F1E9DA") };
                var detailMeta = new TextBlock { Text = "Browse the library you chose in Settings.", Foreground = Brush("#A7B5BD"), Margin = new Thickness(0, 8, 0, 10), TextWrapping = TextWrapping.Wrap };
                var openFolderButton = Btn("Open Folder", null, "#275D47", Brushes.White);
                var scanFolderButton = Btn("Scan Folder", null, "#20343A", Brushes.White);
                var editMetadataButton = Btn("Edit Metadata", null, "#20343A", Brushes.White);
                openFolderButton.Margin = new Thickness(0, 0, 12, 0);
                scanFolderButton.Margin = new Thickness(0, 0, 12, 0);
                editMetadataButton.Margin = new Thickness(0);
                var bannerButtonRow = new Grid { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bannerButtonRow.Children.Add(openFolderButton);
                Grid.SetColumn(scanFolderButton, 1);
                bannerButtonRow.Children.Add(scanFolderButton);
                Grid.SetColumn(editMetadataButton, 2);
                bannerButtonRow.Children.Add(editMetadataButton);
                textStack.Children.Add(detailTitle);
                textStack.Children.Add(detailMeta);
                textStack.Children.Add(bannerButtonRow);
                Grid.SetColumn(textStack, 1);
                bannerGrid.Children.Add(textStack);
                banner.Child = bannerGrid;
                rightGrid.Children.Add(banner);

                var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                var thumbLabel = new TextBlock { Text = "All captures", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F1E9DA"), VerticalAlignment = VerticalAlignment.Center };
                controls.Children.Add(thumbLabel);
                var sliderPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var sliderLabel = new TextBlock { Text = "Capture size", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var sizeValue = new TextBlock { Text = "500", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 40 };
                var thumbSizeSlider = new Slider { Minimum = 140, Maximum = 500, Value = 500, Width = 170, TickFrequency = 20, IsSnapToTickEnabled = true };
                var deleteSelectedButton = new Button
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(10, 0, 0, 0),
                    Padding = new Thickness(0),
                    Background = Brush("#A3473E"),
                    BorderBrush = Brush("#C46A5D"),
                    BorderThickness = new Thickness(1),
                    Foreground = Brushes.White,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Delete selected capture(s)",
                    Content = new TextBlock
                    {
                        Text = "🗑",
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    }
                };
                deleteSelectedButton.IsEnabled = false;
                sliderPanel.Children.Add(sliderLabel);
                sliderPanel.Children.Add(thumbSizeSlider);
                sliderPanel.Children.Add(sizeValue);
                sliderPanel.Children.Add(deleteSelectedButton);
                DockPanel.SetDock(sliderPanel, Dock.Right);
                controls.Children.Add(sliderPanel);
                Grid.SetRow(controls, 1);
                rightGrid.Children.Add(controls);

                var detailRows = CreateVirtualizedRowHost(new Thickness(0), Brush("#0F151A"));
                var thumbScroll = detailRows.ScrollViewer;
                Grid.SetRow(thumbScroll, 2);
                rightGrid.Children.Add(thumbScroll);
                right.Child = rightGrid;
                root.Children.Add(right);
                libraryWindow.Content = root;

                LibraryFolderInfo current = null;
                Action<string, bool> runLibraryScan = null;
                Action<LibraryFolderInfo> openLibraryMetadataEditor = null;
                Action<string> openSingleFileMetadataEditor = null;
                Action<bool> renderTiles = null;
                Action applySearchFilter = null;
                Action<LibraryFolderInfo> showFolder = null;
                Action<List<LibraryFolderInfo>, string, bool> runScopedCoverRefresh = null;
                Action refreshDetailSelectionUi = null;
                Action deleteSelectedLibraryFiles = null;
                Action openSelectedLibraryMetadataEditor = null;
                int intakeBadgeRefreshVersion = 0;
                bool preserveDetailScrollOnNextRender = false;
                double preservedDetailScrollOffset = 0;
                bool preserveFolderScrollOnNextRender = false;
                double preservedFolderScrollOffset = 0;
                var selectedDetailFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var detailTiles = new List<Border>();
                int estimatedDetailRowHeight = 420;

                Func<List<string>> getSelectedDetailFiles = delegate
                {
                    if (current == null) return new List<string>();
                    var visibleFiles = GetFilesForLibraryFolderEntry(current, false)
                        .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                    foreach (var stale in selectedDetailFiles.Where(path => !visibleSet.Contains(path)).ToList()) selectedDetailFiles.Remove(stale);
                    return visibleFiles.Where(path => selectedDetailFiles.Contains(path)).ToList();
                };

                Action<string, bool, bool> updateDetailSelection = delegate(string filePath, bool additive, bool toggle)
                {
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        if (!additive) selectedDetailFiles.Clear();
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        return;
                    }
                    if (!additive)
                    {
                        selectedDetailFiles.Clear();
                        selectedDetailFiles.Add(filePath);
                    }
                    else if (toggle)
                    {
                        if (selectedDetailFiles.Contains(filePath)) selectedDetailFiles.Remove(filePath);
                        else selectedDetailFiles.Add(filePath);
                    }
                    else
                    {
                        selectedDetailFiles.Add(filePath);
                    }
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshDetailSelectionUi = delegate
                {
                    var selectedFiles = getSelectedDetailFiles();
                    foreach (var tile in detailTiles)
                    {
                        var file = tile == null ? string.Empty : tile.Tag as string;
                        var isSelected = !string.IsNullOrWhiteSpace(file) && selectedDetailFiles.Contains(file);
                        tile.Background = isSelected ? Brush("#1D2730") : Brush("#10181D");
                        tile.BorderBrush = isSelected ? Brush("#D46C63") : Brush("#2B3A44");
                        tile.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                    }
                    deleteSelectedButton.IsEnabled = current != null && selectedFiles.Count > 0;
                    thumbLabel.Text = selectedFiles.Count > 0 ? selectedFiles.Count + " selected" : "All captures";
                };
                detailRows.BeforeVisibleRowsRebuilt = delegate
                {
                    detailTiles.Clear();
                };
                detailRows.AfterVisibleRowsRebuilt = delegate
                {
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshIntakeReviewBadge = delegate
                {
                    if (intakeReviewButton == null || intakeReviewBadge == null || intakeReviewBadgeText == null) return;
                    var refreshVersion = ++intakeBadgeRefreshVersion;
                    System.Threading.Tasks.Task.Factory.StartNew(delegate
                    {
                        try
                        {
                            EnsureSourceFolders();
                            return BuildSourceInventory(false).TopLevelMediaFiles.Count;
                        }
                        catch
                        {
                            return -1;
                        }
                    }).ContinueWith(delegate(System.Threading.Tasks.Task<int> badgeTask)
                    {
                        libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (refreshVersion != intakeBadgeRefreshVersion) return;
                            var count = badgeTask.Status == TaskStatus.RanToCompletion ? badgeTask.Result : -1;
                            if (count > 0)
                            {
                                intakeReviewBadgeText.Text = IntakeBadgeCountText(count);
                                intakeReviewBadge.Visibility = Visibility.Visible;
                                intakeReviewButton.ToolTip = count + " intake item(s) waiting";
                            }
                            else
                            {
                                intakeReviewBadgeText.Text = string.Empty;
                                intakeReviewBadge.Visibility = Visibility.Collapsed;
                                intakeReviewButton.ToolTip = count == 0 ? "No intake items waiting" : "Preview upload queue";
                            }
                        }));
                    }, TaskScheduler.Default);
                };

                openSingleFileMetadataEditor = delegate(string filePath)
                {
                    if (current == null)
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedFiles = getSelectedDetailFiles();
                    var useSelection = selectedFiles.Count > 0
                        && (string.IsNullOrWhiteSpace(filePath) || selectedDetailFiles.Contains(filePath));
                    var wantedFiles = useSelection
                        ? new HashSet<string>(selectedFiles, StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(new[] { filePath }, StringComparer.OrdinalIgnoreCase);
                    if (wantedFiles.Count == 0)
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedItems = BuildLibraryMetadataItems(current)
                        .Where(item => wantedFiles.Contains(item.FilePath))
                        .ToList();
                    if (selectedItems.Count == 0)
                    {
                        MessageBox.Show("That capture could not be loaded for metadata editing.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedTitle = selectedItems.Count == 1
                        ? Path.GetFileName(selectedItems[0].FilePath)
                        : (current.Name + " (" + selectedItems.Count + " selected)");
                    status.Text = selectedItems.Count == 1 ? "Editing selected capture metadata" : "Editing selected capture metadata";
                    Log("Opening library metadata editor for " + selectedItems.Count + " selected capture(s) in " + current.Name + ".");
                    if (!ShowManualMetadataWindow(selectedItems, true, selectedTitle))
                    {
                        status.Text = "Library metadata unchanged";
                        return;
                    }
                    var currentFolderPath = current.FolderPath;
                    var currentPlatformLabel = current.PlatformLabel;
                    var currentName = current.Name;
                    RunLibraryMetadataWorkflowWithProgress(current, selectedItems, delegate
                    {
                        selectedDetailFiles.Clear();
                        current = string.IsNullOrWhiteSpace(currentFolderPath)
                            ? null
                            : new LibraryFolderInfo { FolderPath = currentFolderPath, PlatformLabel = currentPlatformLabel ?? string.Empty, Name = currentName ?? string.Empty };
                        folders = LoadLibraryFoldersCached(libraryRoot, false);
                        renderTiles(false);
                    });
                };

                openSelectedLibraryMetadataEditor = delegate
                {
                    var selectedFiles = getSelectedDetailFiles();
                    if (selectedFiles.Count == 0)
                    {
                        openLibraryMetadataEditor(current);
                        return;
                    }
                    openSingleFileMetadataEditor(null);
                };

                deleteSelectedLibraryFiles = delegate
                {
                    if (current == null)
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedFiles = getSelectedDetailFiles()
                        .Where(file => !string.IsNullOrWhiteSpace(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (selectedFiles.Count == 0)
                    {
                        MessageBox.Show("Select one or more captures to delete.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var confirm = MessageBox.Show(
                        "Delete " + selectedFiles.Count + " selected capture(s) from the library?\n\nThis removes the file" + (selectedFiles.Count == 1 ? string.Empty : "s") + " from disk and removes the photo index record" + (selectedFiles.Count == 1 ? string.Empty : "s") + ".",
                        "Delete Capture",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.OK) return;

                    var removedFiles = new List<string>();
                    var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var failures = new List<string>();
                    foreach (var file in selectedFiles)
                    {
                        try
                        {
                            var directory = Path.GetDirectoryName(file) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(directory)) touchedDirectories.Add(directory);
                            DeleteMetadataSidecarIfPresent(file);
                            if (File.Exists(file))
                            {
                                File.Delete(file);
                                removedFiles.Add(file);
                                Log("Library delete: " + file);
                            }
                            else
                            {
                                removedFiles.Add(file);
                            }
                        }
                        catch (Exception deleteEx)
                        {
                            failures.Add(Path.GetFileName(file) + ": " + deleteEx.Message);
                            Log("Library delete failed for " + file + ". " + deleteEx.Message);
                        }
                    }

                    if (removedFiles.Count > 0)
                    {
                        RemoveLibraryMetadataIndexEntries(removedFiles, libraryRoot);
                    }
                    foreach (var directory in touchedDirectories) TryDeleteEmptyDirectory(directory);
                    selectedDetailFiles.Clear();
                    folders = LoadLibraryFoldersCached(libraryRoot, false);
                    current = string.IsNullOrWhiteSpace(current.FolderPath)
                        ? null
                        : new LibraryFolderInfo
                        {
                            FolderPath = current.FolderPath,
                            PlatformLabel = current.PlatformLabel ?? string.Empty,
                            Name = current.Name ?? string.Empty
                        };
                    renderTiles(false);
                    status.Text = removedFiles.Count == 0
                        ? "No captures deleted"
                        : (failures.Count == 0
                            ? "Deleted " + removedFiles.Count + " capture(s)"
                            : "Deleted " + removedFiles.Count + " capture(s) with " + failures.Count + " failure(s)");
                    if (failures.Count > 0)
                    {
                        MessageBox.Show(
                            "Some files could not be deleted." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(8).ToArray()),
                            "PixelVault",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                };

                Action renderSelectedFolder = delegate
                {
                    var renderStopwatch = Stopwatch.StartNew();
                    if (!preserveDetailScrollOnNextRender) preservedDetailScrollOffset = 0;
                    detailTiles.Clear();
                    if (current == null)
                    {
                        selectedDetailFiles.Clear();
                        SetVirtualizedRows(detailRows, new List<VirtualizedRowDefinition>(), true, null);
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=(none); rows=0; files=0", 40);
                        return;
                    }
                    var size = (int)thumbSizeSlider.Value;
                    sizeValue.Text = size.ToString();
                    estimatedDetailRowHeight = Math.Max(220, size + 96);
                    var datedFiles = GetFilesForLibraryFolderEntry(current, false)
                        .Select(file => new { FilePath = file, CaptureDate = GetLibraryDate(file) })
                        .OrderByDescending(entry => entry.CaptureDate)
                        .ToList();
                    var groups = datedFiles
                        .GroupBy(entry => entry.CaptureDate.Date)
                        .OrderByDescending(g => g.Key)
                        .ToList();
                    var visibleFiles = groups.SelectMany(group => group.Select(entry => entry.FilePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                    foreach (var stale in selectedDetailFiles.Where(path => !visibleSet.Contains(path)).ToList()) selectedDetailFiles.Remove(stale);
                    var shouldRestoreDetailScroll = preserveDetailScrollOnNextRender && preservedDetailScrollOffset > 0.1d;
                    preserveDetailScrollOnNextRender = false;
                    if (groups.Count == 0)
                    {
                        SetVirtualizedRows(detailRows, new[]
                        {
                            new VirtualizedRowDefinition
                            {
                                Height = 44,
                                Build = delegate
                                {
                                    return new TextBlock { Text = "No captures found in this folder.", Foreground = Brush("#A7B5BD") };
                                }
                            }
                        }, !shouldRestoreDetailScroll, shouldRestoreDetailScroll ? (double?)preservedDetailScrollOffset : null);
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (current.Name ?? current.FolderPath ?? "(unknown)") + "; rows=1; files=0; size=" + size, 40);
                        return;
                    }
                    var detailColumns = CalculateVirtualizedTileColumns(thumbScroll, size, 10, 32);
                    var virtualRows = new List<VirtualizedRowDefinition>();
                    foreach (var group in groups)
                    {
                        var groupDate = group.Key;
                        var groupFiles = group.Select(entry => entry.FilePath).ToList();
                        virtualRows.Add(new VirtualizedRowDefinition
                        {
                            Height = 34,
                            Build = delegate
                            {
                                return new TextBlock
                                {
                                    Text = groupDate.ToString("MMMM d, yyyy"),
                                    FontSize = 16,
                                    FontWeight = FontWeights.SemiBold,
                                    Foreground = Brush("#F1E9DA"),
                                    Margin = new Thickness(0, 0, 0, 10)
                                };
                            }
                        });
                        for (int rowStart = 0; rowStart < groupFiles.Count; rowStart += detailColumns)
                        {
                            var rowFiles = groupFiles.Skip(rowStart).Take(detailColumns).ToList();
                            virtualRows.Add(new VirtualizedRowDefinition
                            {
                                Height = estimatedDetailRowHeight,
                                Build = delegate
                                {
                                    var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
                                    var renderFolder = current;
                                    foreach (var file in rowFiles)
                                    {
                                        var tile = CreateLibraryDetailTile(
                                            file,
                                            size,
                                            delegate { return SameLibraryFolderSelection(current, renderFolder); },
                                            openSingleFileMetadataEditor,
                                            updateDetailSelection,
                                            selectedDetailFiles,
                                            refreshDetailSelectionUi);
                                        detailTiles.Add(tile);
                                        wrap.Children.Add(tile);
                                    }
                                    return new Border { Height = estimatedDetailRowHeight, Background = Brushes.Transparent, Child = wrap };
                                }
                            });
                        }
                    }
                    SetVirtualizedRows(detailRows, virtualRows, !shouldRestoreDetailScroll, shouldRestoreDetailScroll ? (double?)preservedDetailScrollOffset : null);
                    renderStopwatch.Stop();
                    LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (current.Name ?? current.FolderPath ?? "(unknown)") + "; groups=" + groups.Count + "; files=" + visibleFiles.Count + "; rows=" + virtualRows.Count + "; columns=" + detailColumns + "; size=" + size, 40);
                };

                Func<LibraryFolderInfo, int, int, bool, Button> buildFolderTile = delegate(LibraryFolderInfo folder, int tileWidth, int tileHeight, bool showPlatformBadge)
                {
                    var tile = new Button
                    {
                        Width = tileWidth,
                        Height = tileHeight + 82,
                        Margin = new Thickness(0, 0, 14, 14),
                        Padding = new Thickness(0),
                        Background = Brush("#1A2329"),
                        BorderBrush = Brush("#243139"),
                        BorderThickness = new Thickness(1),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        VerticalContentAlignment = VerticalAlignment.Stretch
                    };
                    var tileStack = new StackPanel();
                    var imageBorder = new Border { Width = tileWidth, Height = tileHeight, Background = Brush("#0E1418") };
                    if (showPlatformBadge)
                    {
                        var imageGrid = new Grid();
                        imageGrid.Children.Add(CreateAsyncImageTile(
                            ResolveLibraryArt(folder, false),
                            Math.Max(tileWidth * 3, 760),
                            tileWidth,
                            tileHeight,
                            Stretch.UniformToFill,
                            folder.PlatformLabel,
                            Brushes.White,
                            new Thickness(0),
                            new Thickness(0),
                            Brushes.Transparent,
                            new CornerRadius(0),
                            Brushes.Transparent,
                            new Thickness(0)));
                        imageGrid.Children.Add(BuildLibraryTilePlatformBadge(folder.PlatformLabel));
                        imageBorder.Child = imageGrid;
                    }
                    else
                    {
                        imageBorder.Child = CreateAsyncImageTile(
                            ResolveLibraryArt(folder, false),
                            Math.Max(tileWidth * 3, 760),
                            tileWidth,
                            tileHeight,
                            Stretch.UniformToFill,
                            folder.PlatformLabel,
                            Brushes.White,
                            new Thickness(0),
                            new Thickness(0),
                            Brushes.Transparent,
                            new CornerRadius(0),
                            Brushes.Transparent,
                            new Thickness(0));
                    }
                    tileStack.Children.Add(imageBorder);
                    tileStack.Children.Add(new TextBlock { Text = folder.Name, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.White, Margin = new Thickness(12, 12, 12, 4), FontWeight = FontWeights.SemiBold, FontSize = 13, Height = 34 });
                    tileStack.Children.Add(new TextBlock { Text = folder.FileCount + " item(s) | " + folder.PlatformLabel, Foreground = Brush("#B7C6C0"), Margin = new Thickness(12, 0, 12, 12), FontSize = 10.5, Height = 16 });
                    tile.Content = tileStack;
                    tile.Click += delegate { showFolder(folder); };
                    var contextMenu = new ContextMenu();
                    var setCoverItem = new MenuItem { Header = "Set Custom Cover..." };
                    setCoverItem.Click += delegate
                    {
                        var pickedCover = PickFile(ResolveLibraryArt(folder, false), "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*");
                        if (string.IsNullOrWhiteSpace(pickedCover)) return;
                        SaveCustomCover(folder, pickedCover);
                        showFolder(folder);
                        renderTiles(false);
                        Log("Custom cover set for " + folder.Name + " | " + folder.PlatformLabel + ".");
                    };
                    var clearCoverItem = new MenuItem { Header = "Clear Custom Cover", IsEnabled = !string.IsNullOrWhiteSpace(CustomCoverPath(folder)) };
                    clearCoverItem.Click += delegate
                    {
                        ClearCustomCover(folder);
                        showFolder(folder);
                        renderTiles(false);
                        Log("Custom cover cleared for " + folder.Name + " | " + folder.PlatformLabel + ".");
                    };
                    var openFolderItem = new MenuItem { Header = "Open Folder" };
                    openFolderItem.Click += delegate { OpenFolder(folder.FolderPath); };
                    var editMetadataItem = new MenuItem { Header = "Edit Metadata" };
                    editMetadataItem.Click += delegate { openLibraryMetadataEditor(folder); };
                    var editIdsItem = new MenuItem { Header = "Edit IDs..." };
                    editIdsItem.Click += delegate
                    {
                        OpenLibraryFolderIdEditor(folder, delegate
                        {
                            showFolder(folder);
                            renderTiles(false);
                        });
                    };
                    var refreshFolderItem = new MenuItem { Header = "Refresh Folder" };
                    refreshFolderItem.Click += delegate { runLibraryScan(folder.FolderPath, false); };
                    var rebuildFolderItem = new MenuItem { Header = "Rebuild Folder" };
                    rebuildFolderItem.Click += delegate { runLibraryScan(folder.FolderPath, true); };
                    var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art" };
                    fetchFolderCoverItem.Click += delegate
                    {
                        showFolder(folder);
                        runScopedCoverRefresh(new List<LibraryFolderInfo> { folder }, folder.Name + " | " + folder.PlatformLabel, true);
                    };
                    contextMenu.Items.Add(openFolderItem);
                    contextMenu.Items.Add(editMetadataItem);
                    contextMenu.Items.Add(editIdsItem);
                    contextMenu.Items.Add(new Separator());
                    contextMenu.Items.Add(refreshFolderItem);
                    contextMenu.Items.Add(rebuildFolderItem);
                    contextMenu.Items.Add(fetchFolderCoverItem);
                    contextMenu.Items.Add(new Separator());
                    contextMenu.Items.Add(setCoverItem);
                    contextMenu.Items.Add(clearCoverItem);
                    tile.ContextMenu = contextMenu;
                    return tile;
                };

                showFolder = delegate(LibraryFolderInfo info)
                {
                    if (!SameLibraryFolderSelection(current, info)) selectedDetailFiles.Clear();
                    preserveDetailScrollOnNextRender = false;
                    preservedDetailScrollOffset = 0;
                    thumbScroll.ScrollToVerticalOffset(0);
                    current = info;
                    detailTitle.Text = info.Name;
                    detailMeta.Text = info.FileCount + " item(s) | " + info.PlatformLabel + " | " + info.FolderPath;
                    var artPath = ResolveLibraryArt(info, false);
                    if (string.IsNullOrWhiteSpace(artPath) || !File.Exists(artPath))
                    {
                        previewImage.Source = null;
                        previewImage.Visibility = Visibility.Collapsed;
                    }
                    else QueueImageLoad(previewImage, artPath, 720, delegate(BitmapImage loaded)
                    {
                        previewImage.Source = loaded;
                        previewImage.Visibility = Visibility.Visible;
                    }, true, delegate { return SameLibraryFolderSelection(current, info); });
                    renderSelectedFolder();
                };

                renderTiles = delegate(bool forceRefresh)
                {
                    var renderStopwatch = Stopwatch.StartNew();
                    var restoreFolderScrollOffset = preserveFolderScrollOnNextRender ? Math.Max(0, preservedFolderScrollOffset) : 0;
                    var shouldRestoreFolderScroll = preserveFolderScrollOnNextRender && restoreFolderScrollOffset > 0.1d;
                    preserveFolderScrollOnNextRender = false;
                    var folderLoadStopwatch = Stopwatch.StartNew();
                    folders = LoadLibraryFoldersCached(libraryRoot, forceRefresh);
                    folderLoadStopwatch.Stop();
                    var sortMode = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
                    var flattenGroups = !string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase);
                    var searchText = string.IsNullOrWhiteSpace(searchBox.Text) ? string.Empty : searchBox.Text.Trim();
                    var filterSortStopwatch = Stopwatch.StartNew();
                    var visibleFolders = string.IsNullOrWhiteSpace(searchText)
                        ? folders
                        : folders.Where(folder =>
                            (!string.IsNullOrWhiteSpace(folder.Name) && folder.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.PlatformLabel) && folder.PlatformLabel.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.FolderPath) && folder.FolderPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.GameId) && folder.GameId.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();
                    var orderedVisibleFolders = visibleFolders
                        .OrderByDescending(folder => string.Equals(sortMode, "recent", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(folder) : DateTime.MinValue)
                        .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? folder.FileCount : 0)
                        .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(folder) : DateTime.MinValue)
                        .ThenBy(folder => string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase) ? PlatformGroupOrder(DetermineLibraryFolderGroup(folder)) : 0)
                        .ThenBy(folder => DetermineLibraryFolderGroup(folder))
                        .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    filterSortStopwatch.Stop();
                    var tileWidth = (int)folderTileSizeSlider.Value;
                    var tileHeight = (int)Math.Round(tileWidth * 1.5d);
                    folderTileSizeValue.Text = tileWidth.ToString();
                    LibraryFolderInfo selectedFolder = null;
                    if (orderedVisibleFolders.Count > 0 && current == null) selectedFolder = orderedVisibleFolders[0];
                    else if (current != null)
                    {
                        selectedFolder = folders.FirstOrDefault(f => f.FolderPath == current.FolderPath && string.Equals(f.PlatformLabel, current.PlatformLabel, StringComparison.OrdinalIgnoreCase));
                        if (selectedFolder == null) selectedFolder = folders.FirstOrDefault(f => f.FolderPath == current.FolderPath);
                    }
                    if (selectedFolder != null)
                    {
                        if (forceRefresh || !SameLibraryFolderSelection(current, selectedFolder)) showFolder(selectedFolder);
                        else current = selectedFolder;
                    }
                    else if (orderedVisibleFolders.Count > 0)
                    {
                        showFolder(orderedVisibleFolders[0]);
                    }
                    else
                    {
                        current = null;
                        selectedDetailFiles.Clear();
                        detailTitle.Text = "Select a folder";
                        detailMeta.Text = "Browse the library you chose in Settings.";
                        previewImage.Source = null;
                        previewImage.Visibility = Visibility.Collapsed;
                        renderSelectedFolder();
                    }

                    var folderCardHeight = tileHeight + 82;
                    var folderRowHeight = folderCardHeight + 14;
                    var folderColumns = CalculateVirtualizedTileColumns(tileScroll, tileWidth, 14, flattenGroups ? 28 : 56);
                    var virtualRows = new List<VirtualizedRowDefinition>();
                    if (orderedVisibleFolders.Count == 0)
                    {
                        virtualRows.Add(new VirtualizedRowDefinition
                        {
                            Height = 44,
                            Build = delegate
                            {
                                return new TextBlock
                                {
                                    Text = string.IsNullOrWhiteSpace(searchText) ? "No library folders found." : "No folders match the current search.",
                                    Foreground = Brush("#A7B5BD"),
                                    Margin = new Thickness(0, 12, 0, 0)
                                };
                            }
                        });
                        SetVirtualizedRows(tileRows, virtualRows, true, null);
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=empty; forceRefresh=" + forceRefresh + "; foldersLoaded=" + folders.Count + "; visible=0; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=" + folderLoadStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                        return;
                    }
                    if (flattenGroups)
                    {
                        for (int rowStart = 0; rowStart < orderedVisibleFolders.Count; rowStart += folderColumns)
                        {
                            var rowFolders = orderedVisibleFolders.Skip(rowStart).Take(folderColumns).ToList();
                            virtualRows.Add(new VirtualizedRowDefinition
                            {
                                Height = folderRowHeight,
                                Build = delegate
                                {
                                    var flatWrap = new WrapPanel();
                                    foreach (var folder in rowFolders) flatWrap.Children.Add(buildFolderTile(folder, tileWidth, tileHeight, true));
                                    return new Border { Height = folderRowHeight, Background = Brushes.Transparent, Child = flatWrap };
                                }
                            });
                        }
                        SetVirtualizedRows(tileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=flat; forceRefresh=" + forceRefresh + "; foldersLoaded=" + folders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=" + folderLoadStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                        return;
                    }

                    var folderGroups = orderedVisibleFolders
                        .GroupBy(folder => DetermineLibraryFolderGroup(folder))
                        .OrderBy(group => PlatformGroupOrder(group.Key))
                        .ThenBy(group => group.Key)
                        .ToList();
                    foreach (var folderGroup in folderGroups)
                    {
                        var groupFolders = folderGroup.ToList();
                        var groupLabel = folderGroup.Key;
                        virtualRows.Add(new VirtualizedRowDefinition
                        {
                            Height = 82,
                            Build = delegate
                            {
                                return new Border
                                {
                                    Height = 82,
                                    Background = Brush("#161F24"),
                                    BorderBrush = Brush("#26363F"),
                                    BorderThickness = new Thickness(1),
                                    CornerRadius = new CornerRadius(10),
                                    Padding = new Thickness(14, 10, 14, 12),
                                    Child = BuildLibrarySectionHeader(groupLabel, groupFolders.Count)
                                };
                            }
                        });
                        for (int rowStart = 0; rowStart < groupFolders.Count; rowStart += folderColumns)
                        {
                            var rowFolders = groupFolders.Skip(rowStart).Take(folderColumns).ToList();
                            virtualRows.Add(new VirtualizedRowDefinition
                            {
                                Height = folderRowHeight,
                                Build = delegate
                                {
                                    var groupWrap = new WrapPanel();
                                    foreach (var folder in rowFolders) groupWrap.Children.Add(buildFolderTile(folder, tileWidth, tileHeight, false));
                                    return new Border { Height = folderRowHeight, Background = Brushes.Transparent, Child = groupWrap };
                                }
                            });
                        }
                    }
                    SetVirtualizedRows(tileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
                    renderStopwatch.Stop();
                    LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=grouped; forceRefresh=" + forceRefresh + "; foldersLoaded=" + folders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=" + folderLoadStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                };

                runLibraryScan = delegate(string folderPath, bool forceRescan)
                {
                    Window progressWindow = null;
                    TextBlock progressTitle = null;
                    TextBlock progressMeta = null;
                    ProgressBar progressBar = null;
                    TextBox progressLog = null;
                    Button actionButton = null;
                    bool cancelRequested = false;
                    bool scanFinished = false;
                    var progressLines = new List<string>();
                    Action<string> appendProgress = delegate(string line)
                    {
                        if (string.IsNullOrWhiteSpace(line) || progressLog == null) return;
                        progressLines.Add(line);
                        while (progressLines.Count > 180) progressLines.RemoveAt(0);
                        progressLog.Text = string.Join(Environment.NewLine, progressLines.ToArray());
                        progressLog.ScrollToEnd();
                    };
                    Action finishButtons = delegate
                    {
                        refreshButton.IsEnabled = true;
                        refreshButton.IsEnabled = true;
                        rebuildLibraryButton.IsEnabled = true;
                        scanFolderButton.IsEnabled = true;
                        editMetadataButton.IsEnabled = true;
                        fetchButton.IsEnabled = true;
                        importButton.IsEnabled = true;
                        importCommentsButton.IsEnabled = true;
                        manualImportButton.IsEnabled = true;
                        System.Windows.Input.Mouse.OverrideCursor = null;
                    };
                    try
                    {
                        var scopeLabel = string.IsNullOrWhiteSpace(folderPath) ? (forceRescan ? "full library rebuild" : "full library refresh") : ((Path.GetFileName(folderPath) ?? "selected folder") + (forceRescan ? " rebuild" : " refresh"));
                        progressWindow = new Window
                        {
                            Title = "PixelVault Scan Monitor",
                            Width = 900,
                            Height = 580,
                            MinWidth = 780,
                            MinHeight = 520,
                            Owner = libraryWindow,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Background = Brush("#0F1519")
                        };
                        var progressRoot = new Grid { Margin = new Thickness(18) };
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        progressTitle = new TextBlock { Text = string.IsNullOrWhiteSpace(folderPath) ? (forceRescan ? "Rebuilding library metadata index" : "Refreshing library metadata index") : (forceRescan ? "Rebuilding folder metadata index" : "Refreshing folder metadata index"), FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
                        progressMeta = new TextBlock { Text = "Building file list...", Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
                        progressBar = new ProgressBar { Height = 18, Minimum = 0, Maximum = 1, Value = 0, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 14) };
                        progressLog = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Background = Brush("#12191E"), Foreground = Brush("#F1E9DA"), BorderBrush = Brush("#2B3A44"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Cascadia Mono") };
                        actionButton = Btn("Cancel Scan", null, "#7A2F2F", Brushes.White);
                        actionButton.Margin = new Thickness(0);
                        actionButton.HorizontalAlignment = HorizontalAlignment.Right;
                        actionButton.Click += delegate
                        {
                            if (!scanFinished)
                            {
                                cancelRequested = true;
                                actionButton.IsEnabled = false;
                                if (progressMeta != null) progressMeta.Text = "Cancel requested. Waiting for the current file to finish...";
                                appendProgress("Cancel requested. Waiting for the current file to finish.");
                            }
                            else if (progressWindow != null)
                            {
                                progressWindow.Close();
                            }
                        };
                        progressRoot.Children.Add(progressTitle);
                        Grid.SetRow(progressMeta, 1);
                        progressRoot.Children.Add(progressMeta);
                        var centerPanel = new Grid();
                        centerPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        centerPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        centerPanel.Children.Add(progressBar);
                        var logBorder = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), BorderBrush = Brush("#26363F"), BorderThickness = new Thickness(1), Child = progressLog, Margin = new Thickness(0, 14, 0, 0) };
                        Grid.SetRow(logBorder, 1);
                        centerPanel.Children.Add(logBorder);
                        Grid.SetRow(centerPanel, 2);
                        progressRoot.Children.Add(centerPanel);
                        Grid.SetRow(actionButton, 3);
                        progressRoot.Children.Add(actionButton);
                        progressWindow.Content = progressRoot;
                        progressWindow.Show();
                        appendProgress("Starting scan for " + scopeLabel + ".");
                        status.Text = string.IsNullOrWhiteSpace(folderPath) ? (forceRescan ? "Rebuilding library metadata index" : "Refreshing library metadata index") : (forceRescan ? "Rebuilding folder metadata index" : "Refreshing folder metadata index");
                        refreshButton.IsEnabled = false;
                        refreshButton.IsEnabled = false;
                        rebuildLibraryButton.IsEnabled = false;
                        scanFolderButton.IsEnabled = false;
                        editMetadataButton.IsEnabled = false;
                        fetchButton.IsEnabled = false;
                        importButton.IsEnabled = false;
                        importCommentsButton.IsEnabled = false;
                        manualImportButton.IsEnabled = false;
                        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                        var capturedFolderPath = folderPath;
                        var capturedForceRescan = forceRescan;
                        System.Threading.Tasks.Task.Factory.StartNew(delegate
                        {
                            return ScanLibraryMetadataIndex(libraryRoot, capturedFolderPath, capturedForceRescan, delegate(int currentCount, int totalCount, string detail)
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
                            }, delegate { return cancelRequested; });
                        }).ContinueWith(delegate(System.Threading.Tasks.Task<int> scanTask)
                        {
                            libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                            {
                                scanFinished = true;
                                finishButtons();
                                if (scanTask.IsCanceled || (scanTask.IsFaulted && scanTask.Exception != null && scanTask.Exception.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException)))
                                {
                                    status.Text = "Library scan cancelled";
                                    if (progressMeta != null) progressMeta.Text = "Scan cancelled before completion.";
                                    appendProgress("Scan cancelled.");
                                }
                                else if (scanTask.IsFaulted)
                                {
                                    status.Text = "Library scan failed";
                                    var flattened = scanTask.Exception == null ? null : scanTask.Exception.Flatten();
                                    var scanError = flattened == null ? new Exception("Library scan failed.") : flattened.InnerExceptions.First();
                                    if (progressMeta != null) progressMeta.Text = scanError.Message;
                                    appendProgress("ERROR: " + scanError.Message);
                                    Log(scanError.ToString());
                                    MessageBox.Show(scanError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                                else
                                {
                                    status.Text = string.IsNullOrWhiteSpace(folderPath) ? (forceRescan ? "Library metadata index rebuilt" : "Library metadata index refreshed") : (forceRescan ? "Folder metadata index rebuilt" : "Folder metadata index refreshed");
                                    if (progressMeta != null) progressMeta.Text += " | complete";
                                    appendProgress("Scan finished successfully.");
                                    if (string.IsNullOrWhiteSpace(folderPath)) current = null;
                                    else current = new LibraryFolderInfo { FolderPath = folderPath, PlatformLabel = current == null ? string.Empty : current.PlatformLabel, Name = current == null ? string.Empty : current.Name };
                                    renderTiles(false);
                                }
                                if (actionButton != null)
                                {
                                    actionButton.IsEnabled = true;
                                    actionButton.Content = "Close";
                                }
                            }));
                        });
                    }
                    catch (Exception ex)
                    {
                        scanFinished = true;
                        finishButtons();
                        status.Text = "Library scan failed";
                        Log(ex.ToString());
                        if (progressMeta != null) progressMeta.Text = ex.Message;
                        appendProgress("ERROR: " + ex.Message);
                        if (actionButton != null)
                        {
                            actionButton.IsEnabled = true;
                            actionButton.Content = "Close";
                        }
                        MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                runScopedCoverRefresh = delegate(List<LibraryFolderInfo> requestedFolders, string scopeLabel, bool forceRefreshExistingCovers)
                {
                    var targetFolders = (requestedFolders ?? new List<LibraryFolderInfo>()).Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.FolderPath)).ToList();
                    if (targetFolders.Count == 0)
                    {
                        MessageBox.Show("No library folder is available for cover refresh.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var resolvedScopeLabel = string.IsNullOrWhiteSpace(scopeLabel) ? (targetFolders.Count == 1 ? "selected folder" : "library") : scopeLabel.Trim();
                    Window progressWindow = null;
                    TextBlock progressMeta = null;
                    ProgressBar progressBar = null;
                    TextBox progressLog = null;
                    Button actionButton = null;
                    bool cancelRequested = false;
                    bool refreshFinished = false;
                    var progressLines = new List<string>();
                    Action<string> appendProgress = delegate(string line)
                    {
                        if (string.IsNullOrWhiteSpace(line) || progressLog == null) return;
                        progressLines.Add(line);
                        while (progressLines.Count > 180) progressLines.RemoveAt(0);
                        progressLog.Text = string.Join(Environment.NewLine, progressLines.ToArray());
                        progressLog.ScrollToEnd();
                    };
                    Action finishButtons = delegate
                    {
                        refreshButton.IsEnabled = true;
                        rebuildLibraryButton.IsEnabled = true;
                        scanFolderButton.IsEnabled = true;
                        editMetadataButton.IsEnabled = true;
                        fetchButton.IsEnabled = true;
                        importButton.IsEnabled = true;
                        importCommentsButton.IsEnabled = true;
                        manualImportButton.IsEnabled = true;
                        System.Windows.Input.Mouse.OverrideCursor = null;
                    };
                    try
                    {
                        progressWindow = new Window
                        {
                            Title = "PixelVault Cover Refresh",
                            Width = 900,
                            Height = 580,
                            MinWidth = 780,
                            MinHeight = 520,
                            Owner = libraryWindow,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Background = Brush("#0F1519")
                        };
                        var progressRoot = new Grid { Margin = new Thickness(18) };
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var progressTitle = new TextBlock { Text = "Resolving IDs and fetching cover art", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
                        progressMeta = new TextBlock { Text = "Preparing library entries...", Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
                        progressBar = new ProgressBar { Height = 18, Minimum = 0, Maximum = 1, Value = 0, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 14) };
                        progressLog = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Background = Brush("#12191E"), Foreground = Brush("#F1E9DA"), BorderBrush = Brush("#2B3A44"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Cascadia Mono") };
                        actionButton = Btn("Cancel Refresh", null, "#7A2F2F", Brushes.White);
                        actionButton.Margin = new Thickness(0);
                        actionButton.HorizontalAlignment = HorizontalAlignment.Right;
                        actionButton.Click += delegate
                        {
                            if (!refreshFinished)
                            {
                                cancelRequested = true;
                                actionButton.IsEnabled = false;
                                if (progressMeta != null) progressMeta.Text = "Cancel requested. Waiting for the current title to finish...";
                                appendProgress("Cancel requested. Waiting for the current title to finish.");
                            }
                            else if (progressWindow != null)
                            {
                                progressWindow.Close();
                            }
                        };
                        progressRoot.Children.Add(progressTitle);
                        Grid.SetRow(progressMeta, 1);
                        progressRoot.Children.Add(progressMeta);
                        var centerPanel = new Grid();
                        centerPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        centerPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        centerPanel.Children.Add(progressBar);
                        var logBorder = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), BorderBrush = Brush("#26363F"), BorderThickness = new Thickness(1), Child = progressLog, Margin = new Thickness(0, 14, 0, 0) };
                        Grid.SetRow(logBorder, 1);
                        centerPanel.Children.Add(logBorder);
                        Grid.SetRow(centerPanel, 2);
                        progressRoot.Children.Add(centerPanel);
                        Grid.SetRow(actionButton, 3);
                        progressRoot.Children.Add(actionButton);
                        progressWindow.Content = progressRoot;
                        progressWindow.Show();
                        appendProgress("Starting cover refresh for " + resolvedScopeLabel + ".");
                        status.Text = targetFolders.Count == 1 ? "Resolving IDs and fetching folder cover art" : "Resolving IDs and fetching cover art";
                        refreshButton.IsEnabled = false;
                        rebuildLibraryButton.IsEnabled = false;
                        scanFolderButton.IsEnabled = false;
                        editMetadataButton.IsEnabled = false;
                        fetchButton.IsEnabled = false;
                        importButton.IsEnabled = false;
                        importCommentsButton.IsEnabled = false;
                        manualImportButton.IsEnabled = false;
                        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                        System.Threading.Tasks.Task.Factory.StartNew(delegate
                        {
                            int resolved = 0;
                            int coversReady = 0;
                            RefreshLibraryCovers(libraryRoot, targetFolders, delegate(int currentCount, int totalCount, string detail)
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
                                        progressMeta.Text = currentCount + " of " + totalCount + " steps complete | " + remaining + " remaining";
                                    }
                                    else
                                    {
                                        progressMeta.Text = detail;
                                    }
                                    appendProgress(detail);
                                }));
                            }, delegate { return cancelRequested; }, forceRefreshExistingCovers, out resolved, out coversReady);
                            return new[] { resolved, coversReady };
                        }).ContinueWith(delegate(System.Threading.Tasks.Task<int[]> refreshTask)
                        {
                            libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                            {
                                refreshFinished = true;
                                finishButtons();
                                if (refreshTask.IsCanceled || (refreshTask.IsFaulted && refreshTask.Exception != null && refreshTask.Exception.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException)))
                                {
                                    status.Text = targetFolders.Count == 1 ? "Folder cover refresh cancelled" : "Cover refresh cancelled";
                                    if (progressMeta != null) progressMeta.Text = "Cover refresh cancelled before completion.";
                                    appendProgress("Cover refresh cancelled.");
                                }
                                else if (refreshTask.IsFaulted)
                                {
                                    status.Text = targetFolders.Count == 1 ? "Folder cover refresh failed" : "Cover refresh failed";
                                    var flattened = refreshTask.Exception == null ? null : refreshTask.Exception.Flatten();
                                    var refreshError = flattened == null ? new Exception("Cover refresh failed.") : flattened.InnerExceptions.First();
                                    if (progressMeta != null) progressMeta.Text = refreshError.Message;
                                    appendProgress("ERROR: " + refreshError.Message);
                                    Log(refreshError.ToString());
                                    MessageBox.Show(refreshError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                                else
                                {
                                    var resolved = refreshTask.Result == null || refreshTask.Result.Length < 1 ? 0 : refreshTask.Result[0];
                                    var coversReady = refreshTask.Result == null || refreshTask.Result.Length < 2 ? 0 : refreshTask.Result[1];
                                    status.Text = targetFolders.Count == 1 ? "Folder cover refresh complete" : "Cover refresh complete";
                                    if (progressMeta != null) progressMeta.Text += " | complete";
                                    appendProgress("Cover refresh finished successfully.");
                                    renderTiles(false);
                                    Log((targetFolders.Count == 1 ? "Folder" : "Library") + " cover art refresh complete for " + resolvedScopeLabel + ". Resolved " + resolved + " external ID entr" + (resolved == 1 ? "y" : "ies") + "; " + coversReady + " title" + (coversReady == 1 ? " has" : "s have") + " cover art ready.");
                                }
                                if (actionButton != null)
                                {
                                    actionButton.IsEnabled = true;
                                    actionButton.Content = "Close";
                                }
                            }));
                        });
                    }
                    catch (Exception ex)
                    {
                        refreshFinished = true;
                        finishButtons();
                        status.Text = "Cover refresh failed";
                        Log(ex.ToString());
                        if (progressMeta != null) progressMeta.Text = ex.Message;
                        appendProgress("ERROR: " + ex.Message);
                        if (actionButton != null)
                        {
                            actionButton.IsEnabled = true;
                            actionButton.Content = "Close";
                        }
                        MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                Action runCoverRefresh = delegate
                {
                    runScopedCoverRefresh(folders, "library", false);
                };
                applySearchFilter = delegate
                {
                    searchDebounceTimer.Stop();
                    renderTiles(false);
                };
                searchDebounceTimer.Tick += delegate
                {
                    applySearchFilter();
                };

                refreshButton.Click += delegate { runLibraryScan(null, false); };
                rebuildLibraryButton.Click += delegate { runLibraryScan(null, true); };
                settingsButton.Click += delegate { ShowSettingsWindow(); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                gameIndexButton.Click += delegate { OpenGameIndexEditor(); };
                photoIndexButton.Click += delegate { OpenPhotoIndexEditor(); };
                importButton.Click += delegate { RunWorkflow(false); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                importCommentsButton.Click += delegate { RunWorkflow(true); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                manualImportButton.Click += delegate { OpenManualIntakeWindow(); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
                fetchButton.Click += delegate { runCoverRefresh(); };
                intakeReviewButton.Click += delegate
                {
                    ShowIntakePreviewWindow(false);
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };
                openFolderButton.Click += delegate { if (current != null) OpenFolder(current.FolderPath); };
                scanFolderButton.Click += delegate
                {
                    if (current == null)
                    {
                        MessageBox.Show("Choose a library folder first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    runLibraryScan(current.FolderPath, true);
                };
                openLibraryMetadataEditor = delegate(LibraryFolderInfo focusFolder)
                {
                    if (focusFolder == null)
                    {
                        MessageBox.Show("Choose a library folder first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    showFolder(focusFolder);
                    var focusFolderPath = focusFolder.FolderPath;
                    var focusPlatformLabel = focusFolder.PlatformLabel;
                    var focusName = focusFolder.Name;
                    RunLibraryMetadataEdit(focusFolder, delegate
                    {
                        current = string.IsNullOrWhiteSpace(focusFolderPath) ? null : new LibraryFolderInfo { FolderPath = focusFolderPath, PlatformLabel = focusPlatformLabel ?? string.Empty, Name = focusName ?? string.Empty };
                        folders = LoadLibraryFoldersCached(libraryRoot, false);
                        renderTiles(false);
                    });
                };
                editMetadataButton.Click += delegate { openSelectedLibraryMetadataEditor(); };
                deleteSelectedButton.Click += delegate { deleteSelectedLibraryFiles(); };
                thumbSizeSlider.ValueChanged += delegate
                {
                    if (current == null) return;
                    preservedDetailScrollOffset = thumbScroll.VerticalOffset;
                    preserveDetailScrollOnNextRender = preservedDetailScrollOffset > 0.1d;
                    renderSelectedFolder();
                };
                thumbScroll.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
                {
                    if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 8)
                    {
                        if (current != null)
                        {
                            preservedDetailScrollOffset = thumbScroll.VerticalOffset;
                            preserveDetailScrollOnNextRender = preservedDetailScrollOffset > 0.1d;
                            renderSelectedFolder();
                        }
                    }
                };
                tileScroll.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
                {
                    if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 8)
                    {
                        preservedFolderScrollOffset = tileScroll.VerticalOffset;
                        preserveFolderScrollOnNextRender = preservedFolderScrollOffset > 0.1d;
                        renderTiles(false);
                    }
                };
                folderTileSizeSlider.ValueChanged += delegate
                {
                    libraryFolderTileSize = NormalizeLibraryFolderTileSize((int)Math.Round(folderTileSizeSlider.Value));
                    if (folderTileSizeValue != null) folderTileSizeValue.Text = libraryFolderTileSize.ToString();
                    SaveSettings();
                    preservedFolderScrollOffset = tileScroll.VerticalOffset;
                    preserveFolderScrollOnNextRender = preservedFolderScrollOffset > 0.1d;
                    renderTiles(false);
                };
                sortModeBox.SelectionChanged += delegate
                {
                    var selectedMode = NormalizeLibraryFolderSortMode(sortModeBox.SelectedItem == null ? string.Empty : sortModeBox.SelectedItem.ToString());
                    if (string.Equals(selectedMode, NormalizeLibraryFolderSortMode(libraryFolderSortMode), StringComparison.OrdinalIgnoreCase)) return;
                    libraryFolderSortMode = selectedMode;
                    SaveSettings();
                    renderTiles(false);
                };
                searchBox.TextChanged += delegate
                {
                    searchDebounceTimer.Stop();
                    searchDebounceTimer.Start();
                };
                searchBox.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key != System.Windows.Input.Key.Enter) return;
                    applySearchFilter();
                    e.Handled = true;
                };
                searchBox.LostKeyboardFocus += delegate
                {
                    if (searchDebounceTimer.IsEnabled) applySearchFilter();
                };
                libraryWindow.Activated += delegate
                {
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };

                renderTiles(false);
                if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                if (!reuseMainWindow) libraryWindow.Show();
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        void ShowPhotographyGallery(Window owner)
        {
            try
            {
                EnsureDir(libraryRoot, "Library folder");
                EnsureExifTool();
                status.Text = "Loading photography gallery";
                var files = GetTaggedImagesCached(libraryRoot, false, GamePhotographyTag, "Photography");
                var galleryWindow = new Window
                {
                    Title = "PixelVault " + AppVersion + " Photography",
                    Width = 1320,
                    Height = 900,
                    MinWidth = 1080,
                    MinHeight = 760,
                    Owner = owner ?? this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#080A0D")
                };

                var root = new Grid { Margin = new Thickness(24), Background = Brushes.White };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border { Background = Brush("#11161B"), CornerRadius = new CornerRadius(18), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 18), BorderBrush = Brush("#273039"), BorderThickness = new Thickness(1) };
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var headerStack = new StackPanel();
                var galleryTitle = new TextBlock { Text = GamePhotographyTag, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4") };
                var galleryMeta = new TextBlock { Text = string.Empty, Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B8B2A7"), FontSize = 14, TextWrapping = TextWrapping.Wrap };
                headerStack.Children.Add(galleryTitle);
                headerStack.Children.Add(galleryMeta);
                headerGrid.Children.Add(headerStack);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var openLibraryButton = Btn("Open Library", null, "#1B232B", Brushes.White);
                openLibraryButton.Margin = new Thickness(12, 0, 0, 0);
                var refreshGalleryButton = Btn("Refresh", null, "#275D47", Brushes.White);
                refreshGalleryButton.Margin = new Thickness(12, 0, 0, 0);
                actions.Children.Add(openLibraryButton);
                actions.Children.Add(refreshGalleryButton);
                Grid.SetColumn(actions, 1);
                headerGrid.Children.Add(actions);
                header.Child = headerGrid;
                root.Children.Add(header);

                var body = new Border { Background = Brush("#0D1115"), CornerRadius = new CornerRadius(18), Padding = new Thickness(22), BorderBrush = Brush("#20272F"), BorderThickness = new Thickness(1) };
                Grid.SetRow(body, 1);
                var bodyGrid = new Grid();
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
                var thumbLabel = new TextBlock { Text = "Curated gallery", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4"), VerticalAlignment = VerticalAlignment.Center };
                controls.Children.Add(thumbLabel);
                var sliderPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var sliderLabel = new TextBlock { Text = "Frame width", Foreground = Brush("#B8B2A7"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var sizeValue = new TextBlock { Text = "600", Foreground = Brush("#B8B2A7"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 38 };
                var sizeSlider = new Slider { Minimum = 440, Maximum = 840, Value = 600, Width = 180, TickFrequency = 20, IsSnapToTickEnabled = true };
                sliderPanel.Children.Add(sliderLabel);
                sliderPanel.Children.Add(sizeSlider);
                sliderPanel.Children.Add(sizeValue);
                DockPanel.SetDock(sliderPanel, Dock.Right);
                controls.Children.Add(sliderPanel);
                bodyGrid.Children.Add(controls);
                var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#0D1115") };
                var panel = new WrapPanel { Orientation = Orientation.Horizontal };
                scroll.Content = panel;
                Grid.SetRow(scroll, 1);
                bodyGrid.Children.Add(scroll);
                body.Child = bodyGrid;
                root.Children.Add(body);
                galleryWindow.Content = root;

                Action render = delegate
                {
                    panel.Children.Clear();
                    var ordered = files.OrderByDescending(GetLibraryDate).ThenBy(Path.GetFileName).ToList();
                    galleryMeta.Text = ordered.Count + " capture(s) tagged for game photography in " + libraryRoot;
                    sizeValue.Text = ((int)sizeSlider.Value).ToString();
                    if (ordered.Count == 0)
                    {
                        panel.Children.Add(new TextBlock { Text = "No " + GamePhotographyTag + " captures found yet.", Foreground = Brush("#B8B2A7"), FontSize = 15, Margin = new Thickness(8) });
                        return;
                    }
                    var width = (int)sizeSlider.Value;
                    foreach (var file in ordered)
                    {
                        var tile = new Border { Width = width, Margin = new Thickness(0, 0, 18, 22), Background = Brush("#12181E"), CornerRadius = new CornerRadius(10), BorderBrush = Brush("#262F38"), BorderThickness = new Thickness(1), Tag = file };
                        var tileStack = new StackPanel();
                        var frame = new Border { Background = Brush("#050607"), Margin = new Thickness(14, 14, 14, 10), Padding = new Thickness(10), CornerRadius = new CornerRadius(4) };
                        var presenter = new Grid();
                        var placeholder = new TextBlock { Text = Path.GetFileName(file), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), Foreground = Brush("#F5EFE4"), TextAlignment = TextAlignment.Center };
                        var image = new Image { Width = width - 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
                        presenter.Children.Add(placeholder);
                        presenter.Children.Add(image);
                        frame.Child = presenter;
                        QueueImageLoad(image, file, width * 2, delegate(BitmapImage loaded)
                        {
                            image.Source = loaded;
                            image.Visibility = Visibility.Visible;
                            placeholder.Visibility = Visibility.Collapsed;
                        });
                        tileStack.Children.Add(frame);
                        tileStack.Children.Add(new TextBlock { Text = Path.GetFileName(Path.GetDirectoryName(file)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 14, 14), Foreground = Brush("#F5EFE4"), FontWeight = FontWeights.SemiBold, FontSize = 16, TextAlignment = TextAlignment.Center });
                        tile.Child = tileStack;
                        tile.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
                        {
                            if (e.ClickCount >= 2)
                            {
                                var clicked = sender as Border;
                                if (clicked != null && clicked.Tag is string) OpenWithShell((string)clicked.Tag);
                            }
                        };
                        panel.Children.Add(tile);
                    }
                };

                refreshGalleryButton.Click += delegate
                {
                    status.Text = "Refreshing photography gallery";
                    files = GetTaggedImagesCached(libraryRoot, true, GamePhotographyTag, "Photography");
                    render();
                    status.Text = "Photography gallery ready";
                };
                openLibraryButton.Click += delegate { OpenFolder(libraryRoot); };
                sizeSlider.ValueChanged += delegate { render(); };

                render();
                galleryWindow.Show();
                status.Text = "Photography gallery ready";
            }
            catch (Exception ex)
            {
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        List<LibraryFolderInfo> LoadLibraryFolderCache(string root, string stamp)
        {
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(lines[1], stamp, StringComparison.Ordinal)) return null;
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
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
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
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
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
                            ? parts[5].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
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
            var stamp = Directory.GetLastWriteTimeUtc(folderPath).Ticks;
            List<string> cached;
            long cachedStamp;
            if (folderImageCache.TryGetValue(folderPath, out cached) && folderImageCacheStamp.TryGetValue(folderPath, out cachedStamp) && cachedStamp == stamp)
            {
                return cached;
            }
            var fresh = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly).Where(IsImage).ToList();
            folderImageCache[folderPath] = fresh;
            folderImageCacheStamp[folderPath] = stamp;
            return fresh;
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

        TimeoutWebClient CreateSteamWebClient()
        {
            return new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = SteamRequestTimeoutMilliseconds
            };
        }

        TimeoutWebClient CreateSteamGridDbWebClient()
        {
            var token = CurrentSteamGridDbApiToken();
            if (string.IsNullOrWhiteSpace(token)) return null;
            var client = new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = SteamRequestTimeoutMilliseconds
            };
            client.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            client.Headers[HttpRequestHeader.Accept] = "application/json";
            client.Headers[HttpRequestHeader.UserAgent] = "PixelVault/" + AppVersion;
            return client;
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

        string TryResolveSteamGridDbIdBySteamAppId(string steamAppId)
        {
            return coverService.TryResolveSteamGridDbIdBySteamAppId(steamAppId);
        }

        string TryResolveSteamGridDbIdByName(string title)
        {
            return coverService.TryResolveSteamGridDbIdByName(title);
        }

        string ResolveBestLibraryFolderSteamGridDbId(string root, LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name) || !HasSteamGridDbApiToken()) return string.Empty;
            if (folder.SuppressSteamGridDbIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId)) return folder.SteamGridDbId;
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
            var steamGridDbId = TryResolveSteamGridDbIdByName(folder.Name);
            if (string.IsNullOrWhiteSpace(steamGridDbId) && ShouldUseSteamStoreLookups(folder))
            {
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder);
                steamGridDbId = !string.IsNullOrWhiteSpace(appId)
                    ? TryResolveSteamGridDbIdBySteamAppId(appId)
                    : null;
            }
            if (!string.IsNullOrWhiteSpace(steamGridDbId))
            {
                folder.SteamGridDbId = steamGridDbId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamGridDbId ?? string.Empty;
        }

        string ResolveBestLibraryFolderSteamAppId(string root, LibraryFolderInfo folder, bool allowLookup = true)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name)) return string.Empty;
            if (folder.SuppressSteamAppIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) return folder.SteamAppId;
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
            if (string.IsNullOrWhiteSpace(appId)) appId = TryResolveSteamAppId(folder.Name);
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
            return string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase)
                || string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase);
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
            SaveLibraryFolderCache(root, BuildLibraryFolderInventoryStamp(root), folders);
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

        string ForceRefreshLibraryArt(LibraryFolderInfo folder)
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

                DeleteCachedCover(folder.Name);
                var steamGridDbDownloaded = TryDownloadSteamGridDbCover(folder);
                if (!string.IsNullOrWhiteSpace(steamGridDbDownloaded) && File.Exists(steamGridDbDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamGridDbDownloaded;
                }

                var steamDownloaded = TryDownloadSteamCover(folder);
                if (!string.IsNullOrWhiteSpace(steamDownloaded) && File.Exists(steamDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamDownloaded;
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

        void RefreshLibraryCovers(string root, List<LibraryFolderInfo> folders, Action<int, int, string> progress, Func<bool> isCancellationRequested, bool forceRefreshExistingCovers, out int resolvedIds, out int coversReady)
        {
            resolvedIds = 0;
            coversReady = 0;
            var targetFolders = (folders ?? new List<LibraryFolderInfo>())
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
                if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Cover refresh cancelled.");
                var folder = targetFolders[i];
                var itemLabel = "Game " + (i + 1) + " of " + targetFolders.Count + " | " + folder.Name;
                var hadAppId = !string.IsNullOrWhiteSpace(folder.SteamAppId);
                var hadSteamGridDbId = !string.IsNullOrWhiteSpace(folder.SteamGridDbId);
                var steamGridDbId = ResolveBestLibraryFolderSteamGridDbId(root, folder);
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder, string.IsNullOrWhiteSpace(steamGridDbId));
                if ((!hadAppId && !string.IsNullOrWhiteSpace(appId)) || (!hadSteamGridDbId && !string.IsNullOrWhiteSpace(steamGridDbId)))
                {
                    resolvedIds++;
                    var matchKey = BuildLibraryFolderMasterKey(folder);
                    foreach (var match in folders.Where(entry => entry != null && string.Equals(BuildLibraryFolderMasterKey(entry), matchKey, StringComparison.OrdinalIgnoreCase)))
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
                if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Cover refresh cancelled.");
                var hasCustomCover = !string.IsNullOrWhiteSpace(CustomCoverPath(folder));
                var hadCachedCover = CachedCoverPath(folder.Name) != null;
                var coverReady = hasCustomCover || hadCachedCover;
                var coverDetail = coverReady ? "cover already present" : "cover missing";
                if (forceRefreshExistingCovers && hadCachedCover && !hasCustomCover)
                {
                    var refreshedCover = ForceRefreshLibraryArt(folder);
                    coverReady = !string.IsNullOrWhiteSpace(refreshedCover) && File.Exists(refreshedCover);
                    coverDetail = coverReady ? "cover refreshed" : "cover refresh not available";
                }
                else if (forceRefreshExistingCovers && hasCustomCover)
                {
                    coverDetail = "custom cover preserved";
                }
                else if (!coverReady)
                {
                    ResolveLibraryArt(folder, true);
                    coverReady = HasDedicatedLibraryCover(folder);
                    coverDetail = coverReady ? "cover ready" : "cover not available";
                }
                if (coverReady) coversReady++;
                completed++;
                if (progress != null) progress(completed, totalWork, itemLabel + " | " + coverDetail);
            }
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null || cached.Count == 0)
            {
                SaveLibraryFolderCache(root, stamp, folders);
                return;
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
                if (!string.IsNullOrWhiteSpace(updated.SteamGridDbId)) match.SteamGridDbId = updated.SteamGridDbId;
            }
            SaveLibraryFolderCache(root, stamp, cached);
        }

        string ResolveLibraryArt(LibraryFolderInfo folder, bool allowDownload)
        {
            var custom = CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            var cached = CachedCoverPath(folder.Name);
            if (cached != null) return cached;
            if (allowDownload)
            {
                var steamGridDbDownloaded = TryDownloadSteamGridDbCover(folder);
                if (steamGridDbDownloaded != null) return steamGridDbDownloaded;
                var downloaded = TryDownloadSteamCover(folder);
                if (downloaded != null) return downloaded;
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

        string TryDownloadSteamCover(LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            try
            {
                var appId = ResolveBestLibraryFolderSteamAppId(libraryRoot, folder, string.IsNullOrWhiteSpace(folder.SteamGridDbId));
                var downloaded = coverService.TryDownloadSteamCover(folder.Name, appId);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                    return downloaded;
                }
            }
            catch { }
            return null;
        }

        string TryDownloadSteamGridDbCover(LibraryFolderInfo folder)
        {
            if (folder == null || !HasSteamGridDbApiToken()) return null;
            try
            {
                var steamGridDbId = ResolveBestLibraryFolderSteamGridDbId(libraryRoot, folder);
                var downloaded = coverService.TryDownloadSteamGridDbCover(folder.Name, steamGridDbId);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                    return downloaded;
                }
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
            SaveLibraryFolderCache(root, stamp, cached);
            UpsertSavedGameIndexRow(root, folder);
        }

        string TryResolveSteamAppId(string title)
        {
            return coverService.TryResolveSteamAppId(title);
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

        string SteamName(string appId)
        {
            return coverService.SteamName(appId);
        }

        string PrimaryPlatformLabel(string file)
        {
            var tags = DetectPlatformTags(file);
            return tags.FirstOrDefault(t => t == "Xbox" || t == "Steam" || t == "PS5" || t == "PlayStation" || t == "PC") ?? "Other";
        }

        string FilenameGuessLabel(string file)
        {
            var label = PrimaryPlatformLabel(file);
            return string.Equals(label, "Other", StringComparison.OrdinalIgnoreCase) ? "No confident match" : label;
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
        static string CleanTag(string s) { return string.IsNullOrWhiteSpace(s) ? string.Empty : Regex.Replace(s, "\\s+", " ").Trim(); }
        static string[] ParseTagText(string s) { return (s ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(CleanTag).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
        static bool SameManualText(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        static string Unique(string path) { if (!File.Exists(path)) return path; var dir = Path.GetDirectoryName(path); var name = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path); int i = 2; string candidate; do { candidate = Path.Combine(dir, name + " (" + i + ")" + ext); i++; } while (File.Exists(candidate)); return candidate; }
        static void EnsureDir(string path, string label) { if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new InvalidOperationException(label + " not found: " + path); }
        static bool IsImage(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp"; }
        static bool IsPngOrJpeg(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg"; }
        static bool IsVideo(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".mp4" || e == ".mkv" || e == ".avi" || e == ".mov" || e == ".wmv" || e == ".webm"; }
        static bool IsMedia(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return new[] { ".jpg", ".jpeg", ".png", ".webp", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" }.Contains(e); }
        static string Quote(string s) { return s.Contains(" ") ? "\"" + s.Replace("\"", "\\\"") + "\"" : s; }
        static DateTime GetLibraryDate(string file)
        {
            var name = Path.GetFileName(file);
            var tags = DetectPlatformTags(name);
            if (!tags.Contains("Xbox"))
            {
                var parsed = ParseCaptureDate(name);
                if (parsed.HasValue) return parsed.Value;
            }
            var created = File.GetCreationTime(file);
            var modified = File.GetLastWriteTime(file);
            if (created == DateTime.MinValue) return modified;
            if (modified == DateTime.MinValue) return created;
            return created < modified ? created : modified;
        }

        static DateTime? ParseCaptureDate(string file)
        {
            DateTime d;
            var a = Regex.Match(file, @"_(\d{14})(?:_|(?=\.[^.]+$))");
            if (a.Success && DateTime.TryParseExact(a.Groups[1].Value, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out d)) return d;
            var steamLegacy = Regex.Match(file, @"_(\d{4})-(\d{2})-(\d{2})(?:_|(?=\.[^.]+$))");
            if (steamLegacy.Success)
            {
                var raw = steamLegacy.Groups[1].Value + steamLegacy.Groups[2].Value + steamLegacy.Groups[3].Value;
                if (DateTime.TryParseExact(raw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out d)) return d;
            }
            var b = Regex.Match(file, @"-(\d{4})_(\d{2})_(\d{2})[-_](\d{2})[-_](\d{2})[-_](\d{2})");
            if (b.Success)
            {
                var raw = b.Groups[1].Value + b.Groups[2].Value + b.Groups[3].Value + b.Groups[4].Value + b.Groups[5].Value + b.Groups[6].Value;
                if (DateTime.TryParseExact(raw, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out d)) return d;
            }
            var steamClipUnix = Regex.Match(file, @"^clip_(?<stamp>[\d,]{13,17})(?=\.[^.]+$)", RegexOptions.IgnoreCase);
            if (steamClipUnix.Success)
            {
                long unixMilliseconds;
                var rawStamp = steamClipUnix.Groups["stamp"].Value.Replace(",", string.Empty);
                if (long.TryParse(rawStamp, out unixMilliseconds))
                {
                    try
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToLocalTime().DateTime;
                    }
                    catch
                    {
                    }
                }
            }
            return null;
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
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
            image.UriSource = new Uri(path);
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
                    && Regex.IsMatch(currentDir.Name, @"^PixelVault-\d+\.\d+$", RegexOptions.IgnoreCase)
                    && currentDir.Parent != null
                    && string.Equals(currentDir.Parent.Name, "dist", StringComparison.OrdinalIgnoreCase)
                    && currentDir.Parent.Parent != null)
                {
                    return Path.Combine(currentDir.Parent.Parent.FullName, "PixelVaultData");
                }
            }
            catch { }
            return currentAppRoot;
        }

        void MigratePersistentDataFromLegacyVersions()
        {
            if (string.Equals(dataRoot, appRoot, StringComparison.OrdinalIgnoreCase)) return;
            CopyIfMissing(Path.Combine(appRoot, "PixelVault.settings.ini"), settingsPath);
            CopyDirectoryContentsIfMissing(Path.Combine(appRoot, "cache"), cacheRoot);
            CopyDirectoryContentsIfMissing(Path.Combine(appRoot, "logs"), logsRoot);
            var currentDir = new DirectoryInfo(appRoot);
            var distDir = currentDir == null ? null : currentDir.Parent;
            if (distDir == null || !distDir.Exists) return;
            foreach (var dir in distDir.GetDirectories("PixelVault-*").OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(dir.FullName.TrimEnd(Path.DirectorySeparatorChar), appRoot, StringComparison.OrdinalIgnoreCase)) continue;
                CopyIfMissing(Path.Combine(dir.FullName, "PixelVault.settings.ini"), settingsPath);
                CopyDirectoryContentsIfMissing(Path.Combine(dir.FullName, "cache"), cacheRoot);
                CopyDirectoryContentsIfMissing(Path.Combine(dir.FullName, "logs"), logsRoot);
            }
        }

        void CopyIfMissing(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath) || File.Exists(destinationPath)) return;
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory)) Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourcePath, destinationPath, false);
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
                status.Text = "Loading photo index";
                var allRows = LoadPhotoIndexEditorRows(libraryRoot);
                var editorWindow = new Window
                {
                    Title = "PixelVault " + AppVersion + " Photo Index",
                    Width = 1500,
                    Height = 900,
                    MinWidth = 1180,
                    MinHeight = 760,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#F3EEE4")
                };

                var root = new Grid { Margin = new Thickness(24) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(20), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 18) };
                var headerStack = new StackPanel();
                headerStack.Children.Add(new TextBlock { Text = "Photo Index", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                headerStack.Children.Add(new TextBlock { Text = "This is the per-file index cache for the library. Edit Game ID, Console, or Tags here when you need to correct the recorded state for individual files.", Margin = new Thickness(0, 8, 0, 0), FontSize = 14, Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap });
                headerStack.Children.Add(new TextBlock { Text = "Saving here rewrites the photo-level index and rebuilds the grouped library from it. A later scan or rebuild can resync these values from the files themselves.", Margin = new Thickness(0, 10, 0, 0), FontSize = 13, Foreground = Brush("#D8C7A4"), TextWrapping = TextWrapping.Wrap });
                header.Child = headerStack;
                root.Children.Add(header);

                var body = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
                Grid.SetRow(body, 1);
                root.Children.Add(body);

                var bodyGrid = new Grid();
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.Child = bodyGrid;

                var controlGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.Children.Add(new TextBlock { Text = "Search", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
                var searchBox = new TextBox { Padding = new Thickness(10, 6, 10, 6), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 0, 14, 0) };
                Grid.SetColumn(searchBox, 1);
                controlGrid.Children.Add(searchBox);
                var helperText = new TextBlock { Text = "Edit the recorded Game ID, Console, and Tags for each indexed file. File path and metadata stamp stay read-only.", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#5F6970"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) };
                Grid.SetColumn(helperText, 2);
                controlGrid.Children.Add(helperText);
                var pullFromFileButton = Btn("Pull From File", null, "#275D47", Brushes.White);
                pullFromFileButton.Width = 150;
                pullFromFileButton.Height = 42;
                pullFromFileButton.Margin = new Thickness(0, 0, 10, 0);
                pullFromFileButton.IsEnabled = false;
                Grid.SetColumn(pullFromFileButton, 3);
                controlGrid.Children.Add(pullFromFileButton);
                var deleteRowButton = Btn("Forget Row", null, "#A3473E", Brushes.White);
                deleteRowButton.Width = 134;
                deleteRowButton.Height = 42;
                deleteRowButton.Margin = new Thickness(0, 0, 10, 0);
                deleteRowButton.IsEnabled = false;
                Grid.SetColumn(deleteRowButton, 4);
                controlGrid.Children.Add(deleteRowButton);
                var reloadButton = Btn("Reload", null, "#EEF2F5", Brush("#33424D"));
                reloadButton.Width = 132;
                reloadButton.Height = 42;
                reloadButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(reloadButton, 5);
                controlGrid.Children.Add(reloadButton);
                var openFolderButton = Btn("Open Folder", null, "#20343A", Brushes.White);
                openFolderButton.Width = 148;
                openFolderButton.Height = 42;
                openFolderButton.Margin = new Thickness(0, 0, 10, 0);
                openFolderButton.IsEnabled = false;
                Grid.SetColumn(openFolderButton, 6);
                controlGrid.Children.Add(openFolderButton);
                var openFileButton = Btn("Open File", null, "#20343A", Brushes.White);
                openFileButton.Width = 132;
                openFileButton.Height = 42;
                openFileButton.Margin = new Thickness(0);
                openFileButton.IsEnabled = false;
                Grid.SetColumn(openFileButton, 7);
                controlGrid.Children.Add(openFileButton);
                bodyGrid.Children.Add(controlGrid);

                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    CanUserResizeRows = false,
                    SelectionMode = DataGridSelectionMode.Extended,
                    SelectionUnit = DataGridSelectionUnit.FullRow,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brush("#D7E1E8"),
                    Background = Brushes.White,
                    AlternatingRowBackground = Brush("#F7FAFC"),
                    RowHeaderWidth = 0,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "Game ID", Binding = new System.Windows.Data.Binding("GameId") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 110 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Console", Binding = new System.Windows.Data.Binding("ConsoleLabel") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 120 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Tags", Binding = new System.Windows.Data.Binding("TagText") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(1.05, DataGridLengthUnitType.Star) });
                grid.Columns.Add(new DataGridTextColumn { Header = "File", Binding = new System.Windows.Data.Binding("FilePath"), IsReadOnly = true, Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
                grid.Columns.Add(new DataGridTextColumn { Header = "Stamp", Binding = new System.Windows.Data.Binding("Stamp"), IsReadOnly = true, Width = 170 });
                Grid.SetRow(grid, 1);
                bodyGrid.Children.Add(grid);

                var footerGrid = new Grid();
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var statusText = new TextBlock { Foreground = Brush("#5F6970"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                footerGrid.Children.Add(statusText);
                var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var closeButton = Btn("Close", null, "#EEF2F5", Brush("#33424D"));
                closeButton.Width = 128;
                closeButton.Height = 44;
                closeButton.Margin = new Thickness(0, 0, 10, 0);
                var saveButton = Btn("Save Index", null, "#275D47", Brushes.White);
                saveButton.Width = 148;
                saveButton.Height = 44;
                saveButton.Margin = new Thickness(0);
                actionRow.Children.Add(closeButton);
                actionRow.Children.Add(saveButton);
                Grid.SetColumn(actionRow, 1);
                footerGrid.Children.Add(actionRow);
                Grid.SetRow(footerGrid, 2);
                bodyGrid.Children.Add(footerGrid);

                photoIndexEditorWindow = editorWindow;
                editorWindow.Content = root;

                bool dirty = false;
                Action refreshStatus = null;
                Action refreshGrid = null;
                Action<IEnumerable<string>> reselection = null;

                Func<List<PhotoIndexEditorRow>> selectedRows = delegate
                {
                    return grid.SelectedItems.Cast<object>()
                        .OfType<PhotoIndexEditorRow>()
                        .Where(row => row != null)
                        .GroupBy(row => row.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .ToList();
                };

                refreshStatus = delegate
                {
                    var selectedItems = selectedRows();
                    var selected = selectedItems.FirstOrDefault();
                    var selectedFile = selected == null ? string.Empty : selected.FilePath ?? string.Empty;
                    var selectedFolder = string.IsNullOrWhiteSpace(selectedFile) ? string.Empty : (Path.GetDirectoryName(selectedFile) ?? string.Empty);
                    openFileButton.IsEnabled = selectedItems.Count == 1 && !string.IsNullOrWhiteSpace(selectedFile) && File.Exists(selectedFile);
                    openFolderButton.IsEnabled = selectedItems.Count == 1 && !string.IsNullOrWhiteSpace(selectedFolder) && Directory.Exists(selectedFolder);
                    pullFromFileButton.IsEnabled = selectedItems.Any(row => !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath));
                    deleteRowButton.IsEnabled = selectedItems.Count > 0;
                    statusText.Text = grid.Items.Count + " visible row(s) | " + allRows.Count + " total | " + (dirty ? "Unsaved changes" : "Saved") + " | " + (selectedItems.Count == 0 ? "No row selected" : selectedItems.Count + " selected");
                };

                refreshGrid = delegate
                {
                    var query = (searchBox.Text ?? string.Empty).Trim();
                    IEnumerable<PhotoIndexEditorRow> rows = allRows;
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        rows = rows.Where(row =>
                            (!string.IsNullOrWhiteSpace(row.GameId) && row.GameId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.ConsoleLabel) && row.ConsoleLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.TagText) && row.TagText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.FilePath) && row.FilePath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                    }
                    grid.ItemsSource = rows.OrderBy(row => row.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
                    refreshStatus();
                };

                reselection = delegate(IEnumerable<string> filePaths)
                {
                    var wanted = new HashSet<string>((filePaths ?? Enumerable.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path)), StringComparer.OrdinalIgnoreCase);
                    if (wanted.Count == 0) return;
                    grid.SelectedItems.Clear();
                    foreach (var row in grid.Items.Cast<object>().OfType<PhotoIndexEditorRow>())
                    {
                        if (!wanted.Contains(row.FilePath ?? string.Empty)) continue;
                        grid.SelectedItems.Add(row);
                    }
                    refreshStatus();
                };

                Action reloadRows = delegate
                {
                    allRows = LoadPhotoIndexEditorRows(libraryRoot);
                    dirty = false;
                    refreshGrid();
                    status.Text = "Photo index reloaded";
                    Log("Reloaded photo index editor rows from cache.");
                };

                searchBox.TextChanged += delegate { refreshGrid(); };
                grid.SelectionChanged += delegate { refreshStatus(); };
                grid.CellEditEnding += delegate { dirty = true; refreshStatus(); };
                pullFromFileButton.Click += delegate
                {
                    var selectedItems = selectedRows()
                        .Where(row => !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath))
                        .ToList();
                    if (selectedItems.Count == 0) return;
                    foreach (var selected in selectedItems)
                    {
                        var tags = ReadEmbeddedKeywordTagsDirect(selected.FilePath);
                        selected.ConsoleLabel = DetermineConsoleLabelFromTags(tags);
                        selected.TagText = string.Join(", ", tags);
                        selected.Stamp = BuildLibraryMetadataStamp(selected.FilePath);
                    }
                    dirty = true;
                    grid.Items.Refresh();
                    refreshStatus();
                    status.Text = "Photo index refreshed from " + selectedItems.Count + " file(s)";
                };
                deleteRowButton.Click += delegate
                {
                    var selectedItems = selectedRows();
                    if (selectedItems.Count == 0) return;
                    var choice = MessageBox.Show("Forget " + selectedItems.Count + " selected row(s) from the photo index?\n\nThis does not delete the file itself. If the file is still in the library, PixelVault can add it back on refresh or rebuild.", "Forget Photo Index Row", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.OK) return;
                    foreach (var selected in selectedItems) allRows.Remove(selected);
                    dirty = true;
                    refreshGrid();
                    status.Text = "Photo index row(s) forgotten";
                };
                openFolderButton.Click += delegate
                {
                    var selected = grid.SelectedItem as PhotoIndexEditorRow;
                    if (selected != null) OpenFolder(Path.GetDirectoryName(selected.FilePath));
                };
                openFileButton.Click += delegate
                {
                    var selected = grid.SelectedItem as PhotoIndexEditorRow;
                    if (selected != null) OpenWithShell(selected.FilePath);
                };
                reloadButton.Click += delegate
                {
                    var selectedItems = selectedRows()
                        .Where(row => !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath))
                        .ToList();
                    if (selectedItems.Count == 0)
                    {
                        if (dirty)
                        {
                            var choice = MessageBox.Show("Discard unsaved photo index edits and reload the current cache from disk?", "Reload Photo Index", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                            if (choice != MessageBoxResult.OK) return;
                        }
                        reloadRows();
                        return;
                    }
                    if (dirty)
                    {
                        var choice = MessageBox.Show("Reloading from the selected file(s) will discard unsaved photo index edits. Continue?", "Reload Photo Index", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                        if (choice != MessageBoxResult.OK) return;
                    }
                    var selectedPaths = selectedItems.Select(row => row.FilePath).ToList();
                    var diskRows = LoadPhotoIndexEditorRows(libraryRoot);
                    var rowMap = diskRows
                        .Where(row => row != null && !string.IsNullOrWhiteSpace(row.FilePath))
                        .GroupBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
                    int refreshed = 0;
                    foreach (var path in selectedPaths)
                    {
                        PhotoIndexEditorRow target;
                        if (!rowMap.TryGetValue(path, out target) || !File.Exists(path)) continue;
                        var tags = ReadEmbeddedKeywordTagsDirect(path);
                        target.ConsoleLabel = DetermineConsoleLabelFromTags(tags);
                        target.TagText = string.Join(", ", tags);
                        target.Stamp = BuildLibraryMetadataStamp(path);
                        refreshed++;
                    }
                    SavePhotoIndexEditorRows(libraryRoot, diskRows);
                    allRows = LoadPhotoIndexEditorRows(libraryRoot);
                    dirty = false;
                    refreshGrid();
                    reselection(selectedPaths);
                    status.Text = "Reloaded " + refreshed + " photo index row(s) from file";
                    Log("Reloaded " + refreshed + " photo index row(s) from selected file(s).");
                };
                closeButton.Click += delegate
                {
                    if (dirty)
                    {
                        var choice = MessageBox.Show("You have unsaved photo index changes.\n\nClose without saving?", "Close Photo Index", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        if (choice != MessageBoxResult.OK) return;
                    }
                    editorWindow.Close();
                };
                saveButton.Click += delegate
                {
                    try
                    {
                        grid.CommitEdit(DataGridEditingUnit.Cell, true);
                        grid.CommitEdit(DataGridEditingUnit.Row, true);
                        foreach (var row in allRows)
                        {
                            row.GameId = NormalizeGameId(row.GameId);
                            row.ConsoleLabel = CleanTag(row.ConsoleLabel);
                            row.TagText = string.Join(", ", ParseTagText(row.TagText));
                        }
                        SavePhotoIndexEditorRows(libraryRoot, allRows);
                        allRows = LoadPhotoIndexEditorRows(libraryRoot);
                        dirty = false;
                        refreshGrid();
                        status.Text = "Photo index saved";
                        Log("Saved " + allRows.Count + " photo index row(s) to cache.");
                    }
                    catch (Exception saveEx)
                    {
                        status.Text = "Photo index save failed";
                        Log("Failed to save photo index. " + saveEx.Message);
                        MessageBox.Show("Could not save the photo index." + Environment.NewLine + Environment.NewLine + saveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                editorWindow.Closed += delegate
                {
                    if (ReferenceEquals(photoIndexEditorWindow, editorWindow)) photoIndexEditorWindow = null;
                    status.Text = "Ready";
                };

                refreshGrid();
                status.Text = "Photo index ready";
                Log("Opened photo index editor.");
                editorWindow.Show();
                editorWindow.Activate();
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
                status.Text = "Loading game index";
                var allRows = LoadGameIndexEditorRows(libraryRoot);
                var editorWindow = new Window
                {
                    Title = "PixelVault " + AppVersion + " Game Index",
                    Width = 1380,
                    Height = 900,
                    MinWidth = 1120,
                    MinHeight = 760,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#F3EEE4")
                };

                var root = new Grid { Margin = new Thickness(24) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(20), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 18) };
                var headerStack = new StackPanel();
                headerStack.Children.Add(new TextBlock { Text = "Game Index", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                headerStack.Children.Add(new TextBlock { Text = "Use this as the master game record: one row per game and platform, with duplicates merged together and platform variants kept separate.", Margin = new Thickness(0, 8, 0, 0), FontSize = 14, Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap });
                headerStack.Children.Add(new TextBlock { Text = "Saving here updates the master index first, then re-syncs the library cache from it so AppIDs and titles stay consistent.", Margin = new Thickness(0, 10, 0, 0), FontSize = 13, Foreground = Brush("#D8C7A4"), TextWrapping = TextWrapping.Wrap });
                header.Child = headerStack;
                root.Children.Add(header);

                var body = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
                Grid.SetRow(body, 1);
                root.Children.Add(body);

                var bodyGrid = new Grid();
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.Child = bodyGrid;

                var controlGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.Children.Add(new TextBlock { Text = "Search", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
                var searchBox = new TextBox { Padding = new Thickness(10, 6, 10, 6), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 0, 14, 0) };
                Grid.SetColumn(searchBox, 1);
                controlGrid.Children.Add(searchBox);
                var helperText = new TextBlock { Text = "Edit the master Game, Platform, Steam AppID, and STID fields. Game ID stays stable, and folder/file details stay read-only so photo-level assignments drive grouping.", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#5F6970"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) };
                Grid.SetColumn(helperText, 2);
                controlGrid.Children.Add(helperText);
                var addRowButton = Btn("Add Game", null, "#8A5A17", Brushes.White);
                addRowButton.Width = 140;
                addRowButton.Height = 42;
                addRowButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(addRowButton, 3);
                controlGrid.Children.Add(addRowButton);
                var deleteRowButton = Btn("Delete Game", null, "#A3473E", Brushes.White);
                deleteRowButton.Width = 146;
                deleteRowButton.Height = 42;
                deleteRowButton.Margin = new Thickness(0, 0, 10, 0);
                deleteRowButton.IsEnabled = false;
                Grid.SetColumn(deleteRowButton, 4);
                controlGrid.Children.Add(deleteRowButton);
                var resolveIdsButton = Btn("Resolve IDs", null, "#275D47", Brushes.White);
                resolveIdsButton.Width = 160;
                resolveIdsButton.Height = 42;
                resolveIdsButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(resolveIdsButton, 5);
                controlGrid.Children.Add(resolveIdsButton);
                var reloadButton = Btn("Reload", null, "#EEF2F5", Brush("#33424D"));
                reloadButton.Width = 132;
                reloadButton.Height = 42;
                reloadButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(reloadButton, 6);
                controlGrid.Children.Add(reloadButton);
                var openFolderButton = Btn("Open Folder", null, "#20343A", Brushes.White);
                openFolderButton.Width = 148;
                openFolderButton.Height = 42;
                openFolderButton.Margin = new Thickness(0);
                openFolderButton.IsEnabled = false;
                Grid.SetColumn(openFolderButton, 7);
                controlGrid.Children.Add(openFolderButton);
                bodyGrid.Children.Add(controlGrid);

                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    CanUserResizeRows = false,
                    SelectionMode = DataGridSelectionMode.Single,
                    SelectionUnit = DataGridSelectionUnit.FullRow,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brush("#D7E1E8"),
                    Background = Brushes.White,
                    AlternatingRowBackground = Brush("#F7FAFC"),
                    RowHeaderWidth = 0,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "Game ID", Binding = new System.Windows.Data.Binding("GameId"), IsReadOnly = true, Width = 180 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Game", Binding = new System.Windows.Data.Binding("Name") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(1.15, DataGridLengthUnitType.Star) });
                grid.Columns.Add(new DataGridTextColumn { Header = "Platform", Binding = new System.Windows.Data.Binding("PlatformLabel") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 130 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Steam AppID", Binding = new System.Windows.Data.Binding("SteamAppId") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 130 });
                grid.Columns.Add(new DataGridTextColumn { Header = "STID", Binding = new System.Windows.Data.Binding("SteamGridDbId") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 120 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Files", Binding = new System.Windows.Data.Binding("FileCount"), IsReadOnly = true, Width = 74 });
                grid.Columns.Add(new DataGridTextColumn { Header = "Folder", Binding = new System.Windows.Data.Binding("FolderPath"), IsReadOnly = true, Width = new DataGridLength(1.85, DataGridLengthUnitType.Star) });
                Grid.SetRow(grid, 1);
                bodyGrid.Children.Add(grid);

                var footerGrid = new Grid();
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var statusText = new TextBlock { Foreground = Brush("#5F6970"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                footerGrid.Children.Add(statusText);
                var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var closeButton = Btn("Close", null, "#EEF2F5", Brush("#33424D"));
                closeButton.Width = 128;
                closeButton.Height = 44;
                closeButton.Margin = new Thickness(0, 0, 10, 0);
                var saveButton = Btn("Save Index", null, "#275D47", Brushes.White);
                saveButton.Width = 148;
                saveButton.Height = 44;
                saveButton.Margin = new Thickness(0);
                actionRow.Children.Add(closeButton);
                actionRow.Children.Add(saveButton);
                Grid.SetColumn(actionRow, 1);
                footerGrid.Children.Add(actionRow);
                Grid.SetRow(footerGrid, 2);
                bodyGrid.Children.Add(footerGrid);

                gameIndexEditorWindow = editorWindow;
                editorWindow.Content = root;

                bool dirty = false;
                Action refreshStatus = null;
                Action refreshGrid = null;

                refreshStatus = delegate
                {
                    var visibleCount = grid.Items.Count;
                    var selected = grid.SelectedItem as GameIndexEditorRow;
                    openFolderButton.IsEnabled = selected != null && !string.IsNullOrWhiteSpace(selected.FolderPath) && Directory.Exists(selected.FolderPath);
                    deleteRowButton.IsEnabled = selected != null;
                    var selectionText = selected == null ? "No row selected" : selected.Name + " | " + selected.PlatformLabel + " | " + (selected.GameId ?? string.Empty);
                    statusText.Text = visibleCount + " visible row(s) | " + allRows.Count + " total | " + (dirty ? "Unsaved changes" : "Saved") + " | " + selectionText;
                };

                refreshGrid = delegate
                {
                    var query = (searchBox.Text ?? string.Empty).Trim();
                    IEnumerable<GameIndexEditorRow> rows = allRows;
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        rows = rows.Where(row =>
                            (!string.IsNullOrWhiteSpace(row.GameId) && row.GameId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.Name) && row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.PlatformLabel) && row.PlatformLabel.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.SteamAppId) && row.SteamAppId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.SteamGridDbId) && row.SteamGridDbId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(row.FolderPath) && row.FolderPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
                    }
                    grid.ItemsSource = rows
                        .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(row => row.PlatformLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    refreshStatus();
                };

                Action reloadRows = delegate
                {
                    allRows = LoadGameIndexEditorRows(libraryRoot);
                    dirty = false;
                    refreshGrid();
                    status.Text = "Game index reloaded";
                    Log("Reloaded game index editor rows from cache.");
                };

                searchBox.TextChanged += delegate { refreshGrid(); };
                grid.SelectionChanged += delegate { refreshStatus(); };
                grid.CellEditEnding += delegate { dirty = true; refreshStatus(); };
                addRowButton.Click += delegate
                {
                    var newRow = new GameIndexEditorRow
                    {
                        GameId = CreateGameId(allRows.Select(row => row.GameId)),
                        Name = string.Empty,
                        PlatformLabel = "Other",
                        SteamAppId = string.Empty,
                        SteamGridDbId = string.Empty,
                        FileCount = 0,
                        FolderPath = string.Empty,
                        PreviewImagePath = string.Empty,
                        FilePaths = new string[0]
                    };
                    allRows.Add(newRow);
                    dirty = true;
                    refreshGrid();
                    grid.SelectedItem = newRow;
                    grid.ScrollIntoView(newRow);
                    status.Text = "New game row added";
                };
                deleteRowButton.Click += delegate
                {
                    var selected = grid.SelectedItem as GameIndexEditorRow;
                    if (selected == null) return;
                    var choice = MessageBox.Show("Remove the selected row from the game index?\n\nThis only deletes the master record row.", "Delete Game Index Row", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.OK) return;
                    allRows.Remove(selected);
                    dirty = true;
                    refreshGrid();
                    status.Text = "Game index row removed";
                };
                resolveIdsButton.Click += delegate
                {
                    try
                    {
                        grid.CommitEdit(DataGridEditingUnit.Cell, true);
                        grid.CommitEdit(DataGridEditingUnit.Row, true);
                        foreach (var row in allRows)
                        {
                            row.Name = NormalizeGameIndexName(row.Name, row.FolderPath);
                            row.PlatformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.PlatformLabel) ? "Other" : row.PlatformLabel.Trim());
                        }
                        allRows = MergeGameIndexRows(allRows);
                        var rowsToResolve = allRows.ToList();
                        var appIdTargets = rowsToResolve
                            .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                            .GroupBy(BuildGameIndexMergeKey, StringComparer.OrdinalIgnoreCase)
                            .Select(group => group.First())
                            .Count();
                        var steamGridDbTargets = appIdTargets;
                        var totalWork = appIdTargets + steamGridDbTargets;

                        RunBackgroundWorkflowWithProgress(
                            "PixelVault " + AppVersion + " Game Index Resolve Progress",
                            "Resolving external game IDs",
                            "Preparing game index rows...",
                            "Resolving game index IDs",
                            "Starting external game ID resolution for " + rowsToResolve.Count + " row(s).",
                            "Game index resolve failed",
                            totalWork,
                            delegate(Action<int, string> reportProgress)
                            {
                                var appIdOffset = 0;
                                var steamGridDbOffset = appIdTargets;
                                var resolvedAppIds = ResolveMissingGameIndexSteamAppIds(libraryRoot, rowsToResolve, delegate(int current, int total, string detail)
                                {
                                    reportProgress(appIdOffset + current, detail);
                                });
                                var resolvedSteamGridDbIds = ResolveMissingGameIndexSteamGridDbIds(libraryRoot, rowsToResolve, delegate(int current, int total, string detail)
                                {
                                    reportProgress(steamGridDbOffset + current, detail);
                                });
                                reportProgress(totalWork, "Game index ID resolution complete.");
                                return new[] { resolvedAppIds, resolvedSteamGridDbIds };
                            },
                            delegate(int[] result)
                            {
                                var resolvedAppIds = result == null || result.Length < 1 ? 0 : result[0];
                                var resolvedSteamGridDbIds = result == null || result.Length < 2 ? 0 : result[1];
                                allRows = rowsToResolve;
                                dirty = false;
                                refreshGrid();
                                status.Text = "Game index IDs resolved";
                                Log("Resolved " + resolvedAppIds + " Steam AppID entr" + (resolvedAppIds == 1 ? "y" : "ies") + " and " + resolvedSteamGridDbIds + " STID entr" + (resolvedSteamGridDbIds == 1 ? "y" : "ies") + " into the game index.");
                            });
                    }
                    catch (Exception resolveEx)
                    {
                        status.Text = "Game index resolve failed";
                        Log("Failed to resolve external game index IDs. " + resolveEx.Message);
                        MessageBox.Show("Could not resolve external IDs for the game index." + Environment.NewLine + Environment.NewLine + resolveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                openFolderButton.Click += delegate
                {
                    var selected = grid.SelectedItem as GameIndexEditorRow;
                    if (selected != null) OpenFolder(selected.FolderPath);
                };
                reloadButton.Click += delegate
                {
                    if (dirty)
                    {
                        var choice = MessageBox.Show("Discard unsaved index edits and reload the current cache from disk?", "Reload Game Index", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                        if (choice != MessageBoxResult.OK) return;
                    }
                    reloadRows();
                };
                closeButton.Click += delegate
                {
                    if (dirty)
                    {
                        var choice = MessageBox.Show("You have unsaved game index changes.\n\nClose without saving?", "Close Game Index", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        if (choice != MessageBoxResult.OK) return;
                    }
                    editorWindow.Close();
                };
                saveButton.Click += delegate
                {
                    try
                    {
                        grid.CommitEdit(DataGridEditingUnit.Cell, true);
                        grid.CommitEdit(DataGridEditingUnit.Row, true);
                        foreach (var row in allRows)
                        {
                            row.Name = NormalizeGameIndexName(row.Name, row.FolderPath);
                            row.PlatformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.PlatformLabel) ? "Other" : row.PlatformLabel.Trim());
                            row.SteamAppId = (row.SteamAppId ?? string.Empty).Trim();
                            row.SteamGridDbId = (row.SteamGridDbId ?? string.Empty).Trim();
                        }
                        allRows = MergeGameIndexRows(allRows);
                        SaveGameIndexEditorRows(libraryRoot, allRows);
                        dirty = false;
                        refreshGrid();
                        status.Text = "Game index saved";
                        Log("Saved " + allRows.Count + " game index row(s) to cache.");
                    }
                    catch (Exception saveEx)
                    {
                        status.Text = "Game index save failed";
                        Log("Failed to save game index. " + saveEx.Message);
                        MessageBox.Show("Could not save the game index." + Environment.NewLine + Environment.NewLine + saveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                editorWindow.Closed += delegate
                {
                    if (ReferenceEquals(gameIndexEditorWindow, editorWindow)) gameIndexEditorWindow = null;
                    status.Text = "Ready";
                };

                refreshGrid();
                status.Text = "Game index ready";
                Log("Opened game index editor.");
                editorWindow.Show();
                editorWindow.Activate();
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
        void LoadLogView() { if (logBox != null && File.Exists(LogFilePath())) { logBox.Text = File.ReadAllText(LogFilePath()); logBox.ScrollToEnd(); } }
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
            File.AppendAllText(LogFilePath(), line + Environment.NewLine);
        }
    }
}



















































































































