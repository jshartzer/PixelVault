using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        UIElement BuildUi()
        {
            var root = new Grid { Margin = new Thickness(24), Background = Brushes.White };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(20), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 16) };
            var headerGrid = new Grid();
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var hs = new StackPanel();
            hs.Children.Add(new TextBlock { Text = "PixelVault Settings", FontSize = 31, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            hs.Children.Add(new TextBlock { Text = "Configure paths, run intake tools, and manage the library without putting the browser itself in the way.", Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            status = new TextBlock { Text = "Ready", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            var headerActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            Action<Button> styleHeaderBtn = delegate(Button b) { b.Margin = new Thickness(0, 0, 10, 8); };
            var pathSettingsTopButton = Btn("Path Settings", delegate { ShowPathSettingsWindow(); }, "#2B3F47", Brushes.White);
            styleHeaderBtn(pathSettingsTopButton);
            var viewLogsTopButton = Btn("View Logs", delegate { OpenFolder(logsRoot); }, "#2B3F47", Brushes.White);
            styleHeaderBtn(viewLogsTopButton);
            var myCoversTopButton = Btn("My Covers", delegate { OpenSavedCoversFolder(); }, "#2B3F47", Brushes.White);
            styleHeaderBtn(myCoversTopButton);
            var gameIndexTopButton = Btn("Game Index", delegate { OpenGameIndexEditor(); }, "#20343A", Brushes.White);
            styleHeaderBtn(gameIndexTopButton);
            var photoIndexTopButton = Btn("Photo Index", delegate { OpenPhotoIndexEditor(); }, "#20343A", Brushes.White);
            styleHeaderBtn(photoIndexTopButton);
            var photographyTopButton = Btn("Photography", delegate { ShowPhotographyGallery(Window.GetWindow(status)); }, "#20343A", Brushes.White);
            photographyTopButton.ToolTip = "Browse captures tagged for game photography";
            styleHeaderBtn(photographyTopButton);
            var filenameRulesTopButton = Btn("Filename Rules", delegate { OpenFilenameConventionEditor(); }, "#20343A", Brushes.White);
            styleHeaderBtn(filenameRulesTopButton);
            var changelogTopButton = Btn("Changelog", delegate { ChangelogWindow.ShowDialog(this, AppVersion, changelogPath); }, "#20343A", Brushes.White);
            styleHeaderBtn(changelogTopButton);
            var sp = new Border { Child = status, Background = Brush("#20343A"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 8), VerticalAlignment = VerticalAlignment.Center };
            headerActions.Children.Add(pathSettingsTopButton);
            headerActions.Children.Add(viewLogsTopButton);
            headerActions.Children.Add(myCoversTopButton);
            headerActions.Children.Add(gameIndexTopButton);
            headerActions.Children.Add(photoIndexTopButton);
            headerActions.Children.Add(photographyTopButton);
            headerActions.Children.Add(filenameRulesTopButton);
            headerActions.Children.Add(changelogTopButton);
            headerActions.Children.Add(sp);
            headerGrid.Children.Add(hs);
            Grid.SetRow(headerActions, 1);
            headerGrid.Children.Add(headerActions);
            header.Child = headerGrid;
            root.Children.Add(header);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.62, GridUnitType.Star) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);

            var left = Card();
            left.Margin = new Thickness(0, 0, 16, 0);
            var leftGrid = new Grid();
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 120 });
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 200 });
            left.Child = leftGrid;

            var leftStack = new StackPanel();
            leftStack.Children.Add(TitleBlock("Control Center"));
            leftStack.Children.Add(new TextBlock { Text = "Use Path Settings for locations and tools, then run imports or maintenance from here whenever you need them.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap });
            leftStack.Children.Add(BuildSettingsSummary());

            leftStack.Children.Add(new TextBlock { Text = "Import options", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 16, 0, 8) });
            recurseBox = new CheckBox { Content = "Search subfolders for rename", Margin = new Thickness(0, 0, 0, 8) };
            keywordsBox = new CheckBox { Content = "Add Game Capture keywords", Margin = new Thickness(0, 0, 0, 8) };
            keywordsBox.Checked += delegate { SyncIncludeGameCaptureKeywordsMirror(); };
            keywordsBox.Unchecked += delegate { SyncIncludeGameCaptureKeywordsMirror(); };
            leftStack.Children.Add(recurseBox);
            leftStack.Children.Add(keywordsBox);
            SyncIncludeGameCaptureKeywordsMirror();

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
            leftStack.Children.Add(new TextBlock { Text = "Organize the destination, reverse the most recent import, and keep heavier metadata rebuilds tucked here when you actually need them.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var libraryRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), ItemHeight = 48 };
            libraryRow.Children.Add(Btn("Sort Destination", delegate { SortDestinationFolders(); }, "#E9EEF3", Brush("#33424D")));
            libraryRow.Children.Add(Btn("Undo Last Import", delegate { UndoLastImport(); }, "#FFF1E2", Brush("#7A4B12")));
            var rebuildSelectedFolderButton = Btn("Rebuild Selected Folder", delegate
            {
                var selectedFolder = CloneLibraryFolderInfo(activeSelectedLibraryFolder);
                if (selectedFolder == null || string.IsNullOrWhiteSpace(selectedFolder.FolderPath))
                {
                    MessageBox.Show("Choose a library folder first, then open Settings to rebuild just that folder.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var choice = MessageBox.Show(
                    "Rebuild metadata for the selected folder only?\n\n" + selectedFolder.Name + " | " + selectedFolder.PlatformLabel,
                    "PixelVault",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (choice != MessageBoxResult.OK) return;
                ShowLibraryMetadataScanWindow(ResolveStatusWindowOwner(), libraryRoot, selectedFolder.FolderPath, true, null, delegate
                {
                    RefreshActiveLibraryFolders(false);
                });
            }, "#EAE6F9", Brush("#4B3E78"));
            rebuildSelectedFolderButton.IsEnabled = activeSelectedLibraryFolder != null && !string.IsNullOrWhiteSpace(activeSelectedLibraryFolder.FolderPath);
            libraryRow.Children.Add(rebuildSelectedFolderButton);
            libraryRow.Children.Add(Btn("Rebuild Library Metadata", delegate
            {
                var choice = MessageBox.Show(
                    "Rebuild the library metadata index from scratch? This fully rescans embedded metadata and can take a while.",
                    "PixelVault",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (choice != MessageBoxResult.OK) return;
                ShowLibraryMetadataScanWindow(ResolveStatusWindowOwner(), libraryRoot, null, true, null, delegate
                {
                    RefreshActiveLibraryFolders(false);
                });
            }, "#F4E8D9", Brush("#6D4A1D")));
            leftStack.Children.Add(libraryRow);

            leftStack.Children.Add(new TextBlock { Text = "Utility actions", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            leftStack.Children.Add(new TextBlock { Text = "Preview the next run and jump directly to the intake or destination folders when you need them.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var openRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), ItemHeight = 48 };
            openRow.Children.Add(Btn("Preview Intake", delegate { ShowIntakePreviewWindow(recurseBox != null && recurseBox.IsChecked == true); }, "#DCEEFF", Brush("#174A73")));
            openRow.Children.Add(Btn("Open Sources", delegate { OpenSourceFolders(); }, "#EEF2F5", Brush("#33424D")));
            openRow.Children.Add(Btn("Open Destination", delegate { OpenFolder(destinationRoot); }, "#EEF2F5", Brush("#33424D")));
            leftStack.Children.Add(openRow);
            var leftScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 4, 0)
            };
            leftScroll.Content = leftStack;
            leftGrid.Children.Add(leftScroll);

            previewBox = new RichTextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 12, 0, 0),
                MinHeight = 160,
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
        Border BuildSettingsSummary()
        {
            var border = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Current paths", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Sources: " + SourceRootsSummary(), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Destination: " + destinationRoot, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Library: " + libraryWorkspace.LibraryRoot, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "My Covers: " + savedCoversRoot, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "ExifTool: " + exifToolPath, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "FFmpeg: " + (string.IsNullOrWhiteSpace(ffmpegPath) ? "(not configured)" : ffmpegPath), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F"), Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "SteamGridDB: " + (HasSteamGridDbApiToken() ? "token configured" : "(token not configured)"), TextWrapping = TextWrapping.Wrap, Foreground = Brush("#4C463F") });
            border.Child = stack;
            return border;
        }

        void ShowPathSettingsWindow()
        {
            var window = new Window
            {
                Title = "PixelVault " + AppVersion + " Path Settings",
                Width = 780,
                Height = 660,
                MinWidth = 640,
                MinHeight = 520,
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

            var pathScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };
            Grid.SetRow(pathScroll, 0);
            root.Children.Add(pathScroll);
            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            var save = Btn("Save Settings", null, "#275D47", Brushes.White);
            save.Margin = new Thickness(0, 0, 12, 0);
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
    }
}
