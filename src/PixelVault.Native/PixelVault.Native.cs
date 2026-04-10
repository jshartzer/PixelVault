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
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PixelVault;component/Themes/PixelVaultFocus.xaml", UriKind.Absolute)
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
        const string AppVersion = "0.075.008";
        const string GamePhotographyTag = "Game Photography";
        const string CustomPlatformPrefix = "Platform:";
        const string ClearedExternalIdSentinel = "__PV_CLEARED__";
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
        BitmapSource libraryCompletionBadgeBitmap;
        readonly SemaphoreSlim videoClipInfoWarmLimiter = new SemaphoreSlim(2);
        readonly SemaphoreSlim videoPreviewWarmLimiter = new SemaphoreSlim(1);
        readonly HashSet<string> failedFfmpegPosterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, byte> activeVideoPreviewGenerations = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        readonly ConcurrentDictionary<string, byte> activeVideoInfoGenerations = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, LibraryMetadataIndexEntry> libraryMetadataIndex = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
        readonly object libraryMetadataIndexSync = new object();
        readonly object libraryMaintenanceSync = new object();
        /// <summary>Serializes folder-cache disk reads/writes and full <see cref="LibraryScanner.RebuildLibraryFolderCache"/> without blocking cache hits during metadata index maintenance.</summary>
        internal readonly ReaderWriterLockSlim libraryFolderCacheRwLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
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
        string steamWebApiKey;
        string retroAchievementsApiKey;
        string steamUserId64;
        string retroAchievementsUsername;
        int libraryFolderTileSize = 300;
        int libraryPhotoTileSize = 340;
        int libraryFolderGridColumnCount;
        bool libraryFolderFillPaneWidth = true;
        int libraryPhotoGridColumnCount;
        int libraryPhotoRailFolderTileSize = 200;
        string libraryPhotoRailFolderSortMode = "alpha";
        string libraryPhotoRailFolderFilterMode = "all";
        int libraryPhotoRailFolderGridColumnCount;
        string libraryFolderSortMode = "alpha";
        string libraryFolderFilterMode = "all";
        string libraryGroupingMode = "all";
        bool troubleshootingLoggingEnabled;
        bool troubleshootingLogRedactPaths;
        bool libraryDoubleClickSetsFolderCover;
        bool libraryRefreshHeroBannerCacheOnNextLibraryOpen;
        readonly string _diagnosticsSessionId;
        const long TroubleshootingLogMaxBytes = 5_000_000L;
        string _libraryBrowserPersistedSearch = string.Empty;
        string _libraryBrowserPersistedLastViewKey = string.Empty;
        double _libraryBrowserPersistedFolderScroll;
        double _libraryBrowserPersistedDetailScroll;
        double _libraryBrowserPersistedFolderPaneWidth;
        LibraryBrowserWorkingSet _libraryBrowserLiveWorkingSet;
        /// <summary>Incremented to cancel in-flight deferred metadata repair when a new deferral is scheduled.</summary>
        int _libraryDeferredMetadataRepairGeneration;
        string _manualMetadataRecentTitleLabelsSerialized = string.Empty;
        Action<bool> activeLibraryFolderRefresh;
        /// <summary>When the library browser is active, runs full-library cover fetch (SteamGrid / IDs). Cleared when the hosted library window closes.</summary>
        Action activeLibraryFullCoverRefresh;
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

        public MainWindow()
        {
            _diagnosticsSessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var paths = ComputePersistentStorageLayout(appRoot, ResolvePersistentDataRoot);
            dataRoot = paths.DataRoot;
            logsRoot = paths.LogsRoot;
            cacheRoot = paths.CacheRoot;
            coversRoot = paths.CoversRoot;
            thumbsRoot = paths.ThumbsRoot;
            savedCoversRoot = paths.SavedCoversRoot;
            settingsPath = paths.SettingsPath;
            changelogPath = paths.ChangelogPath;
            undoManifestPath = paths.UndoManifestPath;
            InitializeLibraryThumbnailPipeline(thumbsRoot);
            (settingsService, fileSystemService) = CreateSettingsAndFileServices();
            coverService = CreateCoverService(this, fileSystemService, coversRoot);
            (indexPersistenceService, filenameParserService, gameIndexEditorAssignmentService, filenameRulesService) = CreateIndexFilenameRulesServices(cacheRoot, this);
            metadataService = CreateMetadataService(this, cacheRoot);
            libraryScanner = CreateLibraryScanner(this, metadataService, fileSystemService);
            importService = new ImportService(BuildImportServiceDependencies(
                this,
                libraryScanner,
                fileSystemService,
                metadataService,
                coverService,
                gameIndexEditorAssignmentService));
            libraryWorkspace = new LibraryWorkspaceContext(this);
            librarySession = CreateLibrarySessionForStartup(
                this,
                libraryWorkspace,
                libraryScanner,
                fileSystemService,
                gameIndexEditorAssignmentService,
                indexPersistenceService);
            importService.AttachLibrarySessionAccessor(() => librarySession);
            gameIndexService = CreateGameIndexServiceForStartup(
                this,
                libraryScanner,
                librarySession,
                indexPersistenceService,
                gameIndexEditorAssignmentService);
            RunPostServiceStartup();
            ApplyMainWindowChromeAndShell();
        }

        string NormalizeLibraryFolderSortMode(string value) => SettingsService.NormalizeLibraryFolderSortMode(value);
        string NormalizeLibraryFolderFilterMode(string value) => SettingsService.NormalizeLibraryFolderFilterMode(value);
        string LibraryFolderSortModeLabel(string value)
        {
            switch (NormalizeLibraryFolderSortMode(value))
            {
                case "captured":
                    return "Date Captured";
                case "added":
                    return "Date Added";
                case "photos":
                    return "Most Photos";
                default:
                    return "Alphabetical";
            }
        }
        string LibraryFolderFilterModeLabel(string value)
        {
            switch (NormalizeLibraryFolderFilterMode(value))
            {
                case "completed":
                    return "100% Achievements";
                case "crossplatform":
                    return "Cross-Platform";
                case "large":
                    return "25+ Captures";
                case "missingid":
                    return "Missing ID / assignment";
                case "nocover":
                    return "No cover path";
                default:
                    return "All Games";
            }
        }
        Button Btn(string t, RoutedEventHandler click, string bg, Brush fg)
        {
            var b = new Button { Content = t, Width = 176, Height = 48, Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 12, 12), Foreground = fg, Background = bg != null ? Brush(bg) : Brushes.White, BorderBrush = Brush("#C0CCD6"), BorderThickness = new Thickness(1), FontWeight = FontWeights.SemiBold, Effect = new DropShadowEffect { Color = Color.FromArgb(64, 18, 27, 36), BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.55 } };
            if (click != null) b.Click += click;
            AccessibilityUi.TryApplyFocusVisualStyle(b);
            return b;
        }
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

        Style LibraryCircleToolbarButtonStyle(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#DCE6EC")
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(foregroundHex)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brush(backgroundHex)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(borderHex)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(UIElement.EffectProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildLibraryCircleToolbarButtonTemplate(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex)));
            return style;
        }

        ControlTemplate BuildLibraryCircleToolbarButtonTemplate(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Chrome";
            borderFactory.SetValue(Border.BackgroundProperty, Brush(backgroundHex));
            borderFactory.SetValue(Border.BorderBrushProperty, Brush(borderHex));
            borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));

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

        Button ApplyLibraryCircleToolbarChrome(Button button, string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#DCE6EC")
        {
            button.Style = LibraryCircleToolbarButtonStyle(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
            button.Cursor = System.Windows.Input.Cursors.Hand;
            button.Width = 36;
            button.Height = 36;
            button.Padding = new Thickness(0);
            button.FontSize = 14;
            button.MinWidth = 0;
            AccessibilityUi.TryApplyFocusVisualStyle(button);
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
                    TryLibraryToast(error.Message, MessageBoxImage.Error);
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

        /// <summary>Resolves missing Steam App IDs using async Steam lookups (no synchronous blocking on async work). Await from UI; <paramref name="progress"/> may run on a thread-pool continuation after network waits.</summary>
        async Task<int> EnrichLibraryFoldersWithSteamAppIdsAsync(string root, List<LibraryFolderInfo> folders, Action<int, int, string> progress, CancellationToken cancellationToken = default(CancellationToken))
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
                cancellationToken.ThrowIfCancellationRequested();
                var folder = targetFolders[i];
                var detailPrefix = "Steam AppID " + (i + 1) + " of " + targetFolders.Count + " | " + folder.Name;
                if (!string.IsNullOrWhiteSpace(folder.SteamAppId))
                {
                    if (progress != null) progress(i + 1, targetFolders.Count, detailPrefix + " | already cached as " + folder.SteamAppId);
                    continue;
                }

                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
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

        string CustomHeroPath(LibraryFolderInfo folder)
        {
            return coverService.CustomHeroPath(folder);
        }

        void SaveCustomHero(LibraryFolderInfo folder, string sourcePath)
        {
            coverService.SaveCustomHero(folder, sourcePath);
        }

        void ClearCustomHero(LibraryFolderInfo folder)
        {
            coverService.ClearCustomHero(folder);
        }

        string CachedHeroPath(string title)
        {
            return coverService.CachedHeroPath(title);
        }

        bool TryGetCustomOrCachedHeroPath(LibraryFolderInfo folder, out string path)
        {
            path = null;
            if (folder == null) return false;
            var custom = CustomHeroPath(folder);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                path = custom;
                return true;
            }

            var cached = CachedHeroPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        /// <summary>Photo-workspace banner: custom or cached wide art only — prefers SteamGridDB Heroes (same asset class as https://www.steamgriddb.com/hero/…), not library cover.</summary>
        internal string GetLibraryHeroBannerPathForDisplayOnly(LibraryFolderInfo folder)
        {
            return TryGetCustomOrCachedHeroPath(folder, out var p) ? p : null;
        }

        /// <summary>Resolves banner art: custom → <b>SteamGridDB Heroes</b> → Valve library_hero / store header fallback.</summary>
        async Task<string> ResolveLibraryHeroBannerWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            if (TryGetCustomOrCachedHeroPath(folder, out var early)) return early;
            var steamGridDbHero = await TryDownloadSteamGridDbHeroAsync(folder, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(steamGridDbHero) && File.Exists(steamGridDbHero)) return steamGridDbHero;
            var steamFallback = await TryDownloadSteamStoreHeaderHeroAsync(folder, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(steamFallback) && File.Exists(steamFallback)) return steamFallback;
            return null;
        }

        async Task<string> TryDownloadSteamGridDbHeroAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || !HasSteamGridDbApiToken()) return null;
            try
            {
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(libraryRoot, folder, cancellationToken).ConfigureAwait(false);
                return await coverService.TryDownloadSteamGridDbHeroAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("SteamGridDB hero download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        async Task<string> TryDownloadSteamStoreHeaderHeroAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            try
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(libraryRoot, folder, true, cancellationToken).ConfigureAwait(false);
                return await coverService.TryDownloadSteamStoreHeaderHeroAsync(folder.Name, appId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("Steam header hero download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
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
            var nonSteamId = parsed.NonSteamId;
            if (!string.IsNullOrWhiteSpace(nonSteamId)) return "Non-Steam ID " + nonSteamId;
            if (parsed.RoutesToManualWhenMissingSteamAppId) return "Steam export | AppID needed";
            var label = parsed.PlatformLabel;
            return string.Equals(label, "Other", StringComparison.OrdinalIgnoreCase) ? "No confident match" : label;
        }

        bool IsSteamManualExportWithoutAppId(string file)
        {
            var parsed = ParseFilename(file);
            return parsed.RoutesToManualWhenMissingSteamAppId
                && string.IsNullOrWhiteSpace(parsed.SteamAppId)
                && string.IsNullOrWhiteSpace(parsed.NonSteamId);
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
                case "Emulation": return 1;
                case "PS5": return 2;
                case "Xbox": return 3;
                case "Xbox PC": return 4;
                case "PC": return 5;
                case "Multiple Tags": return 6;
                case "Other": return 7;
                default: return 8;
            }
        }

        Brush PreviewBadgeBrush(string label)
        {
            switch (label)
            {
                case "Xbox": return Brush("#2E8B57");
                case "Xbox PC": return Brush("#4D8F68");
                case "Steam": return Brush("#2F6FDB");
                case "Emulation": return Brush("#B26A3C");
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
                TryLibraryToast("Library folder not found. Check Settings before opening the photo index.");
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
                        NotifyUser = (msg, icon) => TryLibraryToast(msg, icon),
                        SetStatus = delegate(string text) { if (status != null) status.Text = text; },
                        Log = Log,
                        CreateButton = Btn,
                        LoadRows = libraryScanner.LoadPhotoIndexEditorRows,
                        SaveRows = (root, rows, removed) => libraryScanner.SavePhotoIndexEditorRows(root, rows, removed),
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
                TryLibraryToast("Could not open the photo index." + Environment.NewLine + Environment.NewLine + ex.Message, MessageBoxImage.Error);
            }
        }
        void OpenGameIndexEditor()
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                TryLibraryToast("Library folder not found. Check Settings before opening the game index.");
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
                        TryLibraryToast("Could not load the game index." + Environment.NewLine + Environment.NewLine + err.Message, MessageBoxImage.Error);
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
                                NotifyUser = (msg, icon) => TryLibraryToast(msg, icon),
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
                                FormatCanonicalStorageFolderAbsolutePath = delegate(string root, GameIndexEditorRow row, IReadOnlyList<GameIndexEditorRow> all)
                                {
                                    if (row == null || string.IsNullOrWhiteSpace(root)) return string.Empty;
                                    var list = all as List<GameIndexEditorRow> ?? (all != null ? all.ToList() : new List<GameIndexEditorRow>());
                                    var titleCounts = BuildGameIndexTitleCounts(list);
                                    return LibraryPlacementService.BuildCanonicalStorageFolderPath(
                                        root,
                                        row,
                                        list,
                                        NormalizeGameIndexName,
                                        GetSafeGameFolderName,
                                        NormalizeConsoleLabel,
                                        titleCounts);
                                },
                                RunBackgroundWorkflowIntArray = RunBackgroundWorkflowWithProgress<int[]>,
                                ThrowIfWorkflowCancellationRequested = ImportWorkflowOrchestration.ThrowIfCancellationRequested,
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
                        TryLibraryToast("Could not open the game index." + Environment.NewLine + Environment.NewLine + ex.Message, MessageBoxImage.Error);
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








































































