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
using Velopack;
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
        private static void Main(string[] args)
        {
            // PV-PLN-DIST-001 §5.3: Velopack bootstrap (no-op when not installed via vpk). Same vpk major as NuGet Velopack.
            VelopackApp.Build().SetArgs(args).Run();

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
        const string AppVersion = "0.076.000";
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
        readonly TroubleshootingLog troubleshootingLog;
        readonly ILogService logService;
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
        bool backgroundAutoIntakeEnabled;
        int backgroundAutoIntakeQuietSeconds = 3;
        bool backgroundAutoIntakeToastsEnabled = true;
        bool backgroundAutoIntakeShowSummary;
        bool backgroundAutoIntakeVerboseLogging;
        bool systemTrayMinimizeEnabled;
        bool systemTrayPromptOnCloseEnabled;
        readonly ForegroundIntakeBusyGate _foregroundIntakeBusyGate = new ForegroundIntakeBusyGate();
        readonly string _diagnosticsSessionId;
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
        bool importSearchSubfoldersForRename;
        string importMoveConflictMode = "Rename";
        /// <summary>Thread-safe mirror for background metadata (avoid Dispatcher.Invoke); default matches former Settings checkbox.</summary>
        volatile bool _includeGameCaptureKeywordsMirror = true;
        Window photoIndexEditorWindow;
        Window gameIndexEditorWindow;
        bool gameIndexEditorLoadPending;
        Window filenameConventionEditorWindow;
        PhotographyGalleryWindow _activePhotographyGalleryWindow;
        readonly ICoverService coverService;
        readonly ILibraryCoverResolution libraryCoverResolutionService;
        readonly IFilenameParserService filenameParserService;
        readonly IFilenameRulesService filenameRulesService;
        readonly IIndexPersistenceService indexPersistenceService;
        readonly IMetadataService metadataService;
        readonly ISettingsService settingsService;
        readonly IFileSystemService fileSystemService;
        readonly ILibraryScanner libraryScanner;
        readonly ILibrarySession librarySession;
        readonly IImportService importService;
        readonly IntakePipeline intakePipeline;
        readonly BackgroundIntakeActivitySession _backgroundIntakeActivitySession = new BackgroundIntakeActivitySession();
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
            troubleshootingLog = new TroubleshootingLog(new TroubleshootingLogDependencies
            {
                LogsRoot = logsRoot,
                IsTroubleshootingLoggingEnabled = () => troubleshootingLoggingEnabled,
                RedactPathsEnabled = () => troubleshootingLogRedactPaths,
                DiagnosticsSessionId = _diagnosticsSessionId,
            });
            InitializeLibraryThumbnailPipeline(thumbsRoot);
            var services = BuildApplicationServiceGraph(this, cacheRoot, coversRoot, troubleshootingLog);
            logService = services.LogService;
            settingsService = services.SettingsService;
            fileSystemService = services.FileSystemService;
            coverService = services.CoverService;
            libraryCoverResolutionService = services.LibraryCoverResolutionService;
            indexPersistenceService = services.IndexPersistenceService;
            filenameParserService = services.FilenameParserService;
            filenameRulesService = services.FilenameRulesService;
            gameIndexEditorAssignmentService = services.GameIndexEditorAssignmentService;
            metadataService = services.MetadataService;
            libraryScanner = services.LibraryScanner;
            importService = services.ImportService;
            intakePipeline = services.IntakePipeline;
            libraryWorkspace = services.LibraryWorkspace;
            librarySession = services.LibrarySession;
            gameIndexService = services.GameIndexService;
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
        // PV-PLN-UI-001 Step 9: button / toolbar chrome factories live in LibraryButtonChrome.
        // These thin instance forwarders exist because ~70 call sites across MainWindow partials
        // (and a handful of dependency-delegate wirings like MainWindow.SettingsShell) still bind
        // to `Btn` / `ApplyLibrary*Chrome` as MainWindow members. Keep them one-liners; don't grow.
        Button Btn(string t, RoutedEventHandler click, string bg, Brush fg)
            => LibraryButtonChrome.Btn(t, click, bg, fg);
        Style LibraryToolbarButtonStyle(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#F4F7FA")
            => LibraryButtonChrome.LibraryToolbarButtonStyle(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
        ControlTemplate BuildLibraryToolbarButtonTemplate(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex)
            => LibraryButtonChrome.BuildLibraryToolbarButtonTemplate(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex);
        Button ApplyLibraryToolbarChrome(Button button, string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#F4F7FA")
            => LibraryButtonChrome.ApplyLibraryToolbarChrome(button, backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
        Button ApplyLibraryPillChrome(Button button, string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#DCE6EC")
            => LibraryButtonChrome.ApplyLibraryPillChrome(button, backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
        Style LibraryCircleToolbarButtonStyle(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#DCE6EC")
            => LibraryButtonChrome.LibraryCircleToolbarButtonStyle(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
        ControlTemplate BuildLibraryCircleToolbarButtonTemplate(string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex)
            => LibraryButtonChrome.BuildLibraryCircleToolbarButtonTemplate(backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex);
        Button ApplyLibraryCircleToolbarChrome(Button button, string backgroundHex, string borderHex, string hoverBackgroundHex, string pressedBackgroundHex, string foregroundHex = "#DCE6EC")
            => LibraryButtonChrome.ApplyLibraryCircleToolbarChrome(button, backgroundHex, borderHex, hoverBackgroundHex, pressedBackgroundHex, foregroundHex);
        ControlTemplate BuildRoundedTileButtonTemplate()
            => LibraryButtonChrome.BuildRoundedTileButtonTemplate();


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

        // PV-PLN-UI-001 Step 8 Pass A: Steam / SteamGridDB / cover resolution block (GetGameNameFromFileName
        // through UpdateCachedLibraryFolderInfo) moved to UI/Library/MainWindow.LibraryCoverResolution.cs.

        // PV-PLN-UI-001 Step 12: platform-label helpers live in UI/Library/LibraryPlatformLabels.cs.
        // Keep the `(string file)` instance overloads as one-line forwarders so the ~a dozen call
        // sites across partials (ManualMetadata.Helpers, ManualMetadata, IntakePreview, MetadataReview,
        // LibraryBrowserViewModel, LibraryBrowserRender.FolderList, LibraryBrowserOrchestrator.FolderTile,
        // LibraryBrowserQuickEditDrawer, and method-group captures in IntakePreview / MetadataReview)
        // keep resolving without touching `FilenameParseResult` at every caller.
        string PrimaryPlatformLabel(string file) => LibraryPlatformLabels.PrimaryPlatformLabel(ParseFilename(file));
        string FilenameGuessLabel(string file) => LibraryPlatformLabels.FilenameGuessLabel(ParseFilename(file));
        bool IsSteamManualExportWithoutAppId(string file) => LibraryPlatformLabels.IsSteamManualExportWithoutAppId(ParseFilename(file));
        int PlatformGroupOrder(string label) => LibraryPlatformLabels.PlatformGroupOrder(label);
        Brush PreviewBadgeBrush(string label) => LibraryPlatformLabels.PreviewBadgeBrush(label);

        // PV-PLN-UI-001 Step 12: pure text / path / media-type helpers live in
        // Infrastructure/TextAndPathHelpers.cs. These `MainWindow.X` static forwarders preserve the
        // public surface other classes reference (e.g. `MainWindow.CleanTag`, `MainWindow.IsImage`,
        // `MainWindow.Sanitize`, `MainWindow.ParseTagText`, `MainWindow.Unique`, `MainWindow.EnsureDir`,
        // `MainWindow.IsVideo` — hit by StartupInitialization, IndexServicesWiring, LibraryScanner,
        // GameIndexCore, LibraryWorkspaceContext, LibraryBrowserShellBridge, LibraryScannerBridge).
        // Keep them one-liners; don't grow.
        static int ParseInt(string value) => TextAndPathHelpers.ParseInt(value);
        static long ParseLong(string value) => TextAndPathHelpers.ParseLong(value);
        static string FormatFriendlyTimestamp(DateTime value) => TextAndPathHelpers.FormatFriendlyTimestamp(value);
        static string Sanitize(string s) => TextAndPathHelpers.Sanitize(s);
        static string CleanComment(string s) => TextAndPathHelpers.CleanComment(s);
        internal static string CleanTag(string s) => TextAndPathHelpers.CleanTag(s);
        static string[] ParseTagText(string s) => TextAndPathHelpers.ParseTagText(s);
        static bool SameManualText(string left, string right) => TextAndPathHelpers.SameManualText(left, right);
        static string Unique(string path) => TextAndPathHelpers.Unique(path);
        static void EnsureDir(string path, string label) => TextAndPathHelpers.EnsureDir(path, label);
        internal static bool IsImage(string p) => TextAndPathHelpers.IsImage(p);
        static bool IsPngOrJpeg(string p) => TextAndPathHelpers.IsPngOrJpeg(p);
        static bool IsVideo(string p) => TextAndPathHelpers.IsVideo(p);
        static bool IsMedia(string p) => TextAndPathHelpers.IsMedia(p);
        static string Quote(string s) => TextAndPathHelpers.Quote(s);

        // GetLibraryDate stays as an instance forwarder so the method group captures in
        // IntakePipeline (IntakeAnalysisService), LibraryMetadataEditing, PhotographyAndSteam, LibraryVirtualization,
        // ImportWorkflow.Steps still resolve without rewiring.
        DateTime GetLibraryDate(string file) => TextAndPathHelpers.GetLibraryDate(file, ParseFilename(file));

        string SafeCacheName(string title) => TextAndPathHelpers.SafeCacheName(title);
        string NormalizeTitle(string title) => TextAndPathHelpers.NormalizeTitle(title);
        string StripTags(string html) => TextAndPathHelpers.StripTags(html);

        // PV-PLN-UI-001 Step 11: persistent-data migration + open-folder glue live in
        // Infrastructure/PersistentDataMigrator.cs. These thin instance forwarders exist so
        // `ResolvePersistentDataRoot` can still be passed as a method group to
        // `ComputePersistentStorageLayout`, and so the ~14 call sites that bind `OpenFolder` /
        // `OpenSavedCoversFolder` as `Action<string>` / `Action` (SettingsShellDependencies,
        // PhotoIndexEditorHost, GameIndexEditorHost, HealthDashboardWindow, palette commands,
        // nav chrome, folder tile menu, photo hero menu, quick-edit drawer, manual metadata)
        // keep resolving unchanged. Keep them one-liners; don't grow.
        string ResolvePersistentDataRoot(string currentAppRoot) => PersistentDataMigrator.ResolvePersistentDataRoot(currentAppRoot, Log);
        void MigratePersistentDataFromLegacyVersions() => PersistentDataMigrator.MigrateFromLegacyVersions(appRoot, dataRoot, settingsPath, cacheRoot, logsRoot, fileSystemService);
        void OpenFolder(string path) => PersistentDataMigrator.OpenFolder(path, Log);
        void EnsureSavedCoversReadme() => PersistentDataMigrator.EnsureSavedCoversReadme(savedCoversRoot, Log);
        void OpenSavedCoversFolder() => PersistentDataMigrator.OpenSavedCoversFolder(savedCoversRoot, Log);

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

        // PV-PLN-UI-001 Step 10: log file IO + redaction live in Infrastructure/TroubleshootingLog.cs.
        // These thin instance forwarders exist so MainWindow partials (Log / LogException / LogTroubleshooting)
        // and a handful of other call sites (SettingsShellHost paths, LibraryBrowserViewModel path formatters,
        // LibraryBrowserRender.DetailPane exception formatting) keep binding to MainWindow members unchanged.
        // Keep them one-liners; don't grow.
        string LogFilePath() => troubleshootingLog.MainLogFilePath();
        string TroubleshootingLogFilePath() => troubleshootingLog.TroubleshootingLogFilePath();
        string TryReadLogFile() => troubleshootingLog.TryReadMainLog();
        void LoadLogView()
        {
            if (logBox == null) return;
            logBox.Text = TryReadLogFile();
            logBox.ScrollToEnd();
        }
        void Log(string message)
        {
            var line = logService.AppendMainLine(message);
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
        }

        /// <summary>Writes a full exception (including stack trace) to the main log with ERROR prefix and managed thread id.</summary>
        internal void LogException(string context, Exception ex)
        {
            if (ex == null) return;
            var prefix = string.IsNullOrWhiteSpace(context) ? string.Empty : context + " | ";
            Log("ERROR | T" + Environment.CurrentManagedThreadId + " | " + prefix + ex);
        }

        void LogTroubleshooting(string area, string message) => troubleshootingLog.LogTroubleshooting(area, message);
        string FormatExceptionForTroubleshooting(Exception ex) => TroubleshootingLog.FormatException(ex);
        string FormatPathForTroubleshooting(string path) => troubleshootingLog.FormatPath(path);
        string FormatViewKeyForTroubleshooting(string viewKey) => troubleshootingLog.FormatViewKey(viewKey);
    }
}






























































