using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        public string Name;
        public string FolderPath;
        public int FileCount;
        public string PreviewImagePath;
        public string CoverArtPath;
        public string PlatformLabel;
        public string[] FilePaths;
        public string SteamAppId;
    }


    sealed class LibraryMetadataIndexEntry
    {
        public string FilePath;
        public string Stamp;
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
        public string FilePath;
        public string FileName;
        public string OriginalFileName;
        public DateTime CaptureTime;
        public bool UseCustomCaptureTime;
        public string GameName;
        public string Comment;
        public string TagText;
        public bool AddPhotographyTag;
        public bool TagSteam;
        public bool TagPc;
        public bool TagPs5;
        public bool TagXbox;
        public bool TagOther;
        public string CustomPlatformTag;
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

    public sealed class MainWindow : Window
    {
        const string AppVersion = "0.638";
        const string GamePhotographyTag = "Game Photography";
        const string CustomPlatformPrefix = "Platform:";
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
            LoadSettings();

            Title = "PixelVault " + AppVersion;
            Width = 1420;
            Height = 980;
            MinWidth = 1220;
            MinHeight = 820;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.White;
            var iconPath = Path.Combine(appRoot, "assets", "PixelVault.ico");
            if (File.Exists(iconPath)) Icon = BitmapFrame.Create(new Uri(iconPath));
            Content = BuildUi();
            recurseBox.IsChecked = true;
            keywordsBox.IsChecked = true;
            conflictBox.SelectedIndex = 0;
            LoadLogView();
            Log("PixelVault " + AppVersion + " ready.");
            RefreshPreview();
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
            hs.Children.Add(new TextBlock { Text = "PixelVault " + AppVersion, FontSize = 31, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            hs.Children.Add(new TextBlock { Text = "Prep captures for upload, then browse the grouped archive without disturbing your flow.", Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            status = new TextBlock { Text = "Ready", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            var headerRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var viewLogsTopButton = Btn("View Logs", delegate { OpenFolder(logsRoot); }, "#2B3F47", Brushes.White);
            viewLogsTopButton.Margin = new Thickness(0, 0, 12, 0);
            var settingsTopButton = Btn("Settings", delegate { ShowSettingsWindow(); }, "#20343A", Brushes.White);
            settingsTopButton.Margin = new Thickness(0, 0, 12, 0);
            var changelogTopButton = Btn("Changelog", delegate { ShowChangelogWindow(); }, "#20343A", Brushes.White);
            changelogTopButton.Margin = new Thickness(0, 0, 12, 0);
            var sp = new Border { Child = status, Background = Brush("#20343A"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10) };
            headerRight.Children.Add(viewLogsTopButton);
            headerRight.Children.Add(settingsTopButton);
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
            leftStack.Children.Add(TitleBlock("Workflow"));
            leftStack.Children.Add(new TextBlock { Text = "Use Settings for all path selection. Choose a quick process, add comments when you want them, or open Manual Intake later for files that do not match a known capture format.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap });
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

            leftStack.Children.Add(new TextBlock { Text = "Library actions", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            leftStack.Children.Add(new TextBlock { Text = "Browse, organize, and reverse the most recent import from the capture library.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var libraryRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), ItemHeight = 48 };
            libraryRow.Children.Add(Btn("Browse Library", delegate { ShowLibraryBrowser(); }, null, Brushes.Black));
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
        Button Btn(string t, RoutedEventHandler click, string bg, Brush fg) { var b = new Button { Content = t, Width = 176, Height = 48, Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 12, 12), Foreground = fg, Background = bg != null ? Brush(bg) : Brushes.White, BorderBrush = Brush("#C0CCD6"), BorderThickness = new Thickness(1), FontWeight = FontWeights.SemiBold, Effect = new DropShadowEffect { Color = Color.FromArgb(64, 18, 27, 36), BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.55 } }; if (click != null) b.Click += click; return b; }

        Border BuildSettingsSummary()
        {
            var border = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Current paths", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Sources: " + SourceRootsSummary(), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Destination: " + destinationRoot, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Library: " + libraryRoot, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "ExifTool: " + exifToolPath, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F") });
            border.Child = stack;
            return border;
        }

        void RefreshMainUi()
        {
            bool recurse = recurseBox != null && recurseBox.IsChecked == true;
            bool keywords = keywordsBox != null && keywordsBox.IsChecked == true;
            string conflict = conflictBox != null && conflictBox.SelectedItem != null ? Convert.ToString(conflictBox.SelectedItem) : "Rename";
            Content = BuildUi();
            recurseBox.IsChecked = recurse;
            keywordsBox.IsChecked = keywords;
            conflictBox.SelectedItem = conflict;
            LoadLogView();
            RefreshPreview();
        }

        void ShowSettingsWindow()
        {
            var window = new Window
            {
                Title = "PixelVault " + AppVersion + " Settings",
                Width = 760,
                Height = 460,
                MinWidth = 680,
                MinHeight = 560,
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

            SettingsBrowseButton(panel, 0, delegate { var picked = PickFolder(PrimarySourceRoot()); if (!string.IsNullOrWhiteSpace(picked)) sourceBox.Text = AppendSourceRoot(sourceBox.Text, picked); }, "Add Folder");
            SettingsBrowseButton(panel, 1, delegate { var picked = PickFolder(destinationBox.Text); if (!string.IsNullOrWhiteSpace(picked)) destinationBox.Text = picked; });
            SettingsBrowseButton(panel, 2, delegate { var picked = PickFolder(libraryBox.Text); if (!string.IsNullOrWhiteSpace(picked)) libraryBox.Text = picked; });
            SettingsBrowseButton(panel, 3, delegate { var picked = PickFile(exifBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*"); if (!string.IsNullOrWhiteSpace(picked)) exifBox.Text = picked; });

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
                SaveSettings();
                RefreshMainUi();
                window.Close();
                Log("Settings saved.");
            };
            cancel.Click += delegate { window.Close(); };
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

        void OpenSourceFolders()
        {
            foreach (var root in GetSourceRoots()) OpenFolder(root);
        }
        void RefreshPreview()
        {
            try
            {
                EnsureSourceFolders();
                var rename = EnumerateSourceFiles(recurseBox != null && recurseBox.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly, IsImage).ToList();
                var meta = EnumerateSourceFiles(SearchOption.TopDirectoryOnly, IsMedia).Where(file => CanUpdateMetadata(Path.GetFileName(file))).ToList();
                var move = EnumerateSourceFiles(SearchOption.TopDirectoryOnly, IsMedia).ToList();
                var reviewItems = BuildReviewItems();
                var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(recognizedPaths);
                var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var moveCandidates = move.Where(f => !manualPaths.Contains(f)).ToList();
                int renameCandidates = rename.Count(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"(?<!\d)(\d{3,})(?!\d)"));
                int metaCandidates = reviewItems.Count;
                int conflicts = Directory.Exists(destinationRoot) ? moveCandidates.Count(f => File.Exists(Path.Combine(destinationRoot, Path.GetFileName(f)))) : 0;
                RenderPreview(rename.Count, renameCandidates, meta.Count, metaCandidates, moveCandidates, manualItems, conflicts);
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
                RunRename();
                var reviewItems = BuildReviewItems();
                var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(recognizedPaths);
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
                var imported = RunMove(manualPaths);
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
                var recognizedPaths = new HashSet<string>(BuildReviewItems().Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(recognizedPaths);
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
            int renamed = 0, skipped = 0;
            foreach (var file in EnumerateSourceFiles(recurseBox.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly, IsMedia))
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
            var items = new List<ReviewItem>();
            foreach (var file in EnumerateSourceFiles(SearchOption.TopDirectoryOnly, IsMedia))
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
            var items = new List<ManualMetadataItem>();
            var known = recognizedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateSourceFiles(SearchOption.TopDirectoryOnly, IsMedia))
            {
                if (known.Contains(file)) continue;
                var fileName = Path.GetFileName(file);
                if (CanUpdateMetadata(fileName)) continue;
                var captureTime = GetLibraryDate(file);
                items.Add(new ManualMetadataItem
                {
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
            var gameNameBox = new TextBox { Margin = new Thickness(0, 8, 0, 14), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 14 };
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
            Action refreshSelectionStatus = delegate
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

                gameNameBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.GameName; });
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

            gameNameBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.GameName = gameNameBox.Text;
                refreshSelectionStatus();
            };
            tagsBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.TagText = tagsBox.Text;
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
                foreach (var item in selectedItems) item.AddPhotographyTag = true;
                refreshSelectionUi();
            };
            photographyBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.AddPhotographyTag = false;
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
                foreach (var item in selectedItems) item.TagSteam = false;
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
                foreach (var item in selectedItems) item.TagPc = false;
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
                foreach (var item in selectedItems) item.TagPs5 = false;
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
                foreach (var item in selectedItems) item.TagXbox = false;
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherBox.Checked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "Other");
                foreach (var item in selectedItems) item.CustomPlatformTag = otherPlatformBox.Text;
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
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherPlatformBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.CustomPlatformTag = otherPlatformBox.Text;
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
                var pendingItems = (libraryMode ? items : selectedItems).Distinct().ToList();
                if (pendingItems.Count == 0)
                {
                    MessageBox.Show(libraryMode ? "Select at least one library image to update." : "Select at least one unmatched image to send.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (useCustomTimeBox.IsChecked == true) saveSelectedDateTime();
                if (pendingItems.Any(item => item.TagOther && string.IsNullOrWhiteSpace(CleanTag(item.CustomPlatformTag))))
                {
                    MessageBox.Show("Enter a platform name in the Other box before applying changes.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var confirmText = libraryMode
                    ? items.Count + " loaded image(s) will be renamed if needed, updated with metadata, and reorganized in the library if their title changes.\n\nApply changes now?"
                    : pendingItems.Count + " image(s) will be renamed if needed, tagged, updated with metadata, and moved to the destination.\n\nApply changes and send them now?";
                var confirm = MessageBox.Show(confirmText, confirmCaption, MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.OK) return;
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
                UpsertLibraryMetadataIndexEntries(items.Select(i => i.FilePath), libraryRoot);
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
                var writeTagMetadata = ManualMetadataTouchesTags(item);
                if (!writeDateMetadata && !writeCommentMetadata && !writeTagMetadata)
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | unchanged | " + item.FileName);
                    continue;
                }
                var extraTags = new List<string>(ParseTagText(item.TagText));
                if (item.TagSteam) { extraTags.Add("Steam"); extraTags.Add("PC"); }
                if (item.TagPc) extraTags.Add("PC");
                if (item.TagPs5) { extraTags.Add("PS5"); extraTags.Add("PlayStation"); }
                if (item.TagXbox) extraTags.Add("Xbox");
                if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) extraTags.Add(CustomPlatformPrefix + CleanTag(item.CustomPlatformTag));
                var changeNotes = new List<string>();
                if (writeDateMetadata) changeNotes.Add("date/time");
                if (writeCommentMetadata) changeNotes.Add("comment");
                if (writeTagMetadata) changeNotes.Add("tags");
                var metadataTarget = effectiveTime.ToString("yyyy-MM-dd HH:mm:ss") + (preserveFileTimes ? " (using filesystem timestamp)" : " (custom)");
                Log("Updating manual metadata: " + item.FileName + " -> " + metadataTarget + " [" + string.Join(", ", changeNotes.ToArray()) + "]");
                DateTime originalCreate = DateTime.MinValue;
                DateTime originalWrite = DateTime.MinValue;
                if (writeDateMetadata && preserveFileTimes)
                {
                    originalCreate = File.GetCreationTime(file);
                    originalWrite = File.GetLastWriteTime(file);
                }
                RunExe(exifToolPath, BuildExifArgs(file, effectiveTime, new string[0], extraTags, preserveFileTimes, item.Comment, item.AddPhotographyTag, writeDateMetadata, writeCommentMetadata, writeTagMetadata), Path.GetDirectoryName(exifToolPath), true);
                if (writeDateMetadata && preserveFileTimes)
                {
                    if (originalCreate != DateTime.MinValue) File.SetCreationTime(file, originalCreate);
                    if (originalWrite != DateTime.MinValue) File.SetLastWriteTime(file, originalWrite);
                }
                updated++;
                if (progress != null) progress(i + 1, total, "Updated metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName + " [" + string.Join(", ", changeNotes.ToArray()) + "]");
            }
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

        void RunMetadata(List<ReviewItem> reviewItems)
        {
            int updated = 0, skipped = 0;
            foreach (var item in reviewItems)
            {
                if (item.DeleteBeforeProcessing) { skipped++; continue; }
                var file = item.FilePath;
                if (!File.Exists(file)) { skipped++; continue; }
                var selectedPlatformTags = new List<string>();
                if (item.TagSteam)
                {
                    selectedPlatformTags.Add("Steam");
                    selectedPlatformTags.Add("PC");
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
                DateTime originalCreate = DateTime.MinValue;
                DateTime originalWrite = DateTime.MinValue;
                if (item.PreserveFileTimes)
                {
                    originalCreate = File.GetCreationTime(file);
                    originalWrite = File.GetLastWriteTime(file);
                }
                RunExe(exifToolPath, BuildExifArgs(file, item.CaptureTime, platformTags, item.PreserveFileTimes, item.Comment, item.AddPhotographyTag), Path.GetDirectoryName(exifToolPath), true);
                if (item.PreserveFileTimes)
                {
                    if (originalCreate != DateTime.MinValue) File.SetCreationTime(file, originalCreate);
                    if (originalWrite != DateTime.MinValue) File.SetLastWriteTime(file, originalWrite);
                }
                updated++;
            }
            Log("Metadata summary: updated " + updated + ", skipped " + skipped + ".");
        }

        List<UndoImportEntry> RunMove()
        {
            return RunMove(null);
        }

        List<UndoImportEntry> RunMove(HashSet<string> skipFiles)
        {
            var files = EnumerateSourceFiles(SearchOption.TopDirectoryOnly, IsMedia)
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
                var tags = new List<string>();
                bool includeGameCaptureKeywords = true;
                if (keywordsBox != null)
                {
                    if (keywordsBox.Dispatcher.CheckAccess()) includeGameCaptureKeywords = keywordsBox.IsChecked == true;
                    else includeGameCaptureKeywords = keywordsBox.Dispatcher.Invoke(new Func<bool>(delegate { return keywordsBox.IsChecked == true; }));
                }
                if (includeGameCaptureKeywords)
                {
                    tags.Add("Game Capture");
                    if (platformTags != null) tags.AddRange(platformTags);
                }
                if (extraTags != null) tags.AddRange(extraTags.Where(t => !string.IsNullOrWhiteSpace(t)));
                if (addPhotographyTag) tags.Add(GamePhotographyTag);
                args.Add("-XMP:Subject=");
                args.Add("-XMP-dc:Subject=");
                args.Add("-XMP:TagsList=");
                if (!IsVideo(file))
                {
                    args.Add("-IPTC:Keywords=");
                    args.Add("-Keywords=");
                }
                else
                {
                    args.Add("-XMP-digiKam:TagsList=");
                    args.Add("-XMP-lr:HierarchicalSubject=");
                }
                foreach (var tag in tags.Select(CleanTag).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    args.Add("-XMP:Subject+=" + tag);
                    args.Add("-XMP-dc:Subject+=" + tag);
                    args.Add("-XMP:TagsList+=" + tag);
                    if (!IsVideo(file))
                    {
                        args.Add("-IPTC:Keywords+=" + tag);
                        args.Add("-Keywords+=" + tag);
                    }
                    else
                    {
                        args.Add("-XMP-digiKam:TagsList+=" + tag);
                        args.Add("-XMP-lr:HierarchicalSubject+=" + tag);
                    }
                }
            }
            args.Add("-overwrite_original");
            args.Add(targetPath);
            return args.ToArray();
        }
        void ShowLibraryBrowser()
        {
            try
            {
                EnsureDir(libraryRoot, "Library folder");
                var folders = LoadLibraryFoldersCached(libraryRoot, false);
                var libraryWindow = new Window
                {
                    Title = "PixelVault " + AppVersion + " Library",
                    Width = 1280,
                    Height = 860,
                    MinWidth = 1020,
                    MinHeight = 700,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#0F1519")
                };

                var root = new Grid { Margin = new Thickness(18) };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var left = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Margin = new Thickness(0, 0, 16, 0) };
                var leftGrid = new Grid();
                leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var leftHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition());
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                leftHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new TextBlock { Text = "Game Library", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 12, 0) });
                var folderCount = new TextBlock { Text = folders.Count + " folders", Foreground = Brush("#B7C6C0"), VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
                titleStack.Children.Add(folderCount);
                leftHeader.Children.Add(titleStack);
                var photographyButton = Btn("Photography", null, "#20343A", Brushes.White);
                photographyButton.Margin = new Thickness(18, 0, 18, 0);
                Grid.SetColumn(photographyButton, 1);
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
                Grid.SetColumn(headerActions, 2);
                leftHeader.Children.Add(headerActions);
                leftGrid.Children.Add(leftHeader);

                var tileScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 16, 0, 0) };
                var tilePanel = new StackPanel();
                tileScroll.Content = tilePanel;
                Grid.SetRow(tileScroll, 1);
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
                var sliderLabel = new TextBlock { Text = "Thumbnail size", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var sizeValue = new TextBlock { Text = "260", Foreground = Brush("#A7B5BD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 40 };
                var thumbSizeSlider = new Slider { Minimum = 160, Maximum = 420, Value = 260, Width = 170, TickFrequency = 20, IsSnapToTickEnabled = true };
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
                Action<bool> renderTiles = null;

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
                        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
                        foreach (var file in group)
                        {
                            var tile = new Border { Width = size + 20, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(10), Background = Brush("#1A232A"), CornerRadius = new CornerRadius(12), BorderBrush = Brush("#31414B"), BorderThickness = new Thickness(1), Tag = file };
                            var image = LoadImageSource(file, size * 2);
                            if (image != null)
                            {
                                tile.Child = new Image { Source = image, Width = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
                            }
                            else
                            {
                                tile.Child = new TextBlock { Text = Path.GetFileName(file), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8), Foreground = Brush("#F1E9DA") };
                            }
                            tile.MouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
                            {
                                if (e.ClickCount >= 2)
                                {
                                    var clicked = sender as Border;
                                    if (clicked != null && clicked.Tag is string) OpenWithShell((string)clicked.Tag);
                                }
                            };
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
                    previewImage.Source = LoadImageSource(ResolveLibraryArt(info, false), 720);
                    renderSelectedFolder();
                };

                renderTiles = delegate(bool forceRefresh)
                {
                    folders = LoadLibraryFoldersCached(libraryRoot, forceRefresh);
                    folderCount.Text = folders.Count + " folders";
                    tilePanel.Children.Clear();
                    var folderGroups = folders
                        .GroupBy(folder => DetermineLibraryFolderGroup(folder))
                        .OrderBy(group => PlatformGroupOrder(group.Key))
                        .ThenBy(group => group.Key)
                        .ToList();
                    foreach (var folderGroup in folderGroups)
                    {
                        var groupWrap = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
                        foreach (var folder in folderGroup.OrderBy(f => f.Name))
                        {
                            var tile = new Button
                            {
                                Width = 290,
                                Margin = new Thickness(0, 0, 14, 14),
                                Padding = new Thickness(0),
                                Background = Brush("#1A2329"),
                                BorderBrush = Brush("#243139"),
                                BorderThickness = new Thickness(1),
                                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                                VerticalContentAlignment = VerticalAlignment.Stretch
                            };
                            var tileStack = new StackPanel();
                            var imageBorder = new Border { Width = 290, Height = 435, Background = Brush("#0E1418") };
                            var tileArt = LoadImageSource(ResolveLibraryArt(folder, false), 760);
                            if (tileArt != null) imageBorder.Child = new Image { Source = tileArt, Width = 290, Height = 435, Stretch = Stretch.UniformToFill };
                            else imageBorder.Child = new TextBlock { Text = folder.PlatformLabel, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
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
                            contextMenu.Items.Add(openFolderItem);
                            contextMenu.Items.Add(editMetadataItem);
                            contextMenu.Items.Add(new Separator());
                            contextMenu.Items.Add(refreshFolderItem);
                            contextMenu.Items.Add(rebuildFolderItem);
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

                Action runCoverRefresh = delegate
                {
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
                        appendProgress("Starting cover refresh.");
                        status.Text = "Resolving AppIDs and fetching cover art";
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
                            RefreshLibraryCovers(libraryRoot, folders, delegate(int currentCount, int totalCount, string detail)
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
                                    status.Text = "Cover refresh cancelled";
                                    if (progressMeta != null) progressMeta.Text = "Cover refresh cancelled before completion.";
                                    appendProgress("Cover refresh cancelled.");
                                }
                                else if (refreshTask.IsFaulted)
                                {
                                    status.Text = "Cover refresh failed";
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
                                    status.Text = "Cover refresh complete";
                                    if (progressMeta != null) progressMeta.Text += " | complete";
                                    appendProgress("Cover refresh finished successfully.");
                                    renderTiles(false);
                                    Log("Library cover art refresh complete. Resolved " + resolved + " Steam AppID entr" + (resolved == 1 ? "y" : "ies") + "; " + coversReady + " title" + (coversReady == 1 ? " has" : "s have") + " cover art ready.");
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

                refreshButton.Click += delegate { runLibraryScan(null, false); };
                rebuildLibraryButton.Click += delegate { runLibraryScan(null, true); };
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

                renderTiles(false);
                libraryWindow.Show();
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
                        var img = LoadImageSource(file, width * 2);
                        if (img != null)
                        {
                            frame.Child = new Image { Source = img, Width = width - 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
                        }
                        else
                        {
                            frame.Child = new TextBlock { Text = Path.GetFileName(file), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), Foreground = Brush("#F5EFE4") };
                        }
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
                    Log("Library folder cache hit.");
                    return cached;
                }
            }
            Log("Refreshing library folder cache.");
            var fresh = LoadLibraryFolders(root);
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
            var list = new List<LibraryFolderInfo>();
            foreach (var line in lines.Skip(2))
            {
                var parts = line.Split('\t');
                if (parts.Length < 5) continue;
                list.Add(new LibraryFolderInfo
                {
                    FolderPath = parts[0],
                    Name = parts[1],
                    FileCount = ParseInt(parts[2]),
                    PreviewImagePath = parts[3],
                    PlatformLabel = parts[4],
                    FilePaths = parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5])
                        ? parts[5].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
                        : new string[0],
                    CoverArtPath = parts.Length > 6 ? parts[6] : string.Empty,
                    SteamAppId = parts.Length > 7 ? parts[7] : string.Empty
                });
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
                    folder.FolderPath ?? string.Empty,
                    folder.Name ?? string.Empty,
                    folder.FileCount.ToString(),
                    folder.PreviewImagePath ?? string.Empty,
                    folder.PlatformLabel ?? string.Empty,
                    string.Join("|", (folder.FilePaths ?? new string[0]).Where(File.Exists)),
                    folder.CoverArtPath ?? string.Empty,
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
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var existing = LoadLibraryFolderCache(root, stamp) ?? new List<LibraryFolderInfo>();
            var fresh = LoadLibraryFolders(root, index);
            foreach (var folder in fresh)
            {
                var match = existing.FirstOrDefault(entry =>
                    string.Equals(entry.FolderPath, folder.FolderPath, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizeConsoleLabel(entry.PlatformLabel), NormalizeConsoleLabel(folder.PlatformLabel), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Name ?? string.Empty, folder.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;
                if (string.IsNullOrWhiteSpace(folder.SteamAppId)) folder.SteamAppId = match.SteamAppId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(folder.CoverArtPath) && !string.IsNullOrWhiteSpace(match.CoverArtPath) && File.Exists(match.CoverArtPath)) folder.CoverArtPath = match.CoverArtPath;
            }
            SaveLibraryFolderCache(root, stamp, fresh);
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
            if (folder == null || string.IsNullOrWhiteSpace(folder.FolderPath) || !Directory.Exists(folder.FolderPath)) return new List<string>();
            if (folder.FilePaths != null && folder.FilePaths.Length > 0)
            {
                return folder.FilePaths
                    .Where(File.Exists)
                    .Where(file => !imagesOnly || IsImage(file))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
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

        string NormalizeMetadataLookupPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = path.Trim().Trim('"');
            normalized = normalized.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
            }
            if (normalized.Length >= 2 && normalized[1] == ':')
            {
                normalized = char.ToUpperInvariant(normalized[0]) + normalized.Substring(1);
            }
            return normalized;
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
                targetToSource[readTarget] = file;
                var normalizedReadTarget = NormalizeMetadataLookupPath(readTarget);
                if (!string.IsNullOrWhiteSpace(normalizedReadTarget)) targetToSource[normalizedReadTarget] = file;
            }
            if (readTargets.Count == 0) return result;

            var argFile = Path.Combine(cacheRoot, "exiftool-batch-read-" + Guid.NewGuid().ToString("N") + ".args");
            try
            {
                var argLines = new List<string>
                {
                    "-T",
                    "-sep",
                    "||",
                    "-SourceFile",
                    "-XMP-digiKam:TagsList",
                    "-XMP-lr:HierarchicalSubject",
                    "-XMP-dc:Subject",
                    "-XMP:Subject",
                    "-XMP:TagsList",
                    "-IPTC:Keywords"
                };
                argLines.AddRange(readTargets.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                File.WriteAllLines(argFile, argLines.ToArray(), Encoding.UTF8);
                var output = RunExeCapture(exifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(exifToolPath), false);
                foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 0) continue;
                    string sourceFile;
                    if (!targetToSource.TryGetValue(parts[0], out sourceFile))
                    {
                        var normalizedSource = NormalizeMetadataLookupPath(parts[0]);
                        if (string.IsNullOrWhiteSpace(normalizedSource) || !targetToSource.TryGetValue(normalizedSource, out sourceFile)) continue;
                    }
                    var tags = new List<string>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        foreach (var value in parts[i].Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var tag = CleanTag(value);
                            if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                        }
                    }
                    result[sourceFile] = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        List<LibraryFolderInfo> LoadLibraryFolders(string root, Dictionary<string, LibraryMetadataIndexEntry> index = null)
        {
            var list = new List<LibraryFolderInfo>();
            if (index == null) index = LoadLibraryMetadataIndex(root);
            foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName))
            {
                var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia).OrderBy(Path.GetFileName).ToList();
                var grouped = files.GroupBy(file => NormalizeConsoleLabel(DetermineFolderPlatform(new List<string> { file }, index)));
                foreach (var group in grouped.OrderBy(g => PlatformGroupOrder(g.Key)).ThenBy(g => g.Key))
                {
                    var groupFiles = group.OrderByDescending(GetLibraryDate).ThenBy(Path.GetFileName).ToArray();
                    var folderInfo = new LibraryFolderInfo
                    {
                        Name = Path.GetFileName(dir),
                        FolderPath = dir,
                        FileCount = groupFiles.Length,
                        PreviewImagePath = groupFiles.FirstOrDefault(IsImage) ?? groupFiles.FirstOrDefault(),
                        PlatformLabel = group.Key,
                        FilePaths = groupFiles,
                        SteamAppId = ResolveLibraryFolderSteamAppId(group.Key, groupFiles)
                    };
                    folderInfo.CoverArtPath = CustomCoverPath(folderInfo) ?? CachedCoverPath(folderInfo.Name);
                    list.Add(folderInfo);
                }
            }
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

        string ResolveBestLibraryFolderSteamAppId(LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name)) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) return folder.SteamAppId;
            var appId = ResolveLibraryFolderSteamAppId(folder.PlatformLabel, folder.FilePaths ?? new string[0]);
            if (string.IsNullOrWhiteSpace(appId)) appId = TryResolveSteamAppId(folder.Name);
            if (!string.IsNullOrWhiteSpace(appId)) folder.SteamAppId = appId;
            return folder.SteamAppId ?? string.Empty;
        }

        int EnrichLibraryFoldersWithSteamAppIds(string root, List<LibraryFolderInfo> folders, Action<int, int, string> progress)
        {
            var targetFolders = (folders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .GroupBy(folder => (folder.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
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
                var appId = ResolveBestLibraryFolderSteamAppId(folder);
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    foreach (var match in folders.Where(entry => entry != null && string.Equals(entry.Name ?? string.Empty, folder.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
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
            imageCache.Clear();
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
                var appId = ResolveBestLibraryFolderSteamAppId(folder);
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
                var resolvedArt = ResolveLibraryArt(folder, true);
                foreach (var match in folders.Where(entry => entry != null && string.Equals(entry.Name ?? string.Empty, folder.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
                {
                    match.CoverArtPath = resolvedArt ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(appId) && string.IsNullOrWhiteSpace(match.SteamAppId)) match.SteamAppId = appId;
                }
                folder.CoverArtPath = resolvedArt ?? string.Empty;
                var coverReady = HasDedicatedLibraryCover(folder);
                if (coverReady) coversReady++;
                completed++;
                if (progress != null) progress(completed, totalWork, itemLabel + " | cover " + (coverReady ? "ready" : "not available"));
            }
            SaveLibraryFolderCache(root, BuildLibraryFolderInventoryStamp(root), folders);
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

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root)
        {
            if (string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase) && libraryMetadataIndex.Count > 0) return libraryMetadataIndex;
            libraryMetadataIndex.Clear();
            libraryMetadataIndexRoot = root;
            var path = LibraryMetadataIndexPath(root);
            if (!File.Exists(path)) return libraryMetadataIndex;
            foreach (var line in File.ReadAllLines(path).Skip(1))
            {
                var parts = line.Split(new[] { "\t" }, 4, StringSplitOptions.None);
                if (parts.Length < 4) continue;
                if (!File.Exists(parts[0])) continue;
                var tagText = parts[3] ?? string.Empty;
                libraryMetadataIndex[parts[0]] = new LibraryMetadataIndexEntry
                {
                    FilePath = parts[0],
                    Stamp = parts[1],
                    ConsoleLabel = DetermineConsoleLabelFromTags(ParseTagText(tagText)),
                    TagText = tagText
                };
            }
            return libraryMetadataIndex;
        }

        void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var path = LibraryMetadataIndexPath(root);
            var linesOut = new List<string>();
            linesOut.Add(root);
            foreach (var entry in index.Values.Where(v => v != null && !string.IsNullOrWhiteSpace(v.FilePath) && File.Exists(v.FilePath)).OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                linesOut.Add(string.Join("\t", new[]
                {
                    entry.FilePath ?? string.Empty,
                    entry.Stamp ?? string.Empty,
                    entry.ConsoleLabel ?? string.Empty,
                    entry.TagText ?? string.Empty
                }));
            }
            File.WriteAllLines(path, linesOut.ToArray());
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
            int updated = 0, unchanged = 0, removed = 0, preserved = 0;
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
                if (removed > 0 && progress != null) progress(0, fileList.Count, "Removed " + removed + " stale index entr" + (removed == 1 ? "y" : "ies") + " before scanning.");
            }

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
                    LibraryMetadataIndexEntry existing;
                    var hasExisting = index.TryGetValue(file, out existing) && existing != null;
                    var existingTagText = hasExisting ? (existing.TagText ?? string.Empty) : string.Empty;
                    var existingLabel = hasExisting ? (existing.ConsoleLabel ?? string.Empty) : string.Empty;
                    var noTagsRead = tags == null || tags.Length == 0;
                    var preserveExistingTags = noTagsRead && !string.IsNullOrWhiteSpace(existingTagText);
                    if (preserveExistingTags)
                    {
                        index[file] = new LibraryMetadataIndexEntry
                        {
                            FilePath = file,
                            Stamp = pendingStamps[file],
                            ConsoleLabel = string.IsNullOrWhiteSpace(existingLabel) ? DetermineConsoleLabelFromTags(ParseTagText(existingTagText)) : existingLabel,
                            TagText = existingTagText
                        };
                        fileTagCache[file] = ParseTagText(existingTagText);
                        fileTagCacheStamp[file] = MetadataCacheStamp(file);
                        preserved++;
                        processed++;
                        var remainingPreserved = fileList.Count - (unchanged + processed);
                        if (progress != null) progress(unchanged + processed, fileList.Count, "Preserved cached tags for " + (unchanged + processed) + " of " + fileList.Count + " | " + remainingPreserved + " remaining | " + file);
                        continue;
                    }
                    index[file] = new LibraryMetadataIndexEntry
                    {
                        FilePath = file,
                        Stamp = pendingStamps[file],
                        ConsoleLabel = DetermineConsoleLabelFromTags(tags),
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
                ? "Library metadata index scan complete: updated " + updated + ", preserved " + preserved + ", unchanged " + unchanged + ", removed " + removed + "."
                : "Library folder scan complete for " + Path.GetFileName(folderPath) + ": updated " + updated + ", preserved " + preserved + ", unchanged " + unchanged + ".";
            Log(summary);
            if (progress != null) progress(fileList.Count, fileList.Count, summary);
            return updated;
        }

        void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root);
            foreach (var file in fileList)
            {
                var tags = ReadEmbeddedKeywordTagsDirect(file);
                index[file] = new LibraryMetadataIndexEntry
                {
                    FilePath = file,
                    Stamp = BuildLibraryMetadataStamp(file),
                    ConsoleLabel = DetermineConsoleLabelFromTags(tags),
                    TagText = string.Join(", ", tags)
                };
                fileTagCache[file] = tags;
                fileTagCacheStamp[file] = MetadataCacheStamp(file);
            }
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
        }

        void RemoveLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root);
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
            if (folder != null && !string.IsNullOrWhiteSpace(folder.CoverArtPath) && File.Exists(folder.CoverArtPath)) return folder.CoverArtPath;
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
            imageCache.Clear();
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
            imageCache.Clear();
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
                var appId = ResolveBestLibraryFolderSteamAppId(folder);
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
            var match = cached.FirstOrDefault(entry =>
                string.Equals(entry.FolderPath, folder.FolderPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeConsoleLabel(entry.PlatformLabel), NormalizeConsoleLabel(folder.PlatformLabel), StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Name ?? string.Empty, folder.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (match == null) return;
            match.SteamAppId = folder.SteamAppId ?? string.Empty;
            match.CoverArtPath = folder.CoverArtPath ?? string.Empty;
            SaveLibraryFolderCache(root, stamp, cached);
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

        BitmapImage LoadImageSource(string path, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                if (IsVideo(path))
                {
                    var poster = EnsureVideoPoster(path, decodePixelWidth);
                    if (!string.IsNullOrWhiteSpace(poster) && File.Exists(poster)) path = poster;
                }
                var info = new FileInfo(path);
                var cacheKey = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + decodePixelWidth;
                BitmapImage cached;
                if (imageCache.TryGetValue(cacheKey, out cached)) return cached;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                imageCache[cacheKey] = image;
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
            if (decodePixelWidth <= 0 || decodePixelWidth > 900) return null;
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
            }

            var bundledExifTool = Path.Combine(appRoot, "tools", "exiftool.exe");
            if (!File.Exists(exifToolPath) && File.Exists(bundledExifTool))
            {
                exifToolPath = bundledExifTool;
            }
        }

        void SaveSettings()
        {
            File.WriteAllLines(settingsPath, new[]
            {
                "source=" + SerializeSourceRoots(sourceRoot),
                "destination=" + destinationRoot,
                "library=" + libraryRoot,
                "exiftool=" + exifToolPath
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








































































































































