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
            new Application().Run(new MainWindow());
        }
    }

    sealed class LibraryFolderInfo
    {
        public string GameId;
        public string Name;
        public string FolderPath;
        public int FileCount;
        public string PreviewImagePath;
        public string PlatformLabel;
        public string[] FilePaths;
        public string SteamAppId;
    }

    sealed class GameIndexEditorRow
    {
        public string GameId { get; set; }
        public string Name { get; set; }
        public string PlatformLabel { get; set; }
        public string SteamAppId { get; set; }
        public int FileCount { get; set; }
        public string FolderPath { get; set; }
        public string PreviewImagePath { get; set; }
        public string[] FilePaths { get; set; }
    }

    sealed class PhotoIndexEditorRow
    {
        public string FilePath { get; set; }
        public string Stamp { get; set; }
        public string GameId { get; set; }
        public string ConsoleLabel { get; set; }
        public string TagText { get; set; }
    }


    sealed class LibraryMetadataIndexEntry
    {
        public string FilePath;
        public string Stamp;
        public string GameId;
        public string ConsoleLabel;
        public string TagText;
    }


    sealed class ReviewItem
    {
        public string FilePath;
        public string FileName;
        public string PlatformLabel;
        public string[] PlatformTags;
        public DateTime CaptureTime;
        public bool PreserveFileTimes;
        public string Comment;
        public bool AddPhotographyTag;
        public bool TagSteam;
        public bool TagPc;
        public bool TagPs5;
        public bool TagXbox;
        public bool TagOther;
        public string CustomPlatformTag;
        public bool DeleteBeforeProcessing;
    }

    sealed class ManualMetadataItem
    {
        public string GameId;
        public string FilePath;
        public string FileName;
        public string OriginalFileName;
        public DateTime CaptureTime;
        public bool UseCustomCaptureTime;
        public string GameName;
        public string Comment;
        public string TagText;
        public bool AddPhotographyTag;
        public bool ForceTagMetadataWrite;
        public bool TagSteam;
        public bool TagPc;
        public bool TagPs5;
        public bool TagXbox;
        public bool TagOther;
        public string CustomPlatformTag;
        public string OriginalGameId;
        public DateTime OriginalCaptureTime;
        public bool OriginalUseCustomCaptureTime;
        public string OriginalGameName;
        public string OriginalComment;
        public string OriginalTagText;
        public bool OriginalAddPhotographyTag;
        public bool OriginalTagSteam;
        public bool OriginalTagPc;
        public bool OriginalTagPs5;
        public bool OriginalTagXbox;
        public bool OriginalTagOther;
        public string OriginalCustomPlatformTag;
    }

    sealed class UndoImportEntry
    {
        public string SourceDirectory;
        public string ImportedFileName;
        public string CurrentPath;
    }

    sealed class SourceInventory
    {
        public List<string> TopLevelMediaFiles = new List<string>();
        public List<string> RenameScopeFiles = new List<string>();
    }

    sealed class ExifWriteRequest
    {
        public string FilePath;
        public string FileName;
        public string[] Arguments;
        public bool RestoreFileTimes;
        public DateTime OriginalCreateTime;
        public DateTime OriginalWriteTime;
        public string SuccessDetail;
    }

    public sealed class MainWindow : Window
    {
        const string AppVersion = "0.725";
        const string GamePhotographyTag = "Game Photography";
        const string CustomPlatformPrefix = "Platform:";
        const int MaxImageCacheEntries = 240;
        readonly string appRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        readonly string dataRoot;
        readonly string logsRoot;
        readonly string cacheRoot;
        readonly string coversRoot;
        readonly string thumbsRoot;
        readonly string settingsPath;
        readonly string changelogPath;
        readonly string undoManifestPath;
        readonly Dictionary<string, string> steamCache = new Dictionary<string, string>();
        readonly Dictionary<string, string> steamSearchCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, BitmapImage> imageCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> imageCacheOrder = new Queue<string>();
        readonly object imageCacheSync = new object();
        readonly SemaphoreSlim imageLoadLimiter = new SemaphoreSlim(Math.Max(1, Math.Min(Environment.ProcessorCount, 3)));
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
        int libraryFolderTileSize = 240;

        RichTextBox previewBox;
        TextBox logBox;
        TextBlock status;
        CheckBox recurseBox, keywordsBox;
        ComboBox conflictBox;

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
            var leftStack = new StackPanel();
            left.Child = leftStack;
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
            openRow.Children.Add(Btn("Preview Intake", delegate { RefreshPreview(); }, "#DCEEFF", Brush("#174A73")));
            openRow.Children.Add(Btn("Open Sources", delegate { OpenSourceFolders(); }, "#EEF2F5", Brush("#33424D")));
            openRow.Children.Add(Btn("Open Destination", delegate { OpenFolder(destinationRoot); }, "#EEF2F5", Brush("#33424D")));
            leftStack.Children.Add(openRow);

            previewBox = new RichTextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 560,
                Margin = new Thickness(0, 4, 0, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                FontFamily = new FontFamily("Cascadia Mono"),
                IsDocumentEnabled = false
            };
            leftStack.Children.Add(previewBox);
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
        int NormalizeLibraryFolderTileSize(int value)
        {
            if (value < 140) return 140;
            if (value > 340) return 340;
            return value;
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
            stack.Children.Add(new TextBlock { Text = "FFmpeg: " + (string.IsNullOrWhiteSpace(ffmpegPath) ? "(not configured)" : ffmpegPath), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F") });
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
                Height = 540,
                MinWidth = 680,
                MinHeight = 620,
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
                Height = 980,
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

        SourceInventory BuildSourceInventory(bool recurseRename)
        {
            var topLevelMediaFiles = EnumerateSourceFiles(SearchOption.TopDirectoryOnly, IsMedia).ToList();
            return new SourceInventory
            {
                TopLevelMediaFiles = topLevelMediaFiles,
                RenameScopeFiles = recurseRename
                    ? EnumerateSourceFiles(SearchOption.AllDirectories, IsMedia).ToList()
                    : topLevelMediaFiles.ToList()
            };
        }

        int GetMetadataWorkerCount(int workItems)
        {
            if (workItems <= 1) return 1;
            return Math.Max(1, Math.Min(Math.Min(Environment.ProcessorCount, 4), workItems));
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
            }
        }

        BitmapImage TryGetCachedImage(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)) return null;
            lock (imageCacheSync)
            {
                BitmapImage cached;
                return imageCache.TryGetValue(cacheKey, out cached) ? cached : null;
            }
        }

        void StoreCachedImage(string cacheKey, BitmapImage image)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || image == null) return;
            lock (imageCacheSync)
            {
                if (imageCache.ContainsKey(cacheKey))
                {
                    imageCache[cacheKey] = image;
                    return;
                }
                imageCache[cacheKey] = image;
                imageCacheOrder.Enqueue(cacheKey);
                while (imageCache.Count > MaxImageCacheEntries && imageCacheOrder.Count > 0)
                {
                    var oldest = imageCacheOrder.Dequeue();
                    if (string.IsNullOrWhiteSpace(oldest)) continue;
                    imageCache.Remove(oldest);
                }
            }
        }

        void QueueImageLoad(Image imageControl, string sourcePath, int decodePixelWidth, Action<BitmapImage> onLoaded)
        {
            if (imageControl == null)
            {
                return;
            }

            var requestToken = Guid.NewGuid().ToString("N");
            var hadSource = imageControl.Source != null;
            imageControl.Uid = requestToken;
            if (!hadSource)
            {
                imageControl.Visibility = Visibility.Collapsed;
            }
            Task.Run(delegate
            {
                imageLoadLimiter.Wait();
                try
                {
                    return LoadImageSource(sourcePath, decodePixelWidth);
                }
                finally
                {
                    imageLoadLimiter.Release();
                }
            }).ContinueWith(delegate(Task<BitmapImage> task)
            {
                imageControl.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!string.Equals(imageControl.Uid, requestToken, StringComparison.Ordinal)) return;
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
        void RefreshPreview()
        {
            try
            {
                EnsureSourceFolders();
                var inventory = BuildSourceInventory(recurseBox != null && recurseBox.IsChecked == true);
                var rename = inventory.RenameScopeFiles;
                var move = inventory.TopLevelMediaFiles;
                var reviewItems = BuildReviewItems(move);
                var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(move, recognizedPaths);
                var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var moveCandidates = move.Where(f => !manualPaths.Contains(f)).ToList();
                int renameCandidates = rename.Count(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"(?<!\d)(\d{3,})(?!\d)"));
                int metaCandidates = reviewItems.Count;
                int conflicts = Directory.Exists(destinationRoot) ? moveCandidates.Count(f => File.Exists(Path.Combine(destinationRoot, Path.GetFileName(f)))) : 0;
                RenderPreview(rename.Count, renameCandidates, move.Count, metaCandidates, moveCandidates, manualItems, conflicts);
                status.Text = "Preview ready";
                Log("Preview refreshed. Sources=" + SourceRootsSummary() + "; RenameCandidates=" + renameCandidates + "; MetadataCandidates=" + metaCandidates + "; MoveCandidates=" + moveCandidates.Count + "; ManualCandidates=" + manualItems.Count + ".");
            }
            catch (Exception ex)
            {
                RenderPreviewError(ex.Message);
                status.Text = "Preview failed";
                Log(ex.Message);
            }
        }

        void RenderPreview(int renameTotal, int renameCandidates, int metaTotal, int metaCandidates, List<string> moveFiles, List<ManualMetadataItem> manualItems, int conflicts)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono"), FontSize = 14, Background = Brushes.White };
            doc.Blocks.Add(new Paragraph(new Run("Rename: " + renameCandidates + " candidate(s) out of " + renameTotal)) { Margin = new Thickness(0) });
            doc.Blocks.Add(new Paragraph(new Run("Metadata: " + metaCandidates + " candidate(s) out of " + metaTotal)) { Margin = new Thickness(0) });
            doc.Blocks.Add(new Paragraph(new Run("Move: " + moveFiles.Count + " candidate(s)")) { Margin = new Thickness(0) });
            doc.Blocks.Add(new Paragraph(new Run("Manual Intake: " + manualItems.Count + " unmatched image(s) waiting")) { Margin = new Thickness(0) });
            doc.Blocks.Add(new Paragraph(new Run("Move conflicts: " + conflicts)) { Margin = new Thickness(0, 0, 0, 10) });
            doc.Blocks.Add(new Paragraph(new Run("Files by console:")) { Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.SemiBold });
            var files = moveFiles.OrderBy(Path.GetFileName).ToList();
            if (files.Count == 0)
            {
                doc.Blocks.Add(new Paragraph(new Run("No matching media files found.")) { Margin = new Thickness(0) });
            }
            else
            {
                var grouped = files.GroupBy(f => PrimaryPlatformLabel(Path.GetFileName(f))).OrderBy(g => PlatformGroupOrder(g.Key)).ThenBy(g => g.Key);
                foreach (var group in grouped)
                {
                    doc.Blocks.Add(new Paragraph(new Run(group.Key + " (" + group.Count() + ")")) { Margin = new Thickness(0, 6, 0, 4), FontWeight = FontWeights.SemiBold, Foreground = PreviewBadgeBrush(group.Key) });
                    foreach (var file in group)
                    {
                        doc.Blocks.Add(new Paragraph(new Run(Path.GetFileName(file)) { Foreground = Brush("#1F2A30") }) { Margin = new Thickness(12, 0, 0, 2) });
                    }
                }
            }
            if (manualItems.Count > 0)
            {
                doc.Blocks.Add(new Paragraph(new Run("Unmatched files waiting for Manual Intake:")) { Margin = new Thickness(0, 12, 0, 6), FontWeight = FontWeights.SemiBold, Foreground = Brush("#A16C2E") });
                foreach (var item in manualItems.OrderBy(i => i.FileName))
                {
                    doc.Blocks.Add(new Paragraph(new Run(item.FileName) { Foreground = Brush("#5B5048") }) { Margin = new Thickness(12, 0, 0, 2) });
                }
            }
            previewBox.Document = doc;
        }

        void RenderPreviewError(string message)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Cascadia Mono"), FontSize = 14, Background = Brushes.White };
            doc.Blocks.Add(new Paragraph(new Run(message)) { Margin = new Thickness(0) });
            previewBox.Document = doc;
        }

        void RunWorkflow(bool withReview)
        {
            try
            {
                EnsureSourceFolders();
                EnsureExifTool();
                Directory.CreateDirectory(destinationRoot);
                var renameInventory = BuildSourceInventory(recurseBox != null && recurseBox.IsChecked == true);
                RunRename(renameInventory.RenameScopeFiles);
                var inventory = BuildSourceInventory(false);
                var reviewItems = BuildReviewItems(inventory.TopLevelMediaFiles);
                var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
                var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                if (withReview && reviewItems.Count > 0)
                {
                    status.Text = "Reviewing captures";
                    Log("Opening review window for " + reviewItems.Count + " metadata candidate(s).");
                    if (!ShowMetadataReviewWindow(reviewItems))
                    {
                        status.Text = "Import canceled";
                        Log("Import canceled from review window.");
                        RefreshPreview();
                        return;
                    }
                }
                else if (withReview)
                {
                    Log("No metadata review items found. Continuing without review comments.");
                }
                RunDelete(reviewItems);
                RunMetadata(reviewItems);
                var imported = RunMove(inventory.TopLevelMediaFiles, manualPaths);
                if (imported.Count > 0)
                {
                    SaveUndoManifest(imported);
                    SortDestinationFoldersCore(false);
                }
                if (manualItems.Count > 0)
                {
                    Log("Left " + manualItems.Count + " unmatched intake image(s) untouched. Use Manual Intake when you want to add missing data.");
                }
                RefreshPreview();
                status.Text = "Workflow complete";
                Log("Workflow complete.");
            }
            catch (Exception ex)
            {
                status.Text = "Workflow failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void OpenManualIntakeWindow()
        {
            try
            {
                EnsureSourceFolders();
                EnsureExifTool();
                Directory.CreateDirectory(destinationRoot);
                var inventory = BuildSourceInventory(false);
                var recognizedPaths = new HashSet<string>(BuildReviewItems(inventory.TopLevelMediaFiles).Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
                if (manualItems.Count == 0)
                {
                    status.Text = "No manual intake items";
                    Log("Manual intake opened, but no unmatched image files were found.");
                    MessageBox.Show("There are no unmatched intake images waiting for manual metadata.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshPreview();
                    return;
                }

                status.Text = "Manual intake review";
                Log("Opening manual intake window for " + manualItems.Count + " unmatched image(s).");
                if (!ShowManualMetadataWindow(manualItems, false, string.Empty))
                {
                    status.Text = "Manual intake unchanged";
                    Log("Manual intake window closed. Left " + manualItems.Count + " unmatched image(s) unchanged.");
                    RefreshPreview();
                    return;
                }

                RunManualRename(manualItems);
                RunManualMetadata(manualItems);
                var imported = RunMoveFiles(manualItems.Select(i => i.FilePath), "Manual move summary");
                if (imported.Count > 0)
                {
                    SaveUndoManifest(imported);
                    SortDestinationFoldersCore(false);
                }
                RefreshPreview();
                status.Text = "Manual intake complete";
                Log("Manual intake workflow complete.");
            }
            catch (Exception ex)
            {
                status.Text = "Manual intake failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void RunRename()
        {
            RunRename(BuildSourceInventory(recurseBox != null && recurseBox.IsChecked == true).RenameScopeFiles);
        }

        void RunRename(IEnumerable<string> sourceFiles)
        {
            int renamed = 0, skipped = 0;
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists))
            {
                var m = Regex.Match(Path.GetFileNameWithoutExtension(file), @"(?<!\d)(\d{3,})(?!\d)");
                if (!m.Success) { skipped++; continue; }
                var game = SteamName(m.Groups[1].Value);
                if (string.IsNullOrWhiteSpace(game)) { skipped++; continue; }
                var baseName = Path.GetFileNameWithoutExtension(file);
                var newBase = baseName.Substring(0, m.Index) + game + baseName.Substring(m.Index + m.Length);
                var target = Unique(Path.Combine(Path.GetDirectoryName(file), newBase + Path.GetExtension(file)));
                File.Move(file, target);
                MoveMetadataSidecarIfPresent(file, target);
                renamed++;
                Log("Renamed: " + Path.GetFileName(file) + " -> " + Path.GetFileName(target));
            }
            Log("Rename summary: renamed " + renamed + ", skipped " + skipped + ".");
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
                ? items.Count + " image(s) from " + contextLabel + " are ready for metadata edits. Select one or more files, update the game title prefix, tags, one console tag, an optional capture date/time, and an optional comment. Files can also be reorganized into the proper game folder when the title changes."
                : items.Count + " image(s) were left in intake because they did not match a known format. Select one or more files, add a game title prefix, tags, one console tag, an optional capture date/time, and an optional comment before sending them to the destination.";
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
                Height = 900,
                MinWidth = 1040,
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

            var selectedTitle = new TextBlock { Text = "Select one or more images", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), TextWrapping = TextWrapping.Wrap };
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
                    selectedTitle.Text = "Select one or more images";
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
                    selectedTitle.Text = selectedItems.Count + " images selected";
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
                        PlatformLabel = DetermineManualMetadataPlatformLabel(item)
                    })
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                    .Where(entry => FindSavedGameIndexRowByIdentity(gameRows, entry.Name, entry.PlatformLabel) == null
                        && FindSavedGameIndexRowById(gameRows, entry.Item.GameId) == null)
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
                        if (FindSavedGameIndexRowByIdentity(gameRows, resolvedName, resolvedPlatform) != null) continue;
                        if (!string.IsNullOrWhiteSpace(item.GameId) && FindSavedGameIndexRowById(gameRows, item.GameId) != null) continue;
                        EnsureGameIndexRowForAssignment(gameRows, resolvedName, resolvedPlatform, item.GameId);
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
                    var resolvedRow = ResolveExistingGameIndexRowForAssignment(gameRows, item.GameName, DetermineManualMetadataPlatformLabel(item), item.GameId);
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
        List<ManualMetadataItem> BuildLibraryMetadataItems(LibraryFolderInfo folder)
        {
            var items = new List<ManualMetadataItem>();
            if (folder == null || string.IsNullOrWhiteSpace(folder.FolderPath) || !Directory.Exists(folder.FolderPath)) return items;
            foreach (var file in GetFilesForLibraryFolderEntry(folder, false).OrderByDescending(GetLibraryDate).ThenBy(Path.GetFileName))
            {
                var fileName = Path.GetFileName(file);
                var indexEntry = TryGetLibraryMetadataIndexEntry(libraryRoot, file, null);
                var tags = GetEmbeddedKeywordTags(file);
                var consoleTags = GetConsolePlatformTagsForFile(file);
                var customPlatform = tags.FirstOrDefault(tag => tag.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase));
                var customPlatformName = string.IsNullOrWhiteSpace(customPlatform) ? string.Empty : CleanTag(customPlatform.Substring(CustomPlatformPrefix.Length));
                var normalizedCustomPlatform = NormalizeConsoleLabel(customPlatformName);
                var useCustomPlatform = !string.IsNullOrWhiteSpace(customPlatformName)
                    && !string.Equals(normalizedCustomPlatform, "Steam", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "PC", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "PS5", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "Xbox", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "Other", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "Multiple Tags", StringComparison.OrdinalIgnoreCase);
                var captureTime = GetLibraryDate(file);
                var currentComment = string.Empty;
                var filteredTagText = string.Join(", ", tags.Where(tag =>
                    !string.Equals(tag, "Game Capture", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, GamePhotographyTag, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "Photography", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "Steam", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "PC", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "PS5", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "PlayStation", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "Xbox", StringComparison.OrdinalIgnoreCase) &&
                    !tag.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase)));
                var addPhotographyTag = tags.Any(tag => string.Equals(tag, GamePhotographyTag, StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "Photography", StringComparison.OrdinalIgnoreCase));
                var tagSteam = consoleTags.Contains("Steam");
                var tagPc = !consoleTags.Contains("Steam") && consoleTags.Contains("PC");
                var tagPs5 = consoleTags.Contains("PS5");
                var tagXbox = consoleTags.Contains("Xbox");
                var customPlatformValue = useCustomPlatform ? customPlatformName : string.Empty;
                items.Add(new ManualMetadataItem
                {
                    GameId = indexEntry == null ? (folder == null ? string.Empty : folder.GameId) : indexEntry.GameId,
                    FilePath = file,
                    FileName = fileName,
                    OriginalFileName = fileName,
                    CaptureTime = captureTime,
                    UseCustomCaptureTime = false,
                    GameName = folder.Name ?? string.Empty,
                    Comment = currentComment,
                    TagText = filteredTagText,
                    AddPhotographyTag = addPhotographyTag,
                    TagSteam = tagSteam,
                    TagPc = tagPc,
                    TagPs5 = tagPs5,
                    TagXbox = tagXbox,
                    TagOther = useCustomPlatform,
                    CustomPlatformTag = customPlatformValue,
                    OriginalGameId = indexEntry == null ? (folder == null ? string.Empty : folder.GameId) : indexEntry.GameId,
                    OriginalCaptureTime = captureTime,
                    OriginalUseCustomCaptureTime = false,
                    OriginalGameName = folder.Name ?? string.Empty,
                    OriginalComment = currentComment,
                    OriginalTagText = filteredTagText,
                    OriginalAddPhotographyTag = addPhotographyTag,
                    OriginalTagSteam = tagSteam,
                    OriginalTagPc = tagPc,
                    OriginalTagPs5 = tagPs5,
                    OriginalTagXbox = tagXbox,
                    OriginalTagOther = useCustomPlatform,
                    OriginalCustomPlatformTag = customPlatformValue
                });
            }
            return items;
        }

        void ClearLibraryImageCaches()
        {
            folderImageCache.Clear();
            folderImageCacheStamp.Clear();
            fileTagCache.Clear();
            fileTagCacheStamp.Clear();
            ClearImageCache();
        }
        int OrganizeLibraryItems(List<ManualMetadataItem> items, Action<int, int, string> progress = null)
        {
            int moved = 0, created = 0, renamedConflict = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            if (progress != null) progress(0, total, "Starting organize step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                var item = items[i];
                var remaining = total - (i + 1);
                if (!File.Exists(item.FilePath))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped organize " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var gameName = string.IsNullOrWhiteSpace(item.GameName)
                    ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                    : item.GameName;
                var targetDirectory = Path.Combine(libraryRoot, GetSafeGameFolderName(gameName));
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    created++;
                }
                var currentDirectory = Path.GetDirectoryName(item.FilePath) ?? string.Empty;
                if (string.Equals(currentDirectory.TrimEnd(Path.DirectorySeparatorChar), targetDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Already organized " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
                    continue;
                }
                var target = Path.Combine(targetDirectory, Path.GetFileName(item.FilePath));
                if (File.Exists(target))
                {
                    target = Unique(target);
                    renamedConflict++;
                }
                var oldName = item.FileName;
                var originalPath = item.FilePath;
                File.Move(item.FilePath, target);
                MoveMetadataSidecarIfPresent(originalPath, target);
                item.FilePath = target;
                item.FileName = Path.GetFileName(target);
                moved++;
                Log("Library organize: " + oldName + " -> " + target);
                if (progress != null) progress(i + 1, total, "Organized " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
            }
            if (progress != null) progress(total, total, "Organize step complete: moved " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ", already-in-place " + skipped + ".");
            ClearLibraryImageCaches();
            Log("Library organize summary: moved " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ", already-in-place " + skipped + ".");
            return moved;
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
                    status.Text = "No library images to edit";
                    Log("Library metadata editor opened, but no image files were found in " + folder.FolderPath + ".");
                    MessageBox.Show("There are no image files in this folder yet.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                status.Text = "Editing library metadata";
                Log("Opening library metadata editor for " + items.Count + " image(s) in " + folder.Name + ".");
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
            var progressMeta = new TextBlock { Text = "Preparing " + items.Count + " image(s)...", Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
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
            appendProgress("Starting library metadata apply for " + items.Count + " image(s) in " + folder.Name + ".");
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
                    updateProgress(totalWork, "Library metadata apply complete for " + folder.Name + ". Edited " + items.Count + " image(s); reorganized " + moved + ".");
                    status.Text = moved > 0 ? "Library metadata updated and organized" : "Library metadata updated";

                    if (refreshLibrary != null) refreshLibrary();
                    Log("Library metadata apply complete for " + folder.Name + ". Edited " + items.Count + " image(s); reorganized " + moved + ".");
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

        void RunManualRename(List<ManualMetadataItem> items, Action<int, int, string> progress = null)
        {
            int renamed = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            if (progress != null) progress(0, total, "Starting rename step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                var item = items[i];
                var remaining = total - (i + 1);
                if (!File.Exists(item.FilePath))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var gameName = Sanitize(item.GameName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(gameName))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | no game title");
                    continue;
                }
                var currentBase = Path.GetFileNameWithoutExtension(item.FilePath);
                var normalizedCurrent = NormalizeTitle(currentBase);
                var normalizedGame = NormalizeTitle(gameName);
                if (currentBase.StartsWith(gameName + "_", StringComparison.OrdinalIgnoreCase) || normalizedCurrent == normalizedGame || normalizedCurrent.StartsWith(normalizedGame + " "))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
                    continue;
                }
                var oldName = item.FileName;
                var target = Unique(Path.Combine(Path.GetDirectoryName(item.FilePath), gameName + "_" + currentBase + Path.GetExtension(item.FilePath)));
                var originalPath = item.FilePath;
                File.Move(item.FilePath, target);
                MoveMetadataSidecarIfPresent(originalPath, target);
                Log("Manual rename: " + oldName + " -> " + Path.GetFileName(target));
                item.FilePath = target;
                item.FileName = Path.GetFileName(target);
                renamed++;
                if (progress != null) progress(i + 1, total, "Renamed " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
            }
            if (progress != null) progress(total, total, "Rename step complete: renamed " + renamed + ", skipped " + skipped + ".");
            if (renamed > 0 || skipped > 0) Log("Manual rename summary: renamed " + renamed + ", skipped " + skipped + ".");
        }

        void RunManualMetadata(List<ManualMetadataItem> items, Action<int, int, string> progress = null)
        {
            int updated = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            var requests = new List<ExifWriteRequest>();
            var itemsToReset = new List<ManualMetadataItem>();
            if (progress != null) progress(0, total, "Starting metadata step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                var item = items[i];
                var file = item.FilePath;
                var remaining = total - (i + 1);
                if (!File.Exists(file))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var effectiveTime = item.UseCustomCaptureTime ? item.CaptureTime : GetLibraryDate(file);
                var preserveFileTimes = !item.UseCustomCaptureTime;
                var writeDateMetadata = ManualMetadataTouchesCaptureTime(item);
                var writeCommentMetadata = ManualMetadataTouchesComment(item);
                var writeTagMetadata = item.ForceTagMetadataWrite || ManualMetadataTouchesTags(item);
                if (!writeDateMetadata && !writeCommentMetadata && !writeTagMetadata)
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | unchanged | " + item.FileName);
                    continue;
                }
                var extraTags = BuildManualMetadataExtraTags(item);
                var changeNotes = new List<string>();
                if (writeDateMetadata) changeNotes.Add("date/time");
                if (writeCommentMetadata) changeNotes.Add("comment");
                if (writeTagMetadata) changeNotes.Add("tags");
                var metadataTarget = effectiveTime.ToString("yyyy-MM-dd HH:mm:ss") + (preserveFileTimes ? " (using filesystem timestamp)" : " (custom)");
                Log("Updating manual metadata: " + item.FileName + " -> " + metadataTarget + " [" + string.Join(", ", changeNotes.ToArray()) + "]");
                var originalCreate = DateTime.MinValue;
                var originalWrite = DateTime.MinValue;
                var restoreFileTimes = writeDateMetadata && preserveFileTimes;
                if (restoreFileTimes)
                {
                    originalCreate = File.GetCreationTime(file);
                    originalWrite = File.GetLastWriteTime(file);
                }
                requests.Add(new ExifWriteRequest
                {
                    FilePath = file,
                    FileName = item.FileName,
                    Arguments = BuildExifArgs(file, effectiveTime, new string[0], extraTags, preserveFileTimes, item.Comment, item.AddPhotographyTag, writeDateMetadata, writeCommentMetadata, writeTagMetadata),
                    RestoreFileTimes = restoreFileTimes,
                    OriginalCreateTime = originalCreate,
                    OriginalWriteTime = originalWrite,
                    SuccessDetail = item.FileName + " [" + string.Join(", ", changeNotes.ToArray()) + "]"
                });
                itemsToReset.Add(item);
            }
            updated = RunExifWriteRequests(requests, total, skipped, progress);
            foreach (var item in itemsToReset) item.ForceTagMetadataWrite = false;
            if (progress != null) progress(total, total, "Metadata step complete: updated " + updated + ", skipped " + skipped + ".");
            Log("Manual metadata summary: updated " + updated + ", skipped " + skipped + ".");
        }
        void RunDelete(List<ReviewItem> reviewItems)
        {
            int deleted = 0, skipped = 0;
            foreach (var item in reviewItems.Where(i => i.DeleteBeforeProcessing))
            {
                if (!File.Exists(item.FilePath)) { skipped++; continue; }
                File.Delete(item.FilePath);
                deleted++;
                Log("Deleted before processing: " + item.FileName);
            }
            if (deleted > 0 || skipped > 0) Log("Delete summary: deleted " + deleted + ", skipped " + skipped + ".");
        }

        int RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null)
        {
            var workItems = requests ?? new List<ExifWriteRequest>();
            if (workItems.Count == 0) return 0;

            var completed = alreadyCompleted;
            var failures = new ConcurrentQueue<Exception>();
            var workerCount = GetMetadataWorkerCount(workItems.Count);
            Log("Running metadata writes with " + workerCount + " worker(s) for " + workItems.Count + " file(s).");

            Parallel.ForEach(workItems, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, delegate(ExifWriteRequest request)
            {
                try
                {
                    RunExe(exifToolPath, request.Arguments, Path.GetDirectoryName(exifToolPath), false);
                    if (request.RestoreFileTimes)
                    {
                        if (request.OriginalCreateTime != DateTime.MinValue) File.SetCreationTime(request.FilePath, request.OriginalCreateTime);
                        if (request.OriginalWriteTime != DateTime.MinValue) File.SetLastWriteTime(request.FilePath, request.OriginalWriteTime);
                    }
                    if (progress != null)
                    {
                        var current = Interlocked.Increment(ref completed);
                        var remaining = Math.Max(totalCount - current, 0);
                        progress(current, totalCount, "Updated metadata " + current + " of " + totalCount + " | " + remaining + " remaining | " + request.SuccessDetail);
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(new InvalidOperationException("Metadata update failed for " + request.FileName + ". " + ex.Message, ex));
                }
            });

            if (!failures.IsEmpty) throw new AggregateException(failures);
            return workItems.Count;
        }

        void RunMetadata(List<ReviewItem> reviewItems)
        {
            int updated = 0, skipped = 0;
            var requests = new List<ExifWriteRequest>();
            foreach (var item in reviewItems)
            {
                if (item.DeleteBeforeProcessing) { skipped++; continue; }
                var file = item.FilePath;
                if (!File.Exists(file)) { skipped++; continue; }
                var selectedPlatformTags = new List<string>();
                if (item.TagSteam)
                {
                    selectedPlatformTags.Add("Steam");
                }
                if (item.TagPs5)
                {
                    selectedPlatformTags.Add("PS5");
                    selectedPlatformTags.Add("PlayStation");
                }
                if (item.TagXbox) selectedPlatformTags.Add("Xbox");
                if (selectedPlatformTags.Count == 0 && item.PlatformTags != null) selectedPlatformTags.AddRange(item.PlatformTags);
                var platformTags = selectedPlatformTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var metadataTarget = item.CaptureTime.ToString("yyyy-MM-dd HH:mm:ss") + (item.PreserveFileTimes ? " (preserving file timestamps)" : string.Empty);
                var notes = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.Comment)) notes.Add("comment added");
                if (item.AddPhotographyTag) notes.Add(GamePhotographyTag + " tag added");
                var noteSuffix = notes.Count > 0 ? " [" + string.Join(", ", notes.ToArray()) + "]" : string.Empty;
                Log("Updating metadata: " + item.FileName + " -> " + metadataTarget + (platformTags.Length > 0 ? " [" + string.Join(", ", platformTags) + "]" : " [no platform tag]") + noteSuffix);
                var originalCreate = DateTime.MinValue;
                var originalWrite = DateTime.MinValue;
                if (item.PreserveFileTimes)
                {
                    originalCreate = File.GetCreationTime(file);
                    originalWrite = File.GetLastWriteTime(file);
                }
                requests.Add(new ExifWriteRequest
                {
                    FilePath = file,
                    FileName = item.FileName,
                    Arguments = BuildExifArgs(file, item.CaptureTime, platformTags, item.PreserveFileTimes, item.Comment, item.AddPhotographyTag),
                    RestoreFileTimes = item.PreserveFileTimes,
                    OriginalCreateTime = originalCreate,
                    OriginalWriteTime = originalWrite,
                    SuccessDetail = item.FileName
                });
            }
            updated = RunExifWriteRequests(requests, requests.Count + skipped, skipped, null);
            Log("Metadata summary: updated " + updated + ", skipped " + skipped + ".");
        }

        List<UndoImportEntry> RunMove()
        {
            return RunMove(BuildSourceInventory(false).TopLevelMediaFiles, null);
        }

        List<UndoImportEntry> RunMove(HashSet<string> skipFiles)
        {
            return RunMove(BuildSourceInventory(false).TopLevelMediaFiles, skipFiles);
        }

        List<UndoImportEntry> RunMove(IEnumerable<string> sourceFiles, HashSet<string> skipFiles)
        {
            var files = (sourceFiles ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Where(file => skipFiles == null || !skipFiles.Contains(file));
            return RunMoveFiles(files, "Move summary");
        }

        List<UndoImportEntry> RunMoveFiles(IEnumerable<string> files, string summaryLabel)
        {
            int moved = 0, skipped = 0, renamedConflict = 0;
            var entries = new List<UndoImportEntry>();
            foreach (var file in files.Where(File.Exists))
            {
                var sourceDirectory = Path.GetDirectoryName(file);
                var target = Path.Combine(destinationRoot, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    var mode = Convert.ToString(conflictBox.SelectedItem ?? "Rename");
                    if (mode == "Skip") { skipped++; continue; }
                    if (mode == "Rename") { target = Unique(target); renamedConflict++; }
                    if (mode == "Overwrite") File.Delete(target);
                }
                File.Move(file, target);
                MoveMetadataSidecarIfPresent(file, target);
                moved++;
                entries.Add(new UndoImportEntry { SourceDirectory = sourceDirectory, ImportedFileName = Path.GetFileName(target), CurrentPath = target });
                AddSidecarUndoEntryIfPresent(target, sourceDirectory, entries);
                Log("Moved: " + Path.GetFileName(file) + " -> " + target);
            }
            Log(summaryLabel + ": moved " + moved + ", skipped " + skipped + ", renamed-on-conflict " + renamedConflict + ".");
            return entries;
        }

        void SortDestinationFolders()
        {
            try
            {
                SortDestinationFoldersCore(true);
            }
            catch (Exception ex)
            {
                status.Text = "Sort failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void SortDestinationFoldersCore(bool interactive)
        {
            EnsureDir(destinationRoot, "Destination folder");
            var files = Directory.EnumerateFiles(destinationRoot, "*", SearchOption.TopDirectoryOnly).Where(IsMedia).ToList();
            if (files.Count == 0)
            {
                status.Text = "Nothing to sort";
                Log("Sort destination found no root-level media files to organize.");
                if (interactive) MessageBox.Show("There are no root-level media files in the destination folder to sort right now.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int moved = 0, created = 0, renamedConflict = 0;
            var indexedTargets = new List<string>();
            foreach (var file in files)
            {
                var folderName = GetSafeGameFolderName(GetGameNameFromFileName(Path.GetFileNameWithoutExtension(file)));
                var targetDirectory = Path.Combine(destinationRoot, folderName);
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    created++;
                }

                var target = Path.Combine(targetDirectory, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    target = Unique(target);
                    renamedConflict++;
                }

                File.Move(file, target);
                MoveMetadataSidecarIfPresent(file, target);
                moved++;
                indexedTargets.Add(target);
                Log("Sorted: " + Path.GetFileName(file) + " -> " + target);
            }

            UpsertLibraryMetadataIndexEntries(indexedTargets, libraryRoot);
            status.Text = "Destination sorted";
            Log("Sort summary: sorted " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ".");
            RefreshPreview();
        }

        void UndoLastImport()
        {
            try
            {
                var entries = LoadUndoManifest();
                if (entries.Count == 0)
                {
                    status.Text = "Nothing to undo";
                    Log("Undo requested, but there is no saved import manifest.");
                    MessageBox.Show("There is no saved import to undo yet.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(entries.Count + " imported item(s) will be moved back to their source folders. Embedded metadata changes and comments will stay in the files.\n\nContinue?", "Undo Last Import", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.OK) return;

                int moved = 0, skipped = 0;
                var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var remaining = new List<UndoImportEntry>();
                var removedFromLibrary = new List<string>();
                foreach (var entry in entries)
                {
                    var currentPath = ResolveUndoCurrentPath(entry, usedPaths);
                    if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
                    {
                        skipped++;
                        remaining.Add(entry);
                        Log("Undo skipped: could not find " + entry.ImportedFileName + " in the destination/library folders.");
                        continue;
                    }

                    Directory.CreateDirectory(entry.SourceDirectory);
                    var target = Unique(Path.Combine(entry.SourceDirectory, Path.GetFileName(currentPath)));
                    File.Move(currentPath, target);
                    moved++;
                    removedFromLibrary.Add(currentPath);
                    Log("Undo move: " + currentPath + " -> " + target);
                }

                RemoveLibraryMetadataIndexEntries(removedFromLibrary, libraryRoot);
                SaveUndoManifest(remaining);
                status.Text = moved > 0 ? "Last import undone" : "Undo incomplete";
                Log("Undo summary: moved back " + moved + ", skipped " + skipped + ".");
                RefreshPreview();
            }
            catch (Exception ex)
            {
                status.Text = "Undo failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        string ResolveUndoCurrentPath(UndoImportEntry entry, HashSet<string> usedPaths)
        {
            if (entry == null) return null;
            if (!string.IsNullOrWhiteSpace(entry.CurrentPath) && File.Exists(entry.CurrentPath))
            {
                var fullCurrent = Path.GetFullPath(entry.CurrentPath);
                if (usedPaths.Add(fullCurrent)) return fullCurrent;
            }

            foreach (var root in new[] { destinationRoot, libraryRoot }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var candidate in Directory.EnumerateFiles(root, entry.ImportedFileName, SearchOption.AllDirectories)
                    .OrderByDescending(path => File.GetLastWriteTime(path)))
                {
                    var fullCandidate = Path.GetFullPath(candidate);
                    if (usedPaths.Add(fullCandidate)) return fullCandidate;
                }
            }
            return null;
        }

        List<UndoImportEntry> LoadUndoManifest()
        {
            var entries = new List<UndoImportEntry>();
            if (!File.Exists(undoManifestPath)) return entries;
            foreach (var line in File.ReadAllLines(undoManifestPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 3) continue;
                entries.Add(new UndoImportEntry { SourceDirectory = parts[0], ImportedFileName = parts[1], CurrentPath = parts[2] });
            }
            return entries;
        }

        void SaveUndoManifest(List<UndoImportEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                if (File.Exists(undoManifestPath)) File.Delete(undoManifestPath);
                return;
            }

            File.WriteAllLines(undoManifestPath, entries.Select(entry => string.Join("\t", new[]
            {
                entry.SourceDirectory ?? string.Empty,
                entry.ImportedFileName ?? string.Empty,
                entry.CurrentPath ?? string.Empty
            })).ToArray());
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
        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, bool preserveFileTimes, string comment, bool addPhotographyTag)
        {
            return BuildExifArgs(file, dt, platformTags, null, preserveFileTimes, comment, addPhotographyTag, true, true, true);
        }

        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag)
        {
            return BuildExifArgs(file, dt, platformTags, extraTags, preserveFileTimes, comment, addPhotographyTag, true, true, true);
        }

        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag, bool writeDateMetadata, bool writeCommentMetadata, bool writeTagMetadata)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var targetPath = IsVideo(file) ? MetadataSidecarPath(file) : file;
            var args = new List<string>();
            var png = dt.ToString("yyyy:MM:dd HH:mm:ss");
            var std = dt.ToString("yyyyMMdd HH:mm:ss");
            if (writeDateMetadata)
            {
                if (IsVideo(file))
                {
                    args.Add("-XMP:DateTimeOriginal=" + std);
                    args.Add("-XMP:CreateDate=" + std);
                    args.Add("-XMP:ModifyDate=" + std);
                    args.Add("-XMP:MetadataDate=" + std);
                }
                else if (ext == ".png")
                {
                    args.Add("-PNG:CreationTime=" + png);
                    args.Add("-PNG:ModifyDate=" + png);
                    args.Add("-XMP:DateTimeOriginal=" + std);
                    args.Add("-XMP:CreateDate=" + std);
                    args.Add("-XMP:ModifyDate=" + std);
                    args.Add("-XMP:MetadataDate=" + std);
                }
                else
                {
                    args.Add("-EXIF:DateTimeOriginal=" + std);
                    args.Add("-EXIF:CreateDate=" + std);
                    args.Add("-EXIF:ModifyDate=" + std);
                    args.Add("-XMP:DateTimeOriginal=" + std);
                    args.Add("-XMP:CreateDate=" + std);
                    args.Add("-XMP:ModifyDate=" + std);
                    args.Add("-XMP:MetadataDate=" + std);
                }
                if (!preserveFileTimes && !IsVideo(file))
                {
                    args.Add("-File:FileCreateDate=" + std);
                    args.Add("-File:FileModifyDate=" + std);
                }
            }
            var cleanedComment = CleanComment(comment);
            if (writeCommentMetadata && !string.IsNullOrWhiteSpace(cleanedComment))
            {
                args.Add("-XMP-dc:Description-x-default=" + cleanedComment);
                args.Add("-XMP-dc:Description=" + cleanedComment);
                args.Add("-XMP-exif:UserComment=" + cleanedComment);
                if (!IsVideo(file))
                {
                    args.Add("-EXIF:ImageDescription=" + cleanedComment);
                    args.Add("-EXIF:UserComment=" + cleanedComment);
                    args.Add("-IPTC:Caption-Abstract=" + cleanedComment);
                    if (ext == ".png") args.Add("-PNG:Comment=" + cleanedComment);
                }
            }
            if (writeTagMetadata)
            {
                var tags = BuildMetadataTagSet(platformTags, extraTags, addPhotographyTag);
                var serializedTags = string.Join("||", tags);
                args.Add("-sep");
                args.Add("||");
                args.Add("-XMP:Subject=" + serializedTags);
                args.Add("-XMP-dc:Subject=" + serializedTags);
                args.Add("-XMP:TagsList=" + serializedTags);
                args.Add("-XMP-digiKam:TagsList=" + serializedTags);
                args.Add("-XMP-lr:HierarchicalSubject=" + serializedTags);
                if (!IsVideo(file))
                {
                    args.Add("-IPTC:Keywords=" + serializedTags);
                    args.Add("-Keywords=" + serializedTags);
                }
            }
            args.Add("-overwrite_original");
            args.Add(targetPath);
            return args.ToArray();
        }

        bool ShouldIncludeGameCaptureKeywords()
        {
            bool includeGameCaptureKeywords = true;
            if (keywordsBox != null)
            {
                if (keywordsBox.Dispatcher.CheckAccess()) includeGameCaptureKeywords = keywordsBox.IsChecked == true;
                else includeGameCaptureKeywords = (bool)keywordsBox.Dispatcher.Invoke(new Func<bool>(delegate { return keywordsBox.IsChecked == true; }));
            }
            return includeGameCaptureKeywords;
        }

        string[] BuildMetadataTagSet(IEnumerable<string> platformTags, IEnumerable<string> extraTags, bool addPhotographyTag)
        {
            var tags = new List<string>();
            if (ShouldIncludeGameCaptureKeywords())
            {
                tags.Add("Game Capture");
                if (platformTags != null) tags.AddRange(platformTags.Where(tag => !string.IsNullOrWhiteSpace(tag)));
            }
            if (extraTags != null) tags.AddRange(extraTags.Where(tag => !string.IsNullOrWhiteSpace(tag)));
            if (addPhotographyTag) tags.Add(GamePhotographyTag);
            return tags.Select(CleanTag).Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        List<string> BuildManualMetadataExtraTags(ManualMetadataItem item)
        {
            var extraTags = new List<string>(ParseTagText(item == null ? string.Empty : item.TagText));
            if (item == null) return extraTags;
            if (item.TagSteam) extraTags.Add("Steam");
            if (item.TagPc) extraTags.Add("PC");
            if (item.TagPs5) { extraTags.Add("PS5"); extraTags.Add("PlayStation"); }
            if (item.TagXbox) extraTags.Add("Xbox");
            if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) extraTags.Add(CustomPlatformPrefix + CleanTag(item.CustomPlatformTag));
            return extraTags;
        }
        void ShowLibraryBrowser(bool reuseMainWindow = false)
        {
            try
            {
                EnsureDir(libraryRoot, "Library folder");
                var folders = LoadLibraryFoldersCached(libraryRoot, false);
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
                var leftHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition());
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var titleStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                titleRow.Children.Add(new TextBlock { Text = "Game Library", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 12, 0) });
                var folderCount = new TextBlock { Text = folders.Count + " folders", Foreground = Brush("#B7C6C0"), VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
                titleRow.Children.Add(folderCount);
                titleStack.Children.Add(titleRow);
                status = new TextBlock { Text = "Ready", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new Border { Child = status, Background = Brush("#20343A"), CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
                leftHeader.Children.Add(titleStack);
                var settingsButton = Btn("Settings", null, "#20343A", Brushes.White);
                settingsButton.Margin = new Thickness(18, 0, 0, 0);
                Grid.SetColumn(settingsButton, 1);
                leftHeader.Children.Add(settingsButton);
                var photographyButton = Btn("Photography", null, "#20343A", Brushes.White);
                photographyButton.Margin = new Thickness(18, 0, 18, 0);
                Grid.SetColumn(photographyButton, 2);
                leftHeader.Children.Add(photographyButton);
                var refreshButton = Btn("Refresh", null, "#20343A", Brushes.White);
                var rebuildLibraryButton = Btn("Rebuild", null, "#2E4751", Brushes.White);
                var fetchButton = Btn("Fetch Covers", null, "#275D47", Brushes.White);
                refreshButton.Margin = new Thickness(12, 0, 0, 0);
                rebuildLibraryButton.Margin = new Thickness(12, 0, 0, 0);
                fetchButton.Margin = new Thickness(12, 0, 0, 0);
                var headerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                headerActions.Children.Add(refreshButton);
                headerActions.Children.Add(rebuildLibraryButton);
                headerActions.Children.Add(fetchButton);
                Grid.SetColumn(headerActions, 3);
                leftHeader.Children.Add(headerActions);
                leftGrid.Children.Add(leftHeader);

                var filterGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                filterGrid.Children.Add(new TextBlock { Text = "Search", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                var searchBox = new TextBox { Padding = new Thickness(10, 6, 10, 6), Background = Brush("#182129"), Foreground = Brush("#F1E9DA"), BorderBrush = Brush("#2D3A43"), BorderThickness = new Thickness(1), FontSize = 13 };
                Grid.SetColumn(searchBox, 1);
                filterGrid.Children.Add(searchBox);
                filterGrid.Children.Add(new TextBlock { Text = "Folder size", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 10, 0) });
                Grid.SetColumn(filterGrid.Children[filterGrid.Children.Count - 1], 3);
                var folderTileSizeSlider = new Slider { Minimum = 140, Maximum = 340, Value = NormalizeLibraryFolderTileSize(libraryFolderTileSize), Width = 170, TickFrequency = 10, IsSnapToTickEnabled = true };
                Grid.SetColumn(folderTileSizeSlider, 4);
                filterGrid.Children.Add(folderTileSizeSlider);
                var folderTileSizeValue = new TextBlock { Text = NormalizeLibraryFolderTileSize(libraryFolderTileSize).ToString(), Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 40 };
                Grid.SetColumn(folderTileSizeValue, 5);
                filterGrid.Children.Add(folderTileSizeValue);
                Grid.SetRow(filterGrid, 1);
                leftGrid.Children.Add(filterGrid);

                var tileScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 16, 0, 0) };
                var tilePanel = new StackPanel();
                tileScroll.Content = tilePanel;
                Grid.SetRow(tileScroll, 2);
                leftGrid.Children.Add(tileScroll);
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
                var sizeValue = new TextBlock { Text = "320", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 40 };
                var thumbSizeSlider = new Slider { Minimum = 140, Maximum = 500, Value = 320, Width = 170, TickFrequency = 20, IsSnapToTickEnabled = true };
                sliderPanel.Children.Add(sliderLabel);
                sliderPanel.Children.Add(thumbSizeSlider);
                sliderPanel.Children.Add(sizeValue);
                DockPanel.SetDock(sliderPanel, Dock.Right);
                controls.Children.Add(sliderPanel);
                Grid.SetRow(controls, 1);
                rightGrid.Children.Add(controls);

                var thumbScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#0F151A") };
                var thumbContent = new StackPanel();
                thumbScroll.Content = thumbContent;
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
                Action<List<LibraryFolderInfo>, string> runScopedCoverRefresh = null;

                openSingleFileMetadataEditor = delegate(string filePath)
                {
                    if (current == null || string.IsNullOrWhiteSpace(filePath))
                    {
                        MessageBox.Show("Choose a capture first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var selectedItems = BuildLibraryMetadataItems(current)
                        .Where(item => string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (selectedItems.Count == 0)
                    {
                        MessageBox.Show("That capture could not be loaded for metadata editing.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    status.Text = "Editing selected capture metadata";
                    Log("Opening single-capture metadata editor for " + Path.GetFileName(filePath) + ".");
                    if (!ShowManualMetadataWindow(selectedItems, true, Path.GetFileName(filePath)))
                    {
                        status.Text = "Library metadata unchanged";
                        return;
                    }
                    var currentFolderPath = current.FolderPath;
                    var currentPlatformLabel = current.PlatformLabel;
                    var currentName = current.Name;
                    RunLibraryMetadataWorkflowWithProgress(current, selectedItems, delegate
                    {
                        current = string.IsNullOrWhiteSpace(currentFolderPath)
                            ? null
                            : new LibraryFolderInfo { FolderPath = currentFolderPath, PlatformLabel = currentPlatformLabel ?? string.Empty, Name = currentName ?? string.Empty };
                        folders = LoadLibraryFoldersCached(libraryRoot, true);
                        renderTiles(false);
                    });
                };

                Action renderSelectedFolder = delegate
                {
                    thumbContent.Children.Clear();
                    if (current == null) return;
                    var size = (int)thumbSizeSlider.Value;
                    sizeValue.Text = size.ToString();
                    var groups = GetFilesForLibraryFolderEntry(current, false)
                        .OrderByDescending(GetLibraryDate)
                        .GroupBy(f => GetLibraryDate(f).Date)
                        .OrderByDescending(g => g.Key)
                        .ToList();
                    if (groups.Count == 0)
                    {
                        thumbContent.Children.Add(new TextBlock { Text = "No captures found in this folder.", Foreground = Brush("#A7B5BD") });
                        return;
                    }
                    foreach (var group in groups)
                    {
                        thumbContent.Children.Add(new TextBlock
                        {
                            Text = group.Key.ToString("MMMM d, yyyy"),
                            FontSize = 16,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = Brush("#F1E9DA"),
                            Margin = new Thickness(0, 0, 0, 10)
                        });
                        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
                        foreach (var file in group)
                        {
                            var tile = new Border { Width = size, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Tag = file };
                            var presenter = new Grid();
                            var placeholder = new TextBlock { Text = Path.GetFileName(file), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), Foreground = Brush("#F1E9DA"), TextAlignment = TextAlignment.Center };
                            var image = new Image { Width = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
                            presenter.Children.Add(placeholder);
                            presenter.Children.Add(image);
                            tile.Child = presenter;
                            QueueImageLoad(image, file, size * 2, delegate(BitmapImage loaded)
                            {
                                image.Source = loaded;
                                image.Visibility = Visibility.Visible;
                                placeholder.Visibility = Visibility.Collapsed;
                            });
                            tile.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
                            {
                                if (e.ClickCount >= 2)
                                {
                                    var clicked = sender as Border;
                                    if (clicked != null && clicked.Tag is string) OpenWithShell((string)clicked.Tag);
                                }
                            };
                            var contextMenu = new ContextMenu();
                            var openItem = new MenuItem { Header = "Open" };
                            openItem.Click += delegate { OpenWithShell(file); };
                            var openFolderItem = new MenuItem { Header = "Open Folder" };
                            openFolderItem.Click += delegate { OpenFolder(Path.GetDirectoryName(file) ?? string.Empty); };
                            var editItem = new MenuItem { Header = "Edit Metadata" };
                            editItem.Click += delegate { openSingleFileMetadataEditor(file); };
                            var copyPathItem = new MenuItem { Header = "Copy File Path" };
                            copyPathItem.Click += delegate
                            {
                                try { Clipboard.SetText(file); } catch { }
                            };
                            contextMenu.Items.Add(openItem);
                            contextMenu.Items.Add(openFolderItem);
                            contextMenu.Items.Add(editItem);
                            contextMenu.Items.Add(new Separator());
                            contextMenu.Items.Add(copyPathItem);
                            tile.ContextMenu = contextMenu;
                            wrap.Children.Add(tile);
                        }
                        thumbContent.Children.Add(wrap);
                    }
                };

                Action<LibraryFolderInfo> showFolder = delegate(LibraryFolderInfo info)
                {
                    current = info;
                    detailTitle.Text = info.Name;
                    detailMeta.Text = info.FileCount + " item(s) | " + info.PlatformLabel + " | " + info.FolderPath;
                    previewImage.Source = null;
                    QueueImageLoad(previewImage, ResolveLibraryArt(info, false), 720, delegate(BitmapImage loaded)
                    {
                        previewImage.Source = loaded;
                    });
                    renderSelectedFolder();
                };

                renderTiles = delegate(bool forceRefresh)
                {
                    folders = LoadLibraryFoldersCached(libraryRoot, forceRefresh);
                    var searchText = string.IsNullOrWhiteSpace(searchBox.Text) ? string.Empty : searchBox.Text.Trim();
                    var visibleFolders = string.IsNullOrWhiteSpace(searchText)
                        ? folders
                        : folders.Where(folder =>
                            (!string.IsNullOrWhiteSpace(folder.Name) && folder.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.PlatformLabel) && folder.PlatformLabel.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.FolderPath) && folder.FolderPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrWhiteSpace(folder.GameId) && folder.GameId.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                        .ToList();
                    folderCount.Text = string.IsNullOrWhiteSpace(searchText)
                        ? folders.Count + " folders"
                        : visibleFolders.Count + " of " + folders.Count + " folders";
                    var tileWidth = (int)folderTileSizeSlider.Value;
                    var tileHeight = (int)Math.Round(tileWidth * 1.5d);
                    folderTileSizeValue.Text = tileWidth.ToString();
                    tilePanel.Children.Clear();
                    var folderGroups = visibleFolders
                        .GroupBy(folder => DetermineLibraryFolderGroup(folder))
                        .OrderBy(group => PlatformGroupOrder(group.Key))
                        .ThenBy(group => group.Key)
                        .ToList();
                    if (folderGroups.Count == 0)
                    {
                        tilePanel.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(searchText) ? "No library folders found." : "No folders match the current search.", Foreground = Brush("#A7B5BD"), Margin = new Thickness(0, 12, 0, 0) });
                        return;
                    }
                    foreach (var folderGroup in folderGroups)
                    {
                        var groupWrap = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
                        foreach (var folder in folderGroup.OrderBy(f => f.Name))
                        {
                            var tile = new Button
                            {
                                Width = tileWidth,
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
                            tileStack.Children.Add(imageBorder);
                            tileStack.Children.Add(new TextBlock { Text = folder.Name, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(12, 12, 12, 4), FontWeight = FontWeights.SemiBold, FontSize = 13 });
                            tileStack.Children.Add(new TextBlock { Text = folder.FileCount + " item(s) | " + folder.PlatformLabel, Foreground = Brush("#B7C6C0"), Margin = new Thickness(12, 0, 12, 12), FontSize = 10.5 });
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
                            var refreshFolderItem = new MenuItem { Header = "Refresh Folder" };
                            refreshFolderItem.Click += delegate { runLibraryScan(folder.FolderPath, false); };
                            var rebuildFolderItem = new MenuItem { Header = "Rebuild Folder" };
                            rebuildFolderItem.Click += delegate { runLibraryScan(folder.FolderPath, true); };
                            var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art" };
                            fetchFolderCoverItem.Click += delegate
                            {
                                showFolder(folder);
                                runScopedCoverRefresh(new List<LibraryFolderInfo> { folder }, folder.Name + " | " + folder.PlatformLabel);
                            };
                            contextMenu.Items.Add(openFolderItem);
                            contextMenu.Items.Add(editMetadataItem);
                            contextMenu.Items.Add(new Separator());
                            contextMenu.Items.Add(refreshFolderItem);
                            contextMenu.Items.Add(rebuildFolderItem);
                            contextMenu.Items.Add(fetchFolderCoverItem);
                            contextMenu.Items.Add(new Separator());
                            contextMenu.Items.Add(setCoverItem);
                            contextMenu.Items.Add(clearCoverItem);
                            tile.ContextMenu = contextMenu;
                            groupWrap.Children.Add(tile);
                        }

                        var headerGrid = new Grid();
                        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
                        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        headerGrid.Children.Add(new TextBlock { Text = folderGroup.Key, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
                        var countBadge = new Border { Background = Brush("#20343A"), CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 3), Child = new TextBlock { Text = folderGroup.Count().ToString(), Foreground = Brush("#B7C6C0"), FontSize = 12, FontWeight = FontWeights.SemiBold } };
                        Grid.SetColumn(countBadge, 1);
                        headerGrid.Children.Add(countBadge);

                        var expander = new Expander
                        {
                            Header = headerGrid,
                            IsExpanded = true,
                            Margin = new Thickness(0, 0, 0, 14),
                            Foreground = Brushes.White,
                            Background = Brush("#161F24"),
                            BorderBrush = Brush("#26363F"),
                            BorderThickness = new Thickness(1),
                            Padding = new Thickness(14, 10, 14, 12),
                            Content = groupWrap
                        };
                        tilePanel.Children.Add(expander);
                    }
                    if (folders.Count > 0 && current == null) showFolder(folders[0]);
                    else if (current != null)
                    {
                        var match = folders.FirstOrDefault(f => f.FolderPath == current.FolderPath && string.Equals(f.PlatformLabel, current.PlatformLabel, StringComparison.OrdinalIgnoreCase));
                        if (match == null) match = folders.FirstOrDefault(f => f.FolderPath == current.FolderPath);
                        if (match != null) showFolder(match);
                    }
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
                        photographyButton.IsEnabled = true;
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
                        photographyButton.IsEnabled = false;
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
                                    renderTiles(true);
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

                runScopedCoverRefresh = delegate(List<LibraryFolderInfo> requestedFolders, string scopeLabel)
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
                        photographyButton.IsEnabled = true;
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
                        var progressTitle = new TextBlock { Text = "Resolving AppIDs and fetching cover art", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
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
                        status.Text = targetFolders.Count == 1 ? "Resolving AppID and fetching folder cover art" : "Resolving AppIDs and fetching cover art";
                        refreshButton.IsEnabled = false;
                        rebuildLibraryButton.IsEnabled = false;
                        scanFolderButton.IsEnabled = false;
                        editMetadataButton.IsEnabled = false;
                        fetchButton.IsEnabled = false;
                        photographyButton.IsEnabled = false;
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
                            }, delegate { return cancelRequested; }, out resolved, out coversReady);
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
                                    Log((targetFolders.Count == 1 ? "Folder" : "Library") + " cover art refresh complete for " + resolvedScopeLabel + ". Resolved " + resolved + " Steam AppID entr" + (resolved == 1 ? "y" : "ies") + "; " + coversReady + " title" + (coversReady == 1 ? " has" : "s have") + " cover art ready.");
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
                    runScopedCoverRefresh(folders, "library");
                };

                refreshButton.Click += delegate { runLibraryScan(null, false); };
                rebuildLibraryButton.Click += delegate { runLibraryScan(null, true); };
                settingsButton.Click += delegate { ShowSettingsWindow(); };
                photographyButton.Click += delegate { ShowPhotographyGallery(libraryWindow); };
                fetchButton.Click += delegate { runCoverRefresh(); };
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
                        folders = LoadLibraryFoldersCached(libraryRoot, true);
                        renderTiles(false);
                    });
                };
                editMetadataButton.Click += delegate { openLibraryMetadataEditor(current); };
                thumbSizeSlider.ValueChanged += delegate { if (current != null) renderSelectedFolder(); };
                folderTileSizeSlider.ValueChanged += delegate
                {
                    libraryFolderTileSize = NormalizeLibraryFolderTileSize((int)Math.Round(folderTileSizeSlider.Value));
                    if (folderTileSizeValue != null) folderTileSizeValue.Text = libraryFolderTileSize.ToString();
                    SaveSettings();
                    renderTiles(false);
                };
                searchBox.TextChanged += delegate { renderTiles(false); };

                renderTiles(false);
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
            var stamp = BuildLibraryFolderInventoryStamp(root);
            if (!forceRefresh)
            {
                var cached = LoadLibraryFolderCache(root, stamp);
                if (cached != null)
                {
                    if (ApplySavedGameIndexRows(root, cached))
                    {
                        SaveLibraryFolderCache(root, stamp, cached);
                    }
                    Log("Library folder cache hit.");
                    return cached;
                }
            }
            Log("Refreshing library folder cache.");
            var fresh = LoadLibraryFolders(root);
            ApplySavedGameIndexRows(root, fresh);
            SaveLibraryFolderCache(root, stamp, fresh);
            return fresh;
        }

        string BuildLibraryFolderInventoryStamp(string root)
        {
            long latestDirTicks = 0;
            int folderCount = 0;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                folderCount++;
                var dirTicks = Directory.GetLastWriteTimeUtc(dir).Ticks;
                if (dirTicks > latestDirTicks) latestDirTicks = dirTicks;
            }
            var metadataPath = LibraryMetadataIndexPath(root);
            long metadataTicks = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
            long metadataLength = File.Exists(metadataPath) ? new FileInfo(metadataPath).Length : 0;
            return folderCount + "|" + latestDirTicks + "|" + metadataTicks + "|" + metadataLength;
        }

        string LibraryFolderCachePath(string root)
        {
            return Path.Combine(cacheRoot, "library-folders-" + SafeCacheName(root) + ".cache");
        }

        string GameIndexPath(string root)
        {
            return Path.Combine(cacheRoot, "game-index-" + SafeCacheName(root) + ".cache");
        }

        string NormalizeGameId(string gameId)
        {
            return CleanTag(gameId);
        }

        int ParseGameIdSequence(string gameId)
        {
            var normalized = NormalizeGameId(gameId);
            if (string.IsNullOrWhiteSpace(normalized)) return 0;
            var match = Regex.Match(normalized, @"^G(?<n>\d+)$", RegexOptions.IgnoreCase);
            return match.Success ? ParseInt(match.Groups["n"].Value) : 0;
        }

        string FormatGameId(int number)
        {
            if (number < 1) number = 1;
            return "G" + number.ToString("00000");
        }

        string CreateGameId(IEnumerable<string> existingGameIds)
        {
            var nextNumber = (existingGameIds ?? Enumerable.Empty<string>()).Select(ParseGameIdSequence).DefaultIfEmpty(0).Max() + 1;
            return FormatGameId(nextNumber);
        }

        string NormalizeGameIndexName(string name, string folderPath = null)
        {
            var normalized = CleanTag(name);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
            if (!string.IsNullOrWhiteSpace(folderPath)) return CleanTag(Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)));
            return string.Empty;
        }

        string BuildGameIndexIdentity(string name, string platformLabel)
        {
            return NormalizeGameIndexName(name) + "|" + NormalizeConsoleLabel(platformLabel);
        }

        string BuildGameTitleChoiceLabel(string name, string platformLabel)
        {
            var normalizedName = NormalizeGameIndexName(name);
            if (string.IsNullOrWhiteSpace(normalizedName)) return string.Empty;
            return NormalizeConsoleLabel(string.IsNullOrWhiteSpace(platformLabel) ? "Other" : platformLabel).PadRight(12) + " | " + normalizedName;
        }

        string ExtractGameNameFromChoiceLabel(string value)
        {
            var cleaned = CleanTag(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            var separatorIndex = cleaned.IndexOf(" | ", StringComparison.Ordinal);
            return separatorIndex >= 0 && separatorIndex + 3 < cleaned.Length
                ? CleanTag(cleaned.Substring(separatorIndex + 3))
                : cleaned;
        }

        string BuildGameIndexMergeKey(GameIndexEditorRow row)
        {
            if (row == null) return string.Empty;
            var gameId = NormalizeGameId(row.GameId);
            return !string.IsNullOrWhiteSpace(gameId)
                ? "id:" + gameId
                : "identity:" + BuildGameIndexIdentity(row.Name, row.PlatformLabel);
        }

        string BuildLibraryFolderMasterKey(LibraryFolderInfo folder)
        {
            if (folder == null) return string.Empty;
            var gameId = NormalizeGameId(folder.GameId);
            return !string.IsNullOrWhiteSpace(gameId)
                ? "id:" + gameId
                : "identity:" + BuildGameIndexIdentity(folder.Name, folder.PlatformLabel);
        }

        GameIndexEditorRow CloneGameIndexEditorRow(GameIndexEditorRow row)
        {
            if (row == null) return null;
            return new GameIndexEditorRow
            {
                GameId = NormalizeGameId(row.GameId),
                Name = NormalizeGameIndexName(row.Name, row.FolderPath),
                PlatformLabel = NormalizeConsoleLabel(row.PlatformLabel),
                SteamAppId = CleanTag(row.SteamAppId),
                FileCount = Math.Max(0, row.FileCount),
                FolderPath = (row.FolderPath ?? string.Empty).Trim(),
                PreviewImagePath = (row.PreviewImagePath ?? string.Empty).Trim(),
                FilePaths = (row.FilePaths ?? new string[0]).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        Dictionary<string, string> BuildGameIdAliasMap(IEnumerable<GameIndexEditorRow> sourceRows, IEnumerable<GameIndexEditorRow> normalizedRows)
        {
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedByIdentity = (normalizedRows ?? Enumerable.Empty<GameIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(row => BuildGameIndexIdentity(row.Name, row.PlatformLabel), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => NormalizeGameId(group.First().GameId), StringComparer.OrdinalIgnoreCase);
            foreach (var row in sourceRows ?? Enumerable.Empty<GameIndexEditorRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Name)) continue;
                var oldGameId = NormalizeGameId(row.GameId);
                string newGameId;
                if (!normalizedByIdentity.TryGetValue(BuildGameIndexIdentity(row.Name, row.PlatformLabel), out newGameId)) continue;
                if (!string.IsNullOrWhiteSpace(oldGameId)) aliasMap[oldGameId] = newGameId;
                if (!string.IsNullOrWhiteSpace(newGameId)) aliasMap[newGameId] = newGameId;
            }
            foreach (var row in normalizedRows ?? Enumerable.Empty<GameIndexEditorRow>())
            {
                var gameId = NormalizeGameId(row == null ? string.Empty : row.GameId);
                if (!string.IsNullOrWhiteSpace(gameId)) aliasMap[gameId] = gameId;
            }
            return aliasMap;
        }

        bool HasGameIdAliasChanges(Dictionary<string, string> aliasMap)
        {
            return (aliasMap ?? new Dictionary<string, string>()).Any(pair => !string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase));
        }

        List<GameIndexEditorRow> EnsureGameIndexRowsHaveIds(IEnumerable<GameIndexEditorRow> rows)
        {
            var normalizedRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).Select(CloneGameIndexEditorRow).ToList();
            var groupedRows = normalizedRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(row => BuildGameIndexIdentity(row.Name, row.PlatformLabel), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var assignedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedNumbers = new HashSet<int>();
            foreach (var group in groupedRows)
            {
                var preferredNumber = group
                    .Select(row => ParseGameIdSequence(row.GameId))
                    .Where(number => number > 0)
                    .Distinct()
                    .OrderBy(number => number)
                    .FirstOrDefault(number => !usedNumbers.Contains(number));
                if (preferredNumber > 0)
                {
                    usedNumbers.Add(preferredNumber);
                    assignedIds[group.Key] = FormatGameId(preferredNumber);
                }
            }
            var nextNumber = usedNumbers.Count == 0 ? 1 : usedNumbers.Max() + 1;
            foreach (var group in groupedRows)
            {
                string assignedGameId;
                if (!assignedIds.TryGetValue(group.Key, out assignedGameId))
                {
                    while (usedNumbers.Contains(nextNumber)) nextNumber++;
                    assignedGameId = FormatGameId(nextNumber);
                    usedNumbers.Add(nextNumber);
                    assignedIds[group.Key] = assignedGameId;
                    nextNumber++;
                }
                foreach (var row in group) row.GameId = assignedGameId;
            }
            return normalizedRows;
        }

        List<GameIndexEditorRow> MergeGameIndexRows(IEnumerable<GameIndexEditorRow> rows)
        {
            var normalizedRows = EnsureGameIndexRowsHaveIds(rows).Where(row => !string.IsNullOrWhiteSpace(row.Name)).ToList();
            var mergedRows = new List<GameIndexEditorRow>();
            foreach (var group in normalizedRows.GroupBy(BuildGameIndexMergeKey, StringComparer.OrdinalIgnoreCase))
            {
                var groupRows = group.ToList();
                var preferredName = groupRows
                    .Select(row => NormalizeGameIndexName(row.Name, row.FolderPath))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var representative = groupRows
                    .OrderByDescending(row => !string.IsNullOrWhiteSpace(row.SteamAppId))
                    .ThenByDescending(row => (row.FilePaths ?? new string[0]).Length)
                    .ThenByDescending(row => row.FileCount)
                    .ThenBy(row => row.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .First();
                var mergedFilePaths = groupRows
                    .SelectMany(row => row.FilePaths ?? new string[0])
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var folderPath = groupRows
                    .Select(row => row.FolderPath ?? string.Empty)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    folderPath = groupRows
                    .Select(row => row.FolderPath ?? string.Empty)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
                }
                var previewPath = groupRows
                    .Select(row => row.PreviewImagePath ?? string.Empty)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
                if (string.IsNullOrWhiteSpace(previewPath)) previewPath = mergedFilePaths.FirstOrDefault(IsImage) ?? mergedFilePaths.FirstOrDefault() ?? string.Empty;
                mergedRows.Add(new GameIndexEditorRow
                {
                    GameId = NormalizeGameId(representative.GameId),
                    Name = preferredName ?? NormalizeGameIndexName(representative.Name, folderPath),
                    PlatformLabel = NormalizeConsoleLabel(representative.PlatformLabel),
                    SteamAppId = groupRows.Select(row => row.SteamAppId ?? string.Empty).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                    FileCount = mergedFilePaths.Length > 0 ? mergedFilePaths.Length : groupRows.Max(row => row.FileCount),
                    FolderPath = folderPath ?? string.Empty,
                    PreviewImagePath = previewPath,
                    FilePaths = mergedFilePaths
                });
            }
            return mergedRows
                .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.PlatformLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        bool PruneObsoleteMultipleTagsRows(List<GameIndexEditorRow> rows)
        {
            var rowList = rows ?? new List<GameIndexEditorRow>();
            var staleRows = rowList
                .Where(row => row != null
                    && string.Equals(NormalizeConsoleLabel(row.PlatformLabel), "Multiple Tags", StringComparison.OrdinalIgnoreCase)
                    && (row.FilePaths == null || row.FilePaths.Length == 0)
                    && row.FileCount <= 0
                    && string.IsNullOrWhiteSpace(row.FolderPath)
                    && !string.IsNullOrWhiteSpace(row.Name)
                    && rowList.Any(other => other != null
                        && !ReferenceEquals(other, row)
                        && string.Equals(NormalizeGameIndexName(other.Name), NormalizeGameIndexName(row.Name), StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(NormalizeConsoleLabel(other.PlatformLabel), "Multiple Tags", StringComparison.OrdinalIgnoreCase)
                        && (((other.FilePaths ?? new string[0]).Length > 0) || other.FileCount > 0)))
                .ToList();
            if (staleRows.Count == 0) return false;
            foreach (var staleRow in staleRows) rowList.Remove(staleRow);
            return true;
        }

        void RefreshCachedLibraryFoldersFromGameIndex(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            var folders = LoadLibraryFolders(root, LoadLibraryMetadataIndex(root, true));
            ApplySavedGameIndexRows(root, folders);
            SaveLibraryFolderCache(root, BuildLibraryFolderInventoryStamp(root), folders);
        }

        GameIndexEditorRow FindSavedGameIndexRowById(IEnumerable<GameIndexEditorRow> rows, string gameId)
        {
            var wantedId = NormalizeGameId(gameId);
            if (string.IsNullOrWhiteSpace(wantedId)) return null;
            return (rows ?? Enumerable.Empty<GameIndexEditorRow>()).FirstOrDefault(row =>
                row != null && string.Equals(NormalizeGameId(row.GameId), wantedId, StringComparison.OrdinalIgnoreCase));
        }

        GameIndexEditorRow FindSavedGameIndexRowByIdentity(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel)
        {
            var wantedIdentity = BuildGameIndexIdentity(name, platformLabel);
            return (rows ?? Enumerable.Empty<GameIndexEditorRow>()).FirstOrDefault(row =>
                row != null && string.Equals(BuildGameIndexIdentity(row.Name, row.PlatformLabel), wantedIdentity, StringComparison.OrdinalIgnoreCase));
        }

        List<GameIndexEditorRow> ReadSavedGameIndexRowsFile(string root)
        {
            var path = GameIndexPath(root);
            if (!File.Exists(path)) return new List<GameIndexEditorRow>();
            var lines = File.ReadAllLines(path);
            if (lines.Length < 1) return new List<GameIndexEditorRow>();
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return new List<GameIndexEditorRow>();
            var list = new List<GameIndexEditorRow>();
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 8)
                {
                    list.Add(new GameIndexEditorRow
                    {
                        GameId = parts[0],
                        FolderPath = parts[1],
                        Name = parts[2],
                        PlatformLabel = parts[3],
                        SteamAppId = parts[4],
                        FileCount = parts.Length > 5 ? ParseInt(parts[5]) : 0,
                        PreviewImagePath = parts.Length > 6 ? parts[6] : string.Empty,
                        FilePaths = parts.Length > 7 && !string.IsNullOrWhiteSpace(parts[7])
                            ? parts[7].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
                            : new string[0]
                    });
                }
                else if (parts.Length >= 4)
                {
                    list.Add(new GameIndexEditorRow
                    {
                        GameId = string.Empty,
                        FolderPath = parts[0],
                        Name = parts[1],
                        PlatformLabel = parts[2],
                        SteamAppId = parts[3],
                        FileCount = parts.Length > 4 ? ParseInt(parts[4]) : 0,
                        PreviewImagePath = parts.Length > 5 ? parts[5] : string.Empty,
                        FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
                            : new string[0]
                    });
                }
            }
            return list;
        }

        void WriteSavedGameIndexRowsFile(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var path = GameIndexPath(root);
            var lines = new List<string>();
            lines.Add(root);
            foreach (var row in rows.Where(row => row != null))
            {
                lines.Add(string.Join("\t", new[]
                {
                    NormalizeGameId(row.GameId),
                    row.FolderPath ?? string.Empty,
                    row.Name ?? string.Empty,
                    row.PlatformLabel ?? string.Empty,
                    row.SteamAppId ?? string.Empty,
                    row.FileCount.ToString(),
                    row.PreviewImagePath ?? string.Empty,
                    string.Join("|", (row.FilePaths ?? new string[0]).Where(File.Exists))
                }));
            }
            File.WriteAllLines(path, lines.ToArray());
        }

        Dictionary<string, string> BuildSavedGameIdAliasMapFromFile(string root)
        {
            var rawRows = ReadSavedGameIndexRowsFile(root);
            if (rawRows.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedRows = MergeGameIndexRows(rawRows);
            return BuildGameIdAliasMap(rawRows, normalizedRows);
        }

        void RewriteGameIdAliasesInLibraryMetadataIndexFile(string root, Dictionary<string, string> aliasMap)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            var path = LibraryMetadataIndexPath(root);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;
            bool changed = false;
            var rewritten = new List<string> { lines[0] };
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 5)
                {
                    string mappedGameId;
                    var currentGameId = NormalizeGameId(parts[2]);
                    if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId) && !string.Equals(parts[2], mappedGameId, StringComparison.Ordinal))
                    {
                        parts[2] = mappedGameId;
                        changed = true;
                    }
                    rewritten.Add(string.Join("\t", new[] { parts[0], parts[1], parts[2], parts[3], parts[4] }));
                }
                else
                {
                    rewritten.Add(line);
                }
            }
            if (changed)
            {
                File.WriteAllLines(path, rewritten.ToArray());
                if (string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in libraryMetadataIndex.Values.Where(entry => entry != null))
                    {
                        var currentGameId = NormalizeGameId(entry.GameId);
                        string mappedGameId;
                        if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId)) entry.GameId = mappedGameId;
                    }
                }
            }
        }

        void RewriteGameIdAliasesInLibraryFolderCacheFile(string root, Dictionary<string, string> aliasMap)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3) return;
            bool changed = false;
            var rewritten = new List<string> { lines[0], lines[1] };
            foreach (var line in lines.Skip(2))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 8)
                {
                    string mappedGameId;
                    var currentGameId = NormalizeGameId(parts[0]);
                    if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId) && !string.Equals(parts[0], mappedGameId, StringComparison.Ordinal))
                    {
                        parts[0] = mappedGameId;
                        changed = true;
                    }
                    rewritten.Add(string.Join("\t", parts));
                }
                else
                {
                    rewritten.Add(line);
                }
            }
            if (changed) File.WriteAllLines(path, rewritten.ToArray());
        }

        List<GameIndexEditorRow> LoadSavedGameIndexRows(string root)
        {
            var rawRows = ReadSavedGameIndexRowsFile(root);
            var normalizedRows = MergeGameIndexRows(rawRows);
            var aliasMap = BuildGameIdAliasMap(rawRows, normalizedRows);
            if (rawRows.Count > 0 && (HasGameIdAliasChanges(aliasMap) || normalizedRows.Count != rawRows.Count || rawRows.Any(row => string.IsNullOrWhiteSpace(row.GameId))))
            {
                WriteSavedGameIndexRowsFile(root, normalizedRows);
                RewriteGameIdAliasesInLibraryMetadataIndexFile(root, aliasMap);
                RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
            }
            return normalizedRows;
        }

        void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var sourceRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).Select(CloneGameIndexEditorRow).ToList();
            var mergedRows = MergeGameIndexRows(sourceRows);
            var aliasMap = BuildGameIdAliasMap(sourceRows, mergedRows);
            WriteSavedGameIndexRowsFile(root, mergedRows);
            if (HasGameIdAliasChanges(aliasMap))
            {
                RewriteGameIdAliasesInLibraryMetadataIndexFile(root, aliasMap);
                RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
            }
        }

        GameIndexEditorRow FindSavedGameIndexRow(IEnumerable<GameIndexEditorRow> rows, LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            var savedRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).ToList();
            var byGameId = FindSavedGameIndexRowById(savedRows, folder.GameId);
            if (byGameId != null) return byGameId;
            return FindSavedGameIndexRowByIdentity(savedRows, folder.Name, folder.PlatformLabel);
        }

        bool ApplySavedGameIndexRows(string root, List<LibraryFolderInfo> folders)
        {
            var savedRows = LoadSavedGameIndexRows(root);
            if (savedRows.Count == 0 || folders == null || folders.Count == 0) return false;
            bool changed = false;
            foreach (var folder in folders)
            {
                var saved = FindSavedGameIndexRow(savedRows, folder);
                if (saved == null) continue;
                if (!string.IsNullOrWhiteSpace(saved.GameId) && !string.Equals(folder.GameId ?? string.Empty, saved.GameId ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.GameId = saved.GameId;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(saved.Name) && !string.Equals(folder.Name ?? string.Empty, saved.Name ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.Name = saved.Name;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(saved.PlatformLabel) && !string.Equals(folder.PlatformLabel ?? string.Empty, saved.PlatformLabel ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.PlatformLabel = saved.PlatformLabel;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(saved.SteamAppId) && !string.Equals(folder.SteamAppId ?? string.Empty, saved.SteamAppId ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.SteamAppId = saved.SteamAppId;
                    changed = true;
                }
            }
            return changed;
        }

        void UpsertSavedGameIndexRow(string root, LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(root)) return;
            var rows = LoadSavedGameIndexRows(root);
            var saved = FindSavedGameIndexRow(rows, folder);
            var gameId = NormalizeGameId(folder.GameId);
            if (string.IsNullOrWhiteSpace(gameId))
            {
                var byIdentity = FindSavedGameIndexRowByIdentity(rows, folder.Name, folder.PlatformLabel);
                gameId = byIdentity == null ? CreateGameId(rows.Select(row => row.GameId)) : byIdentity.GameId;
            }
            if (saved == null)
            {
                rows.Add(new GameIndexEditorRow
                {
                    GameId = gameId,
                    FolderPath = folder.FolderPath ?? string.Empty,
                    Name = folder.Name ?? string.Empty,
                    PlatformLabel = folder.PlatformLabel ?? string.Empty,
                    SteamAppId = folder.SteamAppId ?? string.Empty,
                    FileCount = folder.FileCount,
                    PreviewImagePath = folder.PreviewImagePath ?? string.Empty,
                    FilePaths = folder.FilePaths ?? new string[0]
                });
            }
            else
            {
                saved.GameId = gameId;
                saved.Name = folder.Name ?? string.Empty;
                saved.PlatformLabel = folder.PlatformLabel ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) saved.SteamAppId = folder.SteamAppId;
                saved.FileCount = folder.FileCount;
                saved.PreviewImagePath = folder.PreviewImagePath ?? string.Empty;
                saved.FilePaths = folder.FilePaths ?? new string[0];
            }
            SaveSavedGameIndexRows(root, rows);
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
                if (parts.Length >= 8)
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
                        SteamAppId = parts.Length > 7 ? parts[7] : string.Empty
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
                        SteamAppId = parts.Length > 6 ? parts[6] : string.Empty
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
                    folder.SteamAppId ?? string.Empty
                }));
            }
            File.WriteAllLines(path, lines.ToArray());
        }

        void RebuildLibraryFolderCache(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                ClearLibraryFolderCache(root);
                return;
            }
            var fresh = LoadLibraryFolders(root, index);
            ApplySavedGameIndexRows(root, fresh);
            SaveLibraryFolderCache(root, BuildLibraryFolderInventoryStamp(root), fresh);
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

        string MetadataReadPath(string file)
        {
            var sidecar = MetadataSidecarPath(file);
            return !string.IsNullOrWhiteSpace(sidecar) && File.Exists(sidecar) ? sidecar : file;
        }

        string NormalizeExifToolPathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
            try
            {
                return Path.GetFullPath(normalized);
            }
            catch
            {
                return normalized;
            }
        }

        Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files)
        {
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var sourceFiles = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var file in sourceFiles) result[file] = new string[0];
            if (sourceFiles.Count == 0) return result;
            if (string.IsNullOrWhiteSpace(exifToolPath) || !File.Exists(exifToolPath)) return result;

            var readTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var targetToSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in sourceFiles)
            {
                var readTarget = MetadataReadPath(file);
                if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) continue;
                readTargets[file] = readTarget;
                targetToSource[NormalizeExifToolPathKey(readTarget)] = file;
            }
            if (readTargets.Count == 0) return result;
            var orderedReadTargets = readTargets
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var argFile = Path.Combine(cacheRoot, "exiftool-batch-read-" + Guid.NewGuid().ToString("N") + ".args");
            try
            {
                var argLines = new List<string>
                {
                    "-T",
                    "-sep",
                    "||",
                    "-Directory",
                    "-FileName",
                    "-XMP-digiKam:TagsList",
                    "-XMP-lr:HierarchicalSubject",
                    "-XMP-dc:Subject",
                    "-XMP:Subject",
                    "-XMP:TagsList",
                    "-IPTC:Keywords"
                };
                argLines.AddRange(orderedReadTargets.Select(pair => pair.Value));
                File.WriteAllLines(argFile, argLines.ToArray(), Encoding.UTF8);
                var output = RunExeCapture(exifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(exifToolPath), false);
                var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var outputLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int lineIndex = 0; lineIndex < outputLines.Length; lineIndex++)
                {
                    var line = outputLines[lineIndex];
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    var directoryPart = parts[0] == "-" ? string.Empty : parts[0];
                    var fileNamePart = parts[1] == "-" ? string.Empty : parts[1];
                    var exifPath = NormalizeExifToolPathKey(Path.Combine(directoryPart, fileNamePart));
                    string sourceFile;
                    if (!targetToSource.TryGetValue(exifPath, out sourceFile))
                    {
                        if (lineIndex >= orderedReadTargets.Count) continue;
                        sourceFile = orderedReadTargets[lineIndex].Key;
                    }
                    var tags = new List<string>();
                    for (int i = 2; i < parts.Length; i++)
                    {
                        foreach (var value in parts[i].Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var tag = CleanTag(value);
                            if (!string.IsNullOrWhiteSpace(tag) && tag != "-") tags.Add(tag);
                        }
                    }
                    result[sourceFile] = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    matchedFiles.Add(sourceFile);
                }
                foreach (var pair in readTargets)
                {
                    if (matchedFiles.Contains(pair.Key)) continue;
                    result[pair.Key] = ReadEmbeddedKeywordTagsDirect(pair.Key);
                }
            }
            finally
            {
                if (File.Exists(argFile)) File.Delete(argFile);
            }
            return result;
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

        void MoveMetadataSidecarIfPresent(string sourceFile, string targetFile)
        {
            var sourceSidecar = MetadataSidecarPath(sourceFile);
            var targetSidecar = MetadataSidecarPath(targetFile);
            if (string.IsNullOrWhiteSpace(sourceSidecar) || string.IsNullOrWhiteSpace(targetSidecar) || !File.Exists(sourceSidecar)) return;
            if (File.Exists(targetSidecar)) File.Delete(targetSidecar);
            File.Move(sourceSidecar, targetSidecar);
            Log("Moved sidecar: " + Path.GetFileName(sourceSidecar) + " -> " + targetSidecar);
        }

        void AddSidecarUndoEntryIfPresent(string targetFile, string sourceDirectory, List<UndoImportEntry> entries)
        {
            var sidecar = MetadataSidecarPath(targetFile);
            if (string.IsNullOrWhiteSpace(sidecar) || !File.Exists(sidecar) || entries == null) return;
            entries.Add(new UndoImportEntry { SourceDirectory = sourceDirectory, ImportedFileName = Path.GetFileName(sidecar), CurrentPath = sidecar });
        }

        string GuessGameIndexNameForFile(string file)
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(folderName)) return CleanTag(folderName);
            return NormalizeGameIndexName(GetGameNameFromFileName(Path.GetFileNameWithoutExtension(file)));
        }

        GameIndexEditorRow EnsureGameIndexRowForAssignment(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId = null)
        {
            var normalizedName = NormalizeGameIndexName(name);
            var normalizedPlatform = NormalizeConsoleLabel(platformLabel);
            var normalizedGameId = NormalizeGameId(preferredGameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byId = FindSavedGameIndexRowById(rows, normalizedGameId);
                if (byId != null && string.Equals(BuildGameIndexIdentity(byId.Name, byId.PlatformLabel), BuildGameIndexIdentity(normalizedName, normalizedPlatform), StringComparison.OrdinalIgnoreCase))
                {
                    return byId;
                }
            }
            var byIdentity = FindSavedGameIndexRowByIdentity(rows, normalizedName, normalizedPlatform);
            if (byIdentity != null) return byIdentity;
            var created = new GameIndexEditorRow
            {
                GameId = !string.IsNullOrWhiteSpace(normalizedGameId) ? normalizedGameId : CreateGameId(rows.Select(row => row.GameId)),
                Name = normalizedName,
                PlatformLabel = normalizedPlatform,
                SteamAppId = string.Empty,
                FileCount = 0,
                FolderPath = string.Empty,
                PreviewImagePath = string.Empty,
                FilePaths = new string[0]
            };
            rows.Add(created);
            return created;
        }

        GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId = null)
        {
            var normalizedRows = rows ?? Enumerable.Empty<GameIndexEditorRow>();
            var normalizedGameId = NormalizeGameId(preferredGameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byId = FindSavedGameIndexRowById(normalizedRows, normalizedGameId);
                if (byId != null) return byId;
            }
            return FindSavedGameIndexRowByIdentity(normalizedRows, NormalizeGameIndexName(name), NormalizeConsoleLabel(platformLabel));
        }

        bool DoesSavedGameIndexRowMatchIndexedFile(GameIndexEditorRow row, string file, string platformLabel)
        {
            if (row == null || string.IsNullOrWhiteSpace(file)) return false;
            return string.Equals(
                BuildGameIndexIdentity(row.Name, row.PlatformLabel),
                BuildGameIndexIdentity(GuessGameIndexNameForFile(file), platformLabel),
                StringComparison.OrdinalIgnoreCase);
        }

        string ResolveGameIdForIndexedFile(string root, string file, string platformLabel, IEnumerable<string> tags, Dictionary<string, LibraryMetadataIndexEntry> index, List<GameIndexEditorRow> gameRows, string preferredGameId = null)
        {
            var normalizedPlatform = NormalizeConsoleLabel(platformLabel);
            var guessedName = GuessGameIndexNameForFile(file);
            var normalizedPreferredGameId = NormalizeGameId(preferredGameId);
            var existingRow = FindSavedGameIndexRowById(gameRows, normalizedPreferredGameId);
            if (DoesSavedGameIndexRowMatchIndexedFile(existingRow, file, normalizedPlatform)) return existingRow.GameId;
            LibraryMetadataIndexEntry existingEntry;
            if (index != null && index.TryGetValue(file, out existingEntry))
            {
                var existingGameId = NormalizeGameId(existingEntry.GameId);
                var existingGameRow = FindSavedGameIndexRowById(gameRows, existingGameId);
                if (DoesSavedGameIndexRowMatchIndexedFile(existingGameRow, file, normalizedPlatform)) return existingGameId;
            }
            var resolvedByIdentity = FindSavedGameIndexRowByIdentity(gameRows, guessedName, normalizedPlatform);
            if (resolvedByIdentity != null) return resolvedByIdentity.GameId;
            var resolvedRow = EnsureGameIndexRowForAssignment(gameRows, guessedName, normalizedPlatform);
            return resolvedRow == null ? string.Empty : resolvedRow.GameId;
        }

        bool SyncGameIndexRowsFromLibraryFolders(List<GameIndexEditorRow> rows, IEnumerable<LibraryFolderInfo> folders)
        {
            var rowList = rows ?? new List<GameIndexEditorRow>();
            var folderMap = (folders ?? Enumerable.Empty<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.GameId))
                .GroupBy(folder => NormalizeGameId(folder.GameId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (var row in rowList)
            {
                if (row == null) continue;
                var normalizedGameId = NormalizeGameId(row.GameId);
                LibraryFolderInfo folder;
                if (!string.IsNullOrWhiteSpace(normalizedGameId) && folderMap.TryGetValue(normalizedGameId, out folder))
                {
                    if (!string.Equals(row.Name ?? string.Empty, folder.Name ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.Name = folder.Name ?? string.Empty;
                        changed = true;
                    }
                    if (!string.Equals(row.PlatformLabel ?? string.Empty, folder.PlatformLabel ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.PlatformLabel = folder.PlatformLabel ?? string.Empty;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(folder.SteamAppId) && !string.Equals(row.SteamAppId ?? string.Empty, folder.SteamAppId ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.SteamAppId = folder.SteamAppId ?? string.Empty;
                        changed = true;
                    }
                    if (row.FileCount != folder.FileCount)
                    {
                        row.FileCount = folder.FileCount;
                        changed = true;
                    }
                    if (!string.Equals(row.FolderPath ?? string.Empty, folder.FolderPath ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.FolderPath = folder.FolderPath ?? string.Empty;
                        changed = true;
                    }
                    if (!string.Equals(row.PreviewImagePath ?? string.Empty, folder.PreviewImagePath ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.PreviewImagePath = folder.PreviewImagePath ?? string.Empty;
                        changed = true;
                    }
                    if (!Enumerable.SequenceEqual((row.FilePaths ?? new string[0]).OrderBy(path => path, StringComparer.OrdinalIgnoreCase), (folder.FilePaths ?? new string[0]).OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                    {
                        row.FilePaths = folder.FilePaths ?? new string[0];
                        changed = true;
                    }
                }
                else
                {
                    if (row.FileCount != 0) { row.FileCount = 0; changed = true; }
                    if (!string.IsNullOrWhiteSpace(row.FolderPath)) { row.FolderPath = string.Empty; changed = true; }
                    if (!string.IsNullOrWhiteSpace(row.PreviewImagePath)) { row.PreviewImagePath = string.Empty; changed = true; }
                    if ((row.FilePaths ?? new string[0]).Length > 0) { row.FilePaths = new string[0]; changed = true; }
                }
            }
            return changed;
        }

        List<LibraryFolderInfo> LoadLibraryFolders(string root, Dictionary<string, LibraryMetadataIndexEntry> index = null)
        {
            var list = new List<LibraryFolderInfo>();
            if (index == null) index = LoadLibraryMetadataIndex(root);
            var gameRows = LoadSavedGameIndexRows(root);
            bool indexChanged = false;
            bool gameRowsChanged = false;
            var allFiles = Directory.EnumerateDirectories(root)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var file in allFiles)
            {
                LibraryMetadataIndexEntry entry;
                if (!index.TryGetValue(file, out entry) || entry == null)
                {
                    var tags = ReadEmbeddedKeywordTagsDirect(file);
                    var platformLabel = DetermineConsoleLabelFromTags(tags);
                    var gameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows);
                    index[file] = new LibraryMetadataIndexEntry
                    {
                        FilePath = file,
                        Stamp = BuildLibraryMetadataStamp(file),
                        GameId = gameId,
                        ConsoleLabel = platformLabel,
                        TagText = string.Join(", ", tags)
                    };
                    entry = index[file];
                    fileTagCache[file] = tags;
                    fileTagCacheStamp[file] = MetadataCacheStamp(file);
                    indexChanged = true;
                    gameRowsChanged = true;
                }
                else if (string.IsNullOrWhiteSpace(entry.GameId))
                {
                    var tags = ParseTagText(entry.TagText);
                    entry.GameId = ResolveGameIdForIndexedFile(root, file, DetermineConsoleLabelFromTags(tags), tags, index, gameRows);
                    indexChanged = true;
                    gameRowsChanged = true;
                }
                else
                {
                    var tags = ParseTagText(entry.TagText);
                    var platformLabel = DetermineConsoleLabelFromTags(tags);
                    var resolvedGameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, entry.GameId);
                    if (!string.Equals(NormalizeGameId(entry.GameId), NormalizeGameId(resolvedGameId), StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(NormalizeConsoleLabel(entry.ConsoleLabel), NormalizeConsoleLabel(platformLabel), StringComparison.OrdinalIgnoreCase))
                    {
                        entry.GameId = resolvedGameId;
                        entry.ConsoleLabel = platformLabel;
                        indexChanged = true;
                        gameRowsChanged = true;
                    }
                }
            }
            var groupedFiles = allFiles
                .Select(file => new
                {
                    File = file,
                    Entry = index.ContainsKey(file) ? index[file] : null
                })
                .Where(item => item.Entry != null && !string.IsNullOrWhiteSpace(item.Entry.GameId))
                .GroupBy(item => NormalizeGameId(item.Entry.GameId), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var group in groupedFiles)
            {
                var groupFiles = group.Select(item => item.File).OrderByDescending(GetLibraryDate).ThenBy(Path.GetFileName).ToArray();
                var saved = FindSavedGameIndexRowById(gameRows, group.Key);
                var preferredFolderPath = groupFiles
                    .Select(file => Path.GetDirectoryName(file) ?? string.Empty)
                    .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(pathGroup => pathGroup.Count())
                    .ThenBy(pathGroup => pathGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pathGroup => pathGroup.Key)
                    .FirstOrDefault();
                var platformLabel = saved == null
                    ? DetermineFolderPlatform(groupFiles.ToList(), index)
                    : NormalizeConsoleLabel(saved.PlatformLabel);
                list.Add(new LibraryFolderInfo
                {
                    GameId = group.Key,
                    Name = saved == null ? GuessGameIndexNameForFile(groupFiles[0]) : saved.Name,
                    FolderPath = string.IsNullOrWhiteSpace(saved == null ? string.Empty : saved.FolderPath) ? preferredFolderPath : saved.FolderPath,
                    FileCount = groupFiles.Length,
                    PreviewImagePath = groupFiles.FirstOrDefault(IsImage) ?? groupFiles.FirstOrDefault(),
                    PlatformLabel = platformLabel,
                    FilePaths = groupFiles,
                    SteamAppId = saved != null && !string.IsNullOrWhiteSpace(saved.SteamAppId) ? saved.SteamAppId : ResolveLibraryFolderSteamAppId(platformLabel, groupFiles)
                });
            }
            gameRowsChanged = SyncGameIndexRowsFromLibraryFolders(gameRows, list) || gameRowsChanged;
            gameRowsChanged = PruneObsoleteMultipleTagsRows(gameRows) || gameRowsChanged;
            if (gameRowsChanged) SaveSavedGameIndexRows(root, gameRows);
            if (indexChanged) SaveLibraryMetadataIndex(root, index);
            return list;
        }

        string GuessSteamAppIdFromFileName(string file)
        {
            var baseName = Path.GetFileNameWithoutExtension(file ?? string.Empty);
            var match = Regex.Match(baseName, @"^(?<id>\d{3,})_(?<ts>\d{14})(?:[_-]\d+)?$", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["id"].Value : string.Empty;
        }

        string ResolveLibraryFolderSteamAppId(string platformLabel, IEnumerable<string> files)
        {
            if (!string.Equals(NormalizeConsoleLabel(platformLabel), "Steam", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            foreach (var file in files ?? Enumerable.Empty<string>())
            {
                var appId = GuessSteamAppIdFromFileName(file);
                if (!string.IsNullOrWhiteSpace(appId)) return appId;
            }
            return string.Empty;
        }

        string ResolveBestLibraryFolderSteamAppId(string root, LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name)) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) return folder.SteamAppId;
            var saved = FindSavedGameIndexRow(LoadSavedGameIndexRows(root), folder);
            if (saved != null && !string.IsNullOrWhiteSpace(saved.SteamAppId))
            {
                folder.SteamAppId = saved.SteamAppId;
                return folder.SteamAppId;
            }
            var appId = ResolveLibraryFolderSteamAppId(folder.PlatformLabel, folder.FilePaths ?? new string[0]);
            if (string.IsNullOrWhiteSpace(appId)) appId = TryResolveSteamAppId(folder.Name);
            if (!string.IsNullOrWhiteSpace(appId))
            {
                folder.SteamAppId = appId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamAppId ?? string.Empty;
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
            if (folder == null) return false;
            return !string.IsNullOrWhiteSpace(CustomCoverPath(folder)) || CachedCoverPath(folder.Name) != null;
        }

        void DeleteCachedCover(string title)
        {
            var safe = SafeCacheName(title);
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                var path = Path.Combine(coversRoot, safe + ext);
                if (File.Exists(path)) File.Delete(path);
            }
            ClearImageCache();
        }

        void RefreshLibraryCovers(string root, List<LibraryFolderInfo> folders, Action<int, int, string> progress, Func<bool> isCancellationRequested, out int resolvedAppIds, out int coversReady)
        {
            resolvedAppIds = 0;
            coversReady = 0;
            var targetFolders = (folders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .GroupBy(folder => (folder.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
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
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder);
                if (!hadAppId && !string.IsNullOrWhiteSpace(appId))
                {
                    resolvedAppIds++;
                    foreach (var match in folders.Where(entry => entry != null && string.Equals(entry.Name ?? string.Empty, folder.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamAppId = appId;
                    }
                }
                completed++;
                if (progress != null) progress(completed, totalWork, itemLabel + " | AppID " + (string.IsNullOrWhiteSpace(appId) ? "not found" : appId));
                if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Cover refresh cancelled.");
                if (string.IsNullOrWhiteSpace(CustomCoverPath(folder))) DeleteCachedCover(folder.Name);
                ResolveLibraryArt(folder, true);
                var coverReady = HasDedicatedLibraryCover(folder);
                if (coverReady) coversReady++;
                completed++;
                if (progress != null) progress(completed, totalWork, itemLabel + " | cover " + (coverReady ? "ready" : "not available"));
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
            }
            SaveLibraryFolderCache(root, stamp, cached);
        }

        string[] ReadEmbeddedKeywordTagsDirect(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return new string[0];
            if (string.IsNullOrWhiteSpace(exifToolPath) || !File.Exists(exifToolPath)) return new string[0];
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return new string[0];
            var output = RunExeCapture(exifToolPath, new[] { "-s3", "-XMP-digiKam:TagsList", "-XMP-lr:HierarchicalSubject", "-XMP-dc:Subject", "-XMP:Subject", "-XMP:TagsList", "-IPTC:Keywords", readTarget }, Path.GetDirectoryName(exifToolPath), false);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(ParseTagText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        string LibraryMetadataIndexPath(string root)
        {
            return Path.Combine(cacheRoot, "library-metadata-index-" + SafeCacheName(root) + ".cache");
        }

        string BuildLibraryMetadataStamp(string file)
        {
            var info = new FileInfo(file);
            return MetadataCacheStamp(file).ToString();
        }

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false)
        {
            if (!forceDiskReload && string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase) && libraryMetadataIndex.Count > 0) return libraryMetadataIndex;
            libraryMetadataIndex.Clear();
            libraryMetadataIndexRoot = root;
            var path = LibraryMetadataIndexPath(root);
            if (!File.Exists(path)) return libraryMetadataIndex;
            var aliasMap = BuildSavedGameIdAliasMapFromFile(root);
            foreach (var line in File.ReadAllLines(path).Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 5)
                {
                    if (!File.Exists(parts[0])) continue;
                    var tagText = parts[4] ?? string.Empty;
                    string mappedGameId;
                    var currentGameId = NormalizeGameId(parts[2]);
                    libraryMetadataIndex[parts[0]] = new LibraryMetadataIndexEntry
                    {
                        FilePath = parts[0],
                        Stamp = parts[1],
                        GameId = !string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId) ? mappedGameId : parts[2],
                        ConsoleLabel = DetermineConsoleLabelFromTags(ParseTagText(tagText)),
                        TagText = tagText
                    };
                }
                else if (parts.Length >= 4)
                {
                    if (!File.Exists(parts[0])) continue;
                    var tagText = parts[3] ?? string.Empty;
                    libraryMetadataIndex[parts[0]] = new LibraryMetadataIndexEntry
                    {
                        FilePath = parts[0],
                        Stamp = parts[1],
                        GameId = string.Empty,
                        ConsoleLabel = DetermineConsoleLabelFromTags(ParseTagText(tagText)),
                        TagText = tagText
                    };
                }
            }
            return libraryMetadataIndex;
        }

        void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var path = LibraryMetadataIndexPath(root);
            var linesOut = new List<string>();
            var savedEntries = index.Values.Where(v => v != null && !string.IsNullOrWhiteSpace(v.FilePath) && File.Exists(v.FilePath)).OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            linesOut.Add(root);
            foreach (var entry in savedEntries)
            {
                linesOut.Add(string.Join("\t", new[]
                {
                    entry.FilePath ?? string.Empty,
                    entry.Stamp ?? string.Empty,
                    NormalizeGameId(entry.GameId),
                    entry.ConsoleLabel ?? string.Empty,
                    entry.TagText ?? string.Empty
                }));
            }
            File.WriteAllLines(path, linesOut.ToArray());
            libraryMetadataIndex.Clear();
            libraryMetadataIndexRoot = root;
            foreach (var entry in savedEntries)
            {
                libraryMetadataIndex[entry.FilePath] = new LibraryMetadataIndexEntry
                {
                    FilePath = entry.FilePath,
                    Stamp = entry.Stamp,
                    GameId = NormalizeGameId(entry.GameId),
                    ConsoleLabel = entry.ConsoleLabel,
                    TagText = entry.TagText
                };
            }
        }

        string NormalizeConsoleLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "Other";
            if (string.Equals(label, "Steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
            if (string.Equals(label, "PC", StringComparison.OrdinalIgnoreCase)) return "PC";
            if (string.Equals(label, "PlayStation", StringComparison.OrdinalIgnoreCase) || string.Equals(label, "PS5", StringComparison.OrdinalIgnoreCase)) return "PS5";
            if (string.Equals(label, "Xbox", StringComparison.OrdinalIgnoreCase)) return "Xbox";
            if (string.Equals(label, "Multiple Tags", StringComparison.OrdinalIgnoreCase)) return "Multiple Tags";
            return CleanTag(label);
        }

        string[] ExtractConsolePlatformFamilies(IEnumerable<string> tags)
        {
            var labels = new List<string>();
            var tagList = (tags ?? Enumerable.Empty<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(CleanTag).ToList();
            if (tagList.Any(tag => string.Equals(tag, "Steam", StringComparison.OrdinalIgnoreCase))) labels.Add("Steam");
            else if (tagList.Any(tag => string.Equals(tag, "PC", StringComparison.OrdinalIgnoreCase))) labels.Add("PC");
            if (tagList.Any(tag => string.Equals(tag, "PS5", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "PlayStation", StringComparison.OrdinalIgnoreCase))) labels.Add("PS5");
            if (tagList.Any(tag => string.Equals(tag, "Xbox", StringComparison.OrdinalIgnoreCase))) labels.Add("Xbox");
            foreach (var custom in tagList.Where(tag => tag.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase)).Select(tag => CleanTag(tag.Substring(CustomPlatformPrefix.Length))))
            {
                if (string.IsNullOrWhiteSpace(custom)) continue;
                var normalizedCustom = NormalizeConsoleLabel(custom);
                if (string.Equals(normalizedCustom, "Other", StringComparison.OrdinalIgnoreCase) || string.Equals(normalizedCustom, "Multiple Tags", StringComparison.OrdinalIgnoreCase)) continue;
                labels.Add(normalizedCustom);
            }
            return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        string DetermineConsoleLabelFromTags(IEnumerable<string> tags)
        {
            var labels = ExtractConsolePlatformFamilies(tags);
            if (labels.Length > 1) return "Multiple Tags";
            if (labels.Length == 1) return labels[0];
            return "Other";
        }

        LibraryMetadataIndexEntry TryGetLibraryMetadataIndexEntry(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            if (index == null) index = LoadLibraryMetadataIndex(root);
            LibraryMetadataIndexEntry entry;
            if (!index.TryGetValue(file, out entry)) return null;
            return entry;
        }

        int ScanLibraryMetadataIndex(string root, string folderPath, bool forceRescan, Action<int, int, string> progress, Func<bool> isCancellationRequested)
        {
            EnsureDir(root, "Library folder");
            EnsureExifTool();
            var index = LoadLibraryMetadataIndex(root);
            var gameRows = LoadSavedGameIndexRows(root);
            var targets = new List<string>();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    targets.AddRange(Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia));
                }
            }
            else
            {
                targets.AddRange(Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia));
            }
            var fileList = targets.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var targetSet = new HashSet<string>(fileList, StringComparer.OrdinalIgnoreCase);
            int updated = 0, unchanged = 0, removed = 0;
            var scopeLabel = string.IsNullOrWhiteSpace(folderPath) ? "library" : (Path.GetFileName(folderPath) ?? "folder");
            if (progress != null) progress(0, fileList.Count, "Queued " + fileList.Count + " media file(s) for " + scopeLabel + " scan.");
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                foreach (var stale in index.Keys.Where(key => !targetSet.Contains(key) || !File.Exists(key)).ToList())
                {
                    if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                    index.Remove(stale);
                    removed++;
                }
            }
            else
            {
                foreach (var stale in index.Keys.Where(key =>
                {
                    var fileDirectory = Path.GetDirectoryName(key) ?? string.Empty;
                    return string.Equals(fileDirectory, folderPath, StringComparison.OrdinalIgnoreCase)
                        && (!targetSet.Contains(key) || !File.Exists(key));
                }).ToList())
                {
                    if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                    index.Remove(stale);
                    removed++;
                }
            }
            if (removed > 0 && progress != null) progress(0, fileList.Count, "Removed " + removed + " stale index entr" + (removed == 1 ? "y" : "ies") + " before scanning.");

            var pendingFiles = new List<string>();
            var pendingStamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in fileList)
            {
                if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                var stamp = BuildLibraryMetadataStamp(file);
                LibraryMetadataIndexEntry existing;
                if (!forceRescan && index.TryGetValue(file, out existing) && string.Equals(existing.Stamp, stamp, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }
                pendingFiles.Add(file);
                pendingStamps[file] = stamp;
            }

            if (progress != null)
            {
                progress(unchanged, fileList.Count,
                    pendingFiles.Count == 0
                        ? "All files were unchanged after checking cached metadata stamps."
                        : "Preparing batched ExifTool reads for " + pendingFiles.Count + " changed file(s); " + unchanged + " unchanged.");
            }

            const int batchSize = 250;
            int processed = 0;
            int batchCount = pendingFiles.Count == 0 ? 0 : (int)Math.Ceiling((double)pendingFiles.Count / batchSize);
            for (int offset = 0; offset < pendingFiles.Count; offset += batchSize)
            {
                if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                var batch = pendingFiles.Skip(offset).Take(batchSize).ToList();
                var batchNumber = (offset / batchSize) + 1;
                if (progress != null) progress(unchanged + processed, fileList.Count, "Reading embedded tags in batch " + batchNumber + " of " + batchCount + " (" + batch.Count + " file(s)).");
                var batchTags = ReadEmbeddedKeywordTagsBatch(batch);
                foreach (var file in batch)
                {
                    if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                    string[] tags;
                    if (!batchTags.TryGetValue(file, out tags)) tags = new string[0];
                    var platformLabel = DetermineConsoleLabelFromTags(tags);
                    var existingGameId = index.ContainsKey(file) && index[file] != null ? index[file].GameId : string.Empty;
                    index[file] = new LibraryMetadataIndexEntry
                    {
                        FilePath = file,
                        Stamp = pendingStamps[file],
                        GameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, existingGameId),
                        ConsoleLabel = platformLabel,
                        TagText = string.Join(", ", tags)
                    };
                    fileTagCache[file] = tags;
                    fileTagCacheStamp[file] = MetadataCacheStamp(file);
                    updated++;
                    processed++;
                    var remaining = fileList.Count - (unchanged + processed);
                    if (progress != null) progress(unchanged + processed, fileList.Count, "Indexed " + (unchanged + processed) + " of " + fileList.Count + " | " + remaining + " remaining | " + file);
                }
            }
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
            var summary = string.IsNullOrWhiteSpace(folderPath)
                ? "Library metadata index scan complete: updated " + updated + ", unchanged " + unchanged + ", removed " + removed + "."
                : "Library folder scan complete for " + Path.GetFileName(folderPath) + ": updated " + updated + ", unchanged " + unchanged + ", removed " + removed + ".";
            Log(summary);
            if (progress != null) progress(fileList.Count, fileList.Count, summary);
            return updated;
        }

        void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root, true);
            var gameRows = LoadSavedGameIndexRows(root);
            foreach (var file in fileList)
            {
                var tags = ReadEmbeddedKeywordTagsDirect(file);
                var platformLabel = DetermineConsoleLabelFromTags(tags);
                var existingGameId = index.ContainsKey(file) && index[file] != null ? index[file].GameId : string.Empty;
                index[file] = new LibraryMetadataIndexEntry
                {
                    FilePath = file,
                    Stamp = BuildLibraryMetadataStamp(file),
                    GameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, existingGameId),
                    ConsoleLabel = platformLabel,
                    TagText = string.Join(", ", tags)
                };
                fileTagCache[file] = tags;
                fileTagCacheStamp[file] = MetadataCacheStamp(file);
            }
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
        }

        void UpsertLibraryMetadataIndexEntries(IEnumerable<ManualMetadataItem> items, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var itemList = (items ?? Enumerable.Empty<ManualMetadataItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            if (itemList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root, true);
            var gameRows = LoadSavedGameIndexRows(root);
            foreach (var item in itemList)
            {
                var tags = BuildMetadataTagSet(null, BuildManualMetadataExtraTags(item), item.AddPhotographyTag);
                var platformLabel = DetermineConsoleLabelFromTags(tags);
                var resolvedRow = ResolveExistingGameIndexRowForAssignment(gameRows, item.GameName, platformLabel, item.GameId);
                item.GameId = resolvedRow == null ? string.Empty : resolvedRow.GameId;
                if (resolvedRow != null && !string.IsNullOrWhiteSpace(resolvedRow.Name)) item.GameName = resolvedRow.Name;
                index[item.FilePath] = new LibraryMetadataIndexEntry
                {
                    FilePath = item.FilePath,
                    Stamp = BuildLibraryMetadataStamp(item.FilePath),
                    GameId = item.GameId,
                    ConsoleLabel = platformLabel,
                    TagText = string.Join(", ", tags)
                };
                fileTagCache[item.FilePath] = tags;
                fileTagCacheStamp[item.FilePath] = MetadataCacheStamp(item.FilePath);
            }
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
        }

        void RemoveLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root, true);
            var changed = false;
            foreach (var file in fileList)
            {
                if (index.Remove(file)) changed = true;
                fileTagCache.Remove(file);
                fileTagCacheStamp.Remove(file);
            }
            if (changed)
            {
                SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
            }
        }

        string[] GetEmbeddedKeywordTags(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return new string[0];
            var stamp = MetadataCacheStamp(file);
            string[] cachedTags;
            long cachedStamp;
            if (fileTagCache.TryGetValue(file, out cachedTags) && fileTagCacheStamp.TryGetValue(file, out cachedStamp) && cachedStamp == stamp)
            {
                return cachedTags;
            }
            var tags = ReadEmbeddedKeywordTagsDirect(file);
            fileTagCache[file] = tags;
            fileTagCacheStamp[file] = stamp;
            return tags;
        }

        string[] GetConsolePlatformTagsForFile(string file)
        {
            return ExtractConsolePlatformFamilies(GetEmbeddedKeywordTags(file));
        }

        string DetermineFolderPlatform(List<string> files, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                LibraryMetadataIndexEntry entry;
                string indexedLabel;
                if (index != null && index.TryGetValue(file, out entry))
                {
                    indexedLabel = NormalizeConsoleLabel(DetermineConsoleLabelFromTags(ParseTagText(entry.TagText)));
                }
                else
                {
                    indexedLabel = NormalizeConsoleLabel(DetermineConsoleLabelFromTags(GetEmbeddedKeywordTags(file)));
                }
                labels.Add(indexedLabel);
            }
            if (labels.Count > 1) return "Multiple Tags";
            if (labels.Count == 1) return labels.First();
            return "Other";
        }

        string DetermineLibraryFolderGroup(LibraryFolderInfo folder)
        {
            return NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
        }
        string ResolveLibraryArt(LibraryFolderInfo folder, bool allowDownload)
        {
            var custom = CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            var cached = CachedCoverPath(folder.Name);
            if (cached != null) return cached;
            if (allowDownload)
            {
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
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var path = Path.Combine(coversRoot, "custom-" + key + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        void SaveCustomCover(LibraryFolderInfo folder, string sourcePath)
        {
            if (folder == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            Directory.CreateDirectory(coversRoot);
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var existing = Path.Combine(coversRoot, "custom-" + key + ext);
                if (File.Exists(existing)) File.Delete(existing);
            }
            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            var target = Path.Combine(coversRoot, "custom-" + key + extension.ToLowerInvariant());
            File.Copy(sourcePath, target, true);
            ClearImageCache();
        }

        void ClearCustomCover(LibraryFolderInfo folder)
        {
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var existing = Path.Combine(coversRoot, "custom-" + key + ext);
                if (File.Exists(existing)) File.Delete(existing);
            }
            ClearImageCache();
        }

        string CachedCoverPath(string title)
        {
            var safe = SafeCacheName(title);
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                var path = Path.Combine(coversRoot, safe + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        string TryDownloadSteamCover(LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            try
            {
            var appId = ResolveBestLibraryFolderSteamAppId(libraryRoot, folder);
            if (string.IsNullOrWhiteSpace(appId)) return null;
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    var portraitUrls = new[]
                    {
                        "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/" + appId + "/library_600x900_2x.jpg",
                        "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/" + appId + "/library_600x900.jpg",
                        "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_600x900_2x.jpg",
                        "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_600x900.jpg"
                    };
                    foreach (var portraitUrl in portraitUrls)
                    {
                        try
                        {
                            var ext = Path.GetExtension(new Uri(portraitUrl).AbsolutePath);
                            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                            var target = Path.Combine(coversRoot, SafeCacheName(folder.Name) + ext);
                            wc.DownloadFile(portraitUrl, target);
                            if (File.Exists(target) && new FileInfo(target).Length > 0)
                            {
                                UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                                return target;
                            }
                        }
                        catch { }
                    }

                    var json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english");
                    var m = Regex.Match(json, "\"header_image\"\\s*:\\s*\"(?<u>(?:\\\\.|[^\"])*)\"");
                    if (!m.Success) return null;
                    var url = Regex.Unescape(m.Groups["u"].Value).Replace("\\/", "/");
                    var fallbackExt = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(fallbackExt)) fallbackExt = ".jpg";
                    var fallbackTarget = Path.Combine(coversRoot, SafeCacheName(folder.Name) + fallbackExt);
                    wc.DownloadFile(url, fallbackTarget);
                    UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                    return File.Exists(fallbackTarget) ? fallbackTarget : null;
                }
            }
            catch { }
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
            match.SteamAppId = folder.SteamAppId ?? string.Empty;
            SaveLibraryFolderCache(root, stamp, cached);
            UpsertSavedGameIndexRow(root, folder);
        }

        string TryResolveSteamAppId(string title)
        {
            string cached;
            if (steamSearchCache.TryGetValue(title, out cached)) return cached;
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    var html = wc.DownloadString("https://store.steampowered.com/search/suggest?term=" + Uri.EscapeDataString(title) + "&f=games&cc=US&l=english");
                    var matches = Regex.Matches(html, @"https://store\.steampowered\.com/app/(?<id>\d+)/[^""']+.*?<div class=""match_name"">(?<name>.*?)</div>", RegexOptions.Singleline);
                    var wanted = NormalizeTitle(title);
                    foreach (Match match in matches)
                    {
                        var candidate = NormalizeTitle(WebUtility.HtmlDecode(StripTags(match.Groups["name"].Value)));
                        if (candidate == wanted)
                        {
                            cached = match.Groups["id"].Value;
                            steamSearchCache[title] = cached;
                            return cached;
                        }
                    }
                }
            }
            catch { }
            steamSearchCache[title] = null;
            return null;
        }

        string DetermineManualMetadataPlatformLabel(ManualMetadataItem item)
        {
            if (item == null) return "Other";
            if (item.TagSteam) return "Steam";
            if (item.TagPc) return "PC";
            if (item.TagPs5) return "PS5";
            if (item.TagXbox) return "Xbox";
            if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) return NormalizeConsoleLabel(item.CustomPlatformTag);
            return "Other";
        }

        void PreserveLibraryMetadataEditGameIndex(string root, LibraryFolderInfo originalFolder, GameIndexEditorRow originalSavedRow, List<ManualMetadataItem> items)
        {
            if (string.IsNullOrWhiteSpace(root) || items == null || items.Count == 0) return;
            var preservedAppId = !string.IsNullOrWhiteSpace(originalSavedRow == null ? string.Empty : originalSavedRow.SteamAppId)
                ? originalSavedRow.SteamAppId
                : (originalFolder == null ? string.Empty : (originalFolder.SteamAppId ?? string.Empty));
            if (string.IsNullOrWhiteSpace(preservedAppId)) return;
            var rows = LoadSavedGameIndexRows(root);
            var sourceGameId = NormalizeGameId(originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.GameId) : originalSavedRow.GameId);
            var sourceName = NormalizeGameIndexName(originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.Name) : originalSavedRow.Name);
            var sourcePlatform = NormalizeConsoleLabel(originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.PlatformLabel) : originalSavedRow.PlatformLabel);
            var existing = !string.IsNullOrWhiteSpace(sourceGameId)
                ? FindSavedGameIndexRowById(rows, sourceGameId)
                : null;
            if (existing == null && !string.IsNullOrWhiteSpace(sourceName))
            {
                existing = FindSavedGameIndexRowByIdentity(rows, sourceName, sourcePlatform);
            }
            if (existing == null && (originalSavedRow != null || originalFolder != null))
            {
                existing = new GameIndexEditorRow
                {
                    GameId = !string.IsNullOrWhiteSpace(sourceGameId) ? sourceGameId : CreateGameId(rows.Select(row => row.GameId)),
                    Name = sourceName,
                    PlatformLabel = sourcePlatform,
                    SteamAppId = string.Empty,
                    FileCount = 0,
                    FolderPath = originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.FolderPath ?? string.Empty) : originalSavedRow.FolderPath ?? string.Empty,
                    PreviewImagePath = string.Empty,
                    FilePaths = new string[0]
                };
                rows.Add(existing);
            }
            if (existing == null) return;
            if (string.IsNullOrWhiteSpace(existing.GameId)) existing.GameId = !string.IsNullOrWhiteSpace(sourceGameId) ? sourceGameId : CreateGameId(rows.Select(row => row.GameId));
            if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = sourceName;
            if (string.IsNullOrWhiteSpace(existing.PlatformLabel)) existing.PlatformLabel = sourcePlatform;
            if (string.IsNullOrWhiteSpace(existing.SteamAppId)) existing.SteamAppId = preservedAppId;
            SaveSavedGameIndexRows(root, rows);
        }

        BitmapImage LoadImageSource(string path, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var sourcePath = path;
                if (IsVideo(path))
                {
                    var poster = EnsureVideoPoster(path, decodePixelWidth);
                    if (!string.IsNullOrWhiteSpace(poster) && File.Exists(poster)) path = poster;
                }
                var info = new FileInfo(path);
                var cacheKey = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + decodePixelWidth;
                var cached = TryGetCachedImage(cacheKey);
                if (cached != null) return cached;

                BitmapImage image = null;
                var thumbnailPath = IsVideo(sourcePath) ? null : ThumbnailCachePath(path, decodePixelWidth);
                if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    image = LoadFrozenBitmap(thumbnailPath, 0);
                }
                if (image == null)
                {
                    image = LoadFrozenBitmap(path, decodePixelWidth);
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

        string EnsureVideoPoster(string videoPath, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return null;
                var info = new FileInfo(videoPath);
                var width = Math.Max(320, decodePixelWidth > 0 ? decodePixelWidth : 720);
                var keySource = videoPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + width;
                string hash;
                using (var md5 = MD5.Create())
                {
                    hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(keySource))).Replace("-", string.Empty).ToLowerInvariant();
                }
                var posterPath = Path.Combine(thumbsRoot, "video-" + hash + ".png");
                if (File.Exists(posterPath)) return posterPath;
                var renderWidth = Math.Max(320, width);
                var renderHeight = Math.Max(180, (int)Math.Round(renderWidth * 9d / 16d));
                var ffmpegPoster = TryCreateVideoPosterWithFfmpeg(videoPath, posterPath, renderWidth);
                if (!string.IsNullOrWhiteSpace(ffmpegPoster) && File.Exists(ffmpegPoster)) return ffmpegPoster;
                return CreateFallbackVideoPoster(videoPath, posterPath, renderWidth, renderHeight);
            }
            catch
            {
                return null;
            }
        }

        string ResolveFfmpegPath()
        {
            if (!string.IsNullOrWhiteSpace(ffmpegPath) && File.Exists(ffmpegPath)) return ffmpegPath;
            var discovered = FindExecutableOnPath("ffmpeg.exe");
            if (!string.IsNullOrWhiteSpace(discovered)) ffmpegPath = discovered;
            return ffmpegPath;
        }

        string[] BuildFfmpegPosterArgs(string videoPath, string posterPath, int renderWidth, string hwaccel)
        {
            var args = new List<string>
            {
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-threads",
                "0"
            };
            if (!string.IsNullOrWhiteSpace(hwaccel))
            {
                args.Add("-hwaccel");
                args.Add(hwaccel);
            }
            args.Add("-ss");
            args.Add("00:00:00.250");
            args.Add("-i");
            args.Add(videoPath);
            args.Add("-frames:v");
            args.Add("1");
            args.Add("-vf");
            args.Add("scale=" + Math.Max(320, renderWidth) + ":-2");
            args.Add(posterPath);
            return args.ToArray();
        }

        string TryCreateVideoPosterWithFfmpeg(string videoPath, string posterPath, int renderWidth)
        {
            var ffmpeg = ResolveFfmpegPath();
            if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg)) return null;

            foreach (var hwaccel in new[] { "auto", string.Empty })
            {
                try
                {
                    if (File.Exists(posterPath)) File.Delete(posterPath);
                    RunExeCapture(ffmpeg, BuildFfmpegPosterArgs(videoPath, posterPath, renderWidth, hwaccel), Path.GetDirectoryName(ffmpeg), false);
                    if (File.Exists(posterPath)) return posterPath;
                }
                catch
                {
                    try
                    {
                        if (File.Exists(posterPath)) File.Delete(posterPath);
                    }
                    catch
                    {
                    }
                }
            }
            return null;
        }

        string CreateFallbackVideoPoster(string videoPath, string posterPath, int renderWidth, int renderHeight)
        {
            try
            {
                var title = Path.GetFileNameWithoutExtension(videoPath);
                var accent = Brush("#3E6D8C");
                var bg = Brush("#12191E");
                var fg = Brush("#F1E9DA");
                var sub = Brush("#A7B5BD");
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(bg, null, new Rect(0, 0, renderWidth, renderHeight));
                    dc.DrawRectangle(Brush("#1B242A"), null, new Rect(0, 0, renderWidth, 44));
                    dc.DrawRoundedRectangle(Brush("#20343A"), null, new Rect(24, 20, renderWidth - 48, renderHeight - 40), 16, 16);
                    dc.DrawEllipse(accent, null, new Point(renderWidth / 2d, renderHeight / 2d - 12), 54, 54);
                    var play = new StreamGeometry();
                    using (var ctx = play.Open())
                    {
                        ctx.BeginFigure(new Point(renderWidth / 2d - 16, renderHeight / 2d - 38), true, true);
                        ctx.LineTo(new Point(renderWidth / 2d - 16, renderHeight / 2d + 14), true, false);
                        ctx.LineTo(new Point(renderWidth / 2d + 28, renderHeight / 2d - 12), true, false);
                    }
                    play.Freeze();
                    dc.DrawGeometry(Brushes.White, null, play);
                    var titleText = new FormattedText(title, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 18, fg);
                    titleText.MaxTextWidth = renderWidth - 64;
                    titleText.TextAlignment = TextAlignment.Center;
                    dc.DrawText(titleText, new Point((renderWidth - titleText.Width) / 2d, renderHeight - 84));
                    var subText = new FormattedText(Path.GetExtension(videoPath).TrimStart('.').ToUpperInvariant() + " video", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, sub);
                    subText.MaxTextWidth = renderWidth - 64;
                    subText.TextAlignment = TextAlignment.Center;
                    dc.DrawText(subText, new Point((renderWidth - subText.Width) / 2d, renderHeight - 56));
                }
                var bitmap = new RenderTargetBitmap(renderWidth, renderHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(posterPath)) encoder.Save(stream);
                return posterPath;
            }
            catch
            {
                return null;
            }
        }

        void EnsureExifTool()
        {
            if (!File.Exists(exifToolPath)) throw new InvalidOperationException("ExifTool not found: " + exifToolPath);
            var support = Path.Combine(Path.GetDirectoryName(exifToolPath), "exiftool_files");
            if (Path.GetFileName(exifToolPath).Equals("exiftool.exe", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(support)) throw new InvalidOperationException("ExifTool support folder missing: " + support);
            RunExe(exifToolPath, new[] { "-ver" }, Path.GetDirectoryName(exifToolPath), false);
        }

        void RunExe(string file, string[] args, string cwd, bool logOutput)
        {
            RunExeCapture(file, args, cwd, logOutput);
        }

        string RunExeCapture(string file, string[] args, string cwd, bool logOutput)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = string.Join(" ", args.Select(Quote).ToArray()),
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (logOutput)
                {
                    foreach (var line in (stdout + Environment.NewLine + stderr).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) Log(line);
                }
                if (p.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(file) + " failed. " + stderr + stdout);
                return stdout;
            }
        }
        string SteamName(string appId)
        {
            string cached;
            if (steamCache.TryGetValue(appId, out cached)) return cached;
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    var json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english");
                    var m = Regex.Match(json, "\"name\"\\s*:\\s*\"(?<n>(?:\\\\.|[^\"])*)\"");
                    if (m.Success)
                    {
                        cached = Sanitize(Regex.Unescape(m.Groups["n"].Value));
                        steamCache[appId] = cached;
                        return cached;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Steam lookup failed for AppID " + appId + ". " + ex.Message);
            }
            steamCache[appId] = null;
            return null;
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

        static bool ManualMetadataTouchesTags(ManualMetadataItem item)
        {
            if (item == null) return false;
            return !SameManualText(item.TagText, item.OriginalTagText)
                || item.AddPhotographyTag != item.OriginalAddPhotographyTag
                || item.TagSteam != item.OriginalTagSteam
                || item.TagPc != item.OriginalTagPc
                || item.TagPs5 != item.OriginalTagPs5
                || item.TagXbox != item.OriginalTagXbox
                || item.TagOther != item.OriginalTagOther
                || !SameManualText(CleanTag(item.CustomPlatformTag), CleanTag(item.OriginalCustomPlatformTag));
        }

        static bool ManualMetadataTouchesComment(ManualMetadataItem item)
        {
            if (item == null) return false;
            return !SameManualText(item.Comment, item.OriginalComment);
        }

        static bool ManualMetadataTouchesCaptureTime(ManualMetadataItem item)
        {
            if (item == null) return false;
            return item.UseCustomCaptureTime != item.OriginalUseCustomCaptureTime
                || (item.UseCustomCaptureTime && item.CaptureTime != item.OriginalCaptureTime);
        }
        static string Unique(string path) { if (!File.Exists(path)) return path; var dir = Path.GetDirectoryName(path); var name = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path); int i = 2; string candidate; do { candidate = Path.Combine(dir, name + " (" + i + ")" + ext); i++; } while (File.Exists(candidate)); return candidate; }
        static void EnsureDir(string path, string label) { if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new InvalidOperationException(label + " not found: " + path); }
        static bool IsImage(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp"; }
        static bool IsPngOrJpeg(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".png" || e == ".jpg" || e == ".jpeg"; }
        static bool IsVideo(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return e == ".mp4" || e == ".mkv" || e == ".avi" || e == ".mov" || e == ".wmv" || e == ".webm"; }
        static bool IsMedia(string p) { var e = Path.GetExtension(p).ToLowerInvariant(); return new[] { ".jpg", ".jpeg", ".png", ".webp", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" }.Contains(e); }
        static string Quote(string s) { return s.Contains(" ") ? "\"" + s.Replace("\"", "\\\"") + "\"" : s; }
        static bool CanUpdateMetadata(string file) { return IsVideo(file) || DetectPlatformTags(file).Contains("Xbox") || ParseCaptureDate(file).HasValue; }

        static string[] DetectPlatformTags(string file)
        {
            var tags = new List<string>();
            if (Regex.IsMatch(file, @"^.+_\d{14}_\d+\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                tags.Add("Steam");
                tags.Add("PC");
            }
            else if (Regex.IsMatch(file, @"^.+_\d{14}\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                tags.Add("PS5");
                tags.Add("PlayStation");
            }
            if (Regex.IsMatch(file, @".+[-â€“â€”]\d{4}_\d{2}_\d{2}[-_]\d{2}[-_]\d{2}[-_]\d{2}\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase)) tags.Add("Xbox");
            if (file.IndexOf("PS5", StringComparison.OrdinalIgnoreCase) >= 0) { tags.Add("PS5"); tags.Add("PlayStation"); }
            else if (file.IndexOf("PlayStation", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("PlayStation");
            return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

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
            var b = Regex.Match(file, @"-(\d{4})_(\d{2})_(\d{2})[-_](\d{2})[-_](\d{2})[-_](\d{2})");
            if (b.Success)
            {
                var raw = b.Groups[1].Value + b.Groups[2].Value + b.Groups[3].Value + b.Groups[4].Value + b.Groups[5].Value + b.Groups[6].Value;
                if (DateTime.TryParseExact(raw, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out d)) return d;
            }
            return null;
        }

        string SafeCacheName(string title) { return Regex.Replace(NormalizeTitle(title), @"\s+", "_"); }

        string ThumbnailCachePath(string sourcePath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            if (decodePixelWidth <= 0 || decodePixelWidth > 1600) return null;
            try
            {
                var info = new FileInfo(sourcePath);
                var key = sourcePath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + decodePixelWidth;
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

        List<PhotoIndexEditorRow> LoadPhotoIndexEditorRows(string root)
        {
            return LoadLibraryMetadataIndex(root, true)
                .Values
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath))
                .Select(entry => new PhotoIndexEditorRow
                {
                    FilePath = entry.FilePath ?? string.Empty,
                    Stamp = entry.Stamp ?? string.Empty,
                    GameId = NormalizeGameId(entry.GameId),
                    ConsoleLabel = NormalizeConsoleLabel(entry.ConsoleLabel),
                    TagText = entry.TagText ?? string.Empty
                })
                .OrderBy(row => row.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        void SavePhotoIndexEditorRows(string root, IEnumerable<PhotoIndexEditorRow> rows)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            var rowList = (rows ?? Enumerable.Empty<PhotoIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath))
                .GroupBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            var missingGameId = rowList.FirstOrDefault(row => string.IsNullOrWhiteSpace(NormalizeGameId(row.GameId)));
            if (missingGameId != null) throw new InvalidOperationException("Each photo index row needs a Game ID before saving. Missing: " + Path.GetFileName(missingGameId.FilePath));

            var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rowList)
            {
                var normalizedTags = string.Join(", ", ParseTagText(row.TagText));
                var normalizedConsole = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.ConsoleLabel) ? DetermineConsoleLabelFromTags(ParseTagText(normalizedTags)) : row.ConsoleLabel);
                index[row.FilePath] = new LibraryMetadataIndexEntry
                {
                    FilePath = row.FilePath,
                    Stamp = BuildLibraryMetadataStamp(row.FilePath),
                    GameId = NormalizeGameId(row.GameId),
                    ConsoleLabel = normalizedConsole,
                    TagText = normalizedTags
                };
            }

            var gameRows = LoadSavedGameIndexRows(root);
            foreach (var group in index.Values.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.GameId)).GroupBy(entry => NormalizeGameId(entry.GameId), StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var row = EnsureGameIndexRowForAssignment(gameRows, GuessGameIndexNameForFile(first.FilePath), first.ConsoleLabel, group.Key);
                if (row == null) continue;
                var filePaths = group.Select(entry => entry.FilePath).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                row.FileCount = filePaths.Length;
                row.FilePaths = filePaths;
                row.PreviewImagePath = filePaths.FirstOrDefault(IsImage) ?? filePaths.FirstOrDefault() ?? string.Empty;
                row.FolderPath = filePaths
                    .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                    .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(pathGroup => pathGroup.Count())
                    .ThenBy(pathGroup => pathGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pathGroup => pathGroup.Key)
                    .FirstOrDefault() ?? string.Empty;
                row.PlatformLabel = NormalizeConsoleLabel(first.ConsoleLabel);
            }

            SaveSavedGameIndexRows(root, gameRows);
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
        }

        void OpenPhotoIndexEditor()
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                MessageBox.Show("Library folder not found. Check Settings before opening the photo index.", "PixelVault");
                return;
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
                var deleteRowButton = Btn("Delete Row", null, "#A3473E", Brushes.White);
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
                    var choice = MessageBox.Show("Remove " + selectedItems.Count + " selected row(s) from the photo index?\n\nThis does not delete the file itself.", "Delete Photo Index Row", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.OK) return;
                    foreach (var selected in selectedItems) allRows.Remove(selected);
                    dirty = true;
                    refreshGrid();
                    status.Text = "Photo index row(s) removed";
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
                editorWindow.Closed += delegate { status.Text = "Ready"; };

                refreshGrid();
                status.Text = "Photo index ready";
                Log("Opened photo index editor.");
                editorWindow.ShowDialog();
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
                var helperText = new TextBlock { Text = "Edit the master Game, Platform, and Steam AppID fields. Game ID stays stable, and folder/file details stay read-only so photo-level assignments drive grouping.", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#5F6970"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) };
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
                var resolveIdsButton = Btn("Resolve AppIDs", null, "#275D47", Brushes.White);
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
                        var resolved = ResolveMissingGameIndexSteamAppIds(libraryRoot, allRows, delegate(int current, int total, string detail)
                        {
                            statusText.Text = current + " of " + total + " checked | " + detail;
                        });
                        dirty = false;
                        refreshGrid();
                        status.Text = "Game index IDs resolved";
                        Log("Resolved " + resolved + " Steam AppID entr" + (resolved == 1 ? "y" : "ies") + " into the game index.");
                    }
                    catch (Exception resolveEx)
                    {
                        status.Text = "Game index resolve failed";
                        Log("Failed to resolve game index AppIDs. " + resolveEx.Message);
                        MessageBox.Show("Could not resolve Steam AppIDs for the game index." + Environment.NewLine + Environment.NewLine + resolveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
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
                editorWindow.Closed += delegate { status.Text = "Ready"; };

                refreshGrid();
                status.Text = "Game index ready";
                Log("Opened game index editor.");
                editorWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                status.Text = "Game index unavailable";
                Log("Failed to open game index. " + ex.Message);
                MessageBox.Show("Could not open the game index." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault");
            }
        }
        List<GameIndexEditorRow> LoadGameIndexEditorRows(string root)
        {
            var folders = LoadLibraryFoldersCached(root, false);
            if (folders == null || folders.Count == 0)
            {
                status.Text = "Building game index";
                Log("Game index cache missing or stale. Rebuilding it before editing.");
                folders = LoadLibraryFoldersCached(root, true);
            }
            var liveRows = folders
                .Select(folder => new GameIndexEditorRow
                {
                    GameId = folder.GameId ?? string.Empty,
                    Name = folder.Name ?? string.Empty,
                    PlatformLabel = folder.PlatformLabel ?? string.Empty,
                    SteamAppId = folder.SteamAppId ?? string.Empty,
                    FileCount = folder.FileCount,
                    FolderPath = folder.FolderPath ?? string.Empty,
                    PreviewImagePath = folder.PreviewImagePath ?? string.Empty,
                    FilePaths = folder.FilePaths ?? new string[0]
                })
                .ToList();
            var savedRows = LoadSavedGameIndexRows(root);
            var rows = MergeGameIndexRows(savedRows.Concat(liveRows));
            if (savedRows.Count == 0 || rows.Count != savedRows.Count)
            {
                SaveSavedGameIndexRows(root, rows);
                RefreshCachedLibraryFoldersFromGameIndex(root);
            }
            return rows;
        }
        void SaveGameIndexEditorRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var normalizedRows = MergeGameIndexRows(rows);
            SaveSavedGameIndexRows(root, normalizedRows);
            RefreshCachedLibraryFoldersFromGameIndex(root);
        }
        int ResolveMissingGameIndexSteamAppIds(string root, List<GameIndexEditorRow> rows, Action<int, int, string> progress)
        {
            var allRows = rows ?? new List<GameIndexEditorRow>();
            var targets = allRows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(BuildGameIndexMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int resolved = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var row = targets[i];
                var detailPrefix = "Game " + (i + 1) + " of " + targets.Count + " | " + row.Name;
                if (!string.IsNullOrWhiteSpace(row.SteamAppId))
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | already cached as " + row.SteamAppId);
                    continue;
                }
                var folder = new LibraryFolderInfo
                {
                    GameId = row.GameId ?? string.Empty,
                    Name = row.Name ?? string.Empty,
                    FolderPath = row.FolderPath ?? string.Empty,
                    FileCount = row.FileCount,
                    PreviewImagePath = row.PreviewImagePath ?? string.Empty,
                    PlatformLabel = row.PlatformLabel ?? string.Empty,
                    FilePaths = row.FilePaths ?? new string[0],
                    SteamAppId = row.SteamAppId ?? string.Empty
                };
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder);
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    foreach (var match in allRows.Where(entry =>
                        string.Equals(BuildGameIndexMergeKey(entry), BuildGameIndexMergeKey(row), StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamAppId = appId;
                    }
                    resolved++;
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | resolved " + appId);
                }
                else
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | no match");
                }
            }
            SaveGameIndexEditorRows(root, allRows);
            return resolved;
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
                else if (key == "library_folder_tile_size")
                {
                    int parsedSize;
                    if (int.TryParse(value, out parsedSize)) libraryFolderTileSize = NormalizeLibraryFolderTileSize(parsedSize);
                }
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
                "library_folder_tile_size=" + NormalizeLibraryFolderTileSize(libraryFolderTileSize)
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



















































































































