using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelVaultNative
{
    /// <summary>Owns Settings and Path Settings modal UI; <see cref="MainWindow"/> supplies <see cref="SettingsShellDependencies"/>.</summary>
    public sealed class SettingsShellHost
    {
        readonly SettingsShellDependencies d;

        public SettingsShellHost(SettingsShellDependencies dependencies)
        {
            d = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public void ShowMainSettingsDialog()
        {
            var window = new Window
            {
                Title = "PixelVault " + d.AppVersion + " Settings",
                Width = 1200,
                Height = PreferredSettingsWindowHeight(),
                MinWidth = 900,
                MinHeight = 560,
                Owner = d.OwnerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = d.Brush("#0F1519")
            };

            var previousStatus = d.GetStatusLine();
            var previousLogBox = d.GetLogBox();
            window.Content = BuildUi();
            d.LoadLogView();
            window.Closed += delegate
            {
                d.SetStatusLine(previousStatus);
                d.SetLogBox(previousLogBox);
                d.SyncIncludeGameCaptureKeywordsMirror();
            };
            window.ShowDialog();
        }

        public void ShowPathSettingsDialog()
        {
            var pageBg = d.Brush("#0F1519");
            var panelBg = d.Brush("#141B20");
            var borderBrush = d.Brush("#27313A");
            var labelFg = d.Brush("#A7B5BD");
            var boxBg = d.Brush("#0D1218");
            var boxFg = d.Brush("#E8EEF2");

            var window = new Window
            {
                Title = "PixelVault " + d.AppVersion + " Path Settings",
                Width = 780,
                Height = 660,
                MinWidth = 640,
                MinHeight = 520,
                Owner = d.OwnerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = pageBg
            };

            var root = new Grid { Margin = new Thickness(24), Background = panelBg };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sourceBox = SettingsTextBox(panel, 0, "Source folders", d.SourceRootsEditorText(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            sourceBox.AcceptsReturn = true;
            sourceBox.TextWrapping = TextWrapping.Wrap;
            sourceBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sourceBox.Height = 96;
            var destinationBox = SettingsTextBox(panel, 1, "Destination folder", d.GetDestinationRoot(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            var libraryBox = SettingsTextBox(panel, 2, "Library folder", d.GetLibraryRoot(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            var exifBox = SettingsTextBox(panel, 3, "ExifTool path", d.GetExifToolPath(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            var ffmpegBox = SettingsTextBox(panel, 4, "FFmpeg path", d.GetFfmpegPath(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            var steamGridDbTokenBox = SettingsTextBox(panel, 5, "SteamGridDB token", d.GetSteamGridDbApiToken(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            steamGridDbTokenBox.ToolTip = "Stored locally in PixelVault.settings.ini. Environment variables can also override it.";
            var starredExportBox = SettingsTextBox(panel, 6, "Starred export folder (optional)", d.GetStarredExportFolder(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            starredExportBox.ToolTip = "Library → Export Starred copies files marked starred in the photo index here. Existing files with the same name are replaced.";

            SettingsBrowseButton(panel, 0, delegate { var picked = d.PickFolder(d.PrimarySourceRoot()); if (!string.IsNullOrWhiteSpace(picked)) sourceBox.Text = d.AppendSourceRoot(sourceBox.Text, picked); }, "Add Folder");
            SettingsBrowseButton(panel, 1, delegate { var picked = d.PickFolder(destinationBox.Text); if (!string.IsNullOrWhiteSpace(picked)) destinationBox.Text = picked; });
            SettingsBrowseButton(panel, 2, delegate { var picked = d.PickFolder(libraryBox.Text); if (!string.IsNullOrWhiteSpace(picked)) libraryBox.Text = picked; });
            SettingsBrowseButton(panel, 3, delegate { var picked = d.PickFile(exifBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*", null); if (!string.IsNullOrWhiteSpace(picked)) exifBox.Text = picked; });
            SettingsBrowseButton(panel, 4, delegate { var picked = d.PickFile(ffmpegBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*", null); if (!string.IsNullOrWhiteSpace(picked)) ffmpegBox.Text = picked; });
            SettingsBrowseButton(panel, 6, delegate { var picked = d.PickFolder(starredExportBox.Text); if (!string.IsNullOrWhiteSpace(picked)) starredExportBox.Text = picked; });

            var pathScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };
            Grid.SetRow(pathScroll, 0);
            root.Children.Add(pathScroll);
            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            var save = d.Btn("Save Settings", null, "#2B7A52", Brushes.White);
            save.Margin = new Thickness(0, 0, 12, 0);
            var cancel = d.Btn("Cancel", null, "#20343A", Brushes.White);
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            window.Content = root;

            save.Click += delegate
            {
                d.SetSourceRoot(d.SerializeSourceRoots(sourceBox.Text));
                d.SetDestinationRoot(destinationBox.Text);
                d.SetLibraryRoot(libraryBox.Text);
                d.SetStarredExportFolder((starredExportBox.Text ?? string.Empty).Trim());
                d.SetExifToolPath(exifBox.Text);
                d.SetFfmpegPath(ffmpegBox.Text);
                d.ClearFailedFfmpegPosterKeys();
                d.SetSteamGridDbApiToken((steamGridDbTokenBox.Text ?? string.Empty).Trim());
                d.SaveSettings();
                d.RefreshMainUi();
                window.Close();
                d.Log("Settings saved.");
            };
            cancel.Click += delegate { window.Close(); };
            window.ShowDialog();
        }

        UIElement BuildUi()
        {
            var pageBg = d.Brush("#0F1519");
            var cardBg = d.Brush("#141B20");
            var cardBorder = d.Brush("#27313A");
            var textPrimary = d.Brush("#F4F7FA");
            var textMuted = d.Brush("#8FA1AD");
            var textSoft = d.Brush("#A7B5BD");
            var accentHeader = d.Brush("#161E24");

            var root = new Grid { Margin = new Thickness(20), Background = pageBg };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border
            {
                Background = accentHeader,
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(22),
                Margin = new Thickness(0, 0, 0, 16)
            };
            var headerGrid = new Grid();
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var hs = new StackPanel();
            hs.Children.Add(new TextBlock { Text = "PixelVault Settings", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = textPrimary });
            hs.Children.Add(new TextBlock
            {
                Text = "Paths, editors, and diagnostics. Import and upload queue tools live on the Library toolbar.",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = textSoft,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });
            var statusLocal = new TextBlock { Text = "Ready", Foreground = textPrimary, VerticalAlignment = VerticalAlignment.Center };
            d.SetStatusLine(statusLocal);
            var headerActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            Action<Button> styleHeaderBtn = delegate(Button b) { b.Margin = new Thickness(0, 0, 10, 8); };
            var pathSettingsTopButton = d.Btn("Path Settings", delegate { d.OpenPathSettingsDialog?.Invoke(); }, "#2B7A52", Brushes.White);
            styleHeaderBtn(pathSettingsTopButton);
            var viewLogsTopButton = d.Btn("View Logs", delegate { d.OpenFolder(d.LogsRoot); }, "#20343A", Brushes.White);
            styleHeaderBtn(viewLogsTopButton);
            var myCoversTopButton = d.Btn("My Covers", delegate { d.OpenSavedCoversFolder(); }, "#20343A", Brushes.White);
            styleHeaderBtn(myCoversTopButton);
            var gameIndexTopButton = d.Btn("Game Index", delegate { d.OpenGameIndexEditor(); }, "#20343A", Brushes.White);
            styleHeaderBtn(gameIndexTopButton);
            var photoIndexTopButton = d.Btn("Photo Index", delegate { d.OpenPhotoIndexEditor(); }, "#20343A", Brushes.White);
            styleHeaderBtn(photoIndexTopButton);
            var photographyTopButton = d.Btn("Photography", delegate { d.ShowPhotographyGallery(Window.GetWindow(statusLocal)); }, "#20343A", Brushes.White);
            photographyTopButton.ToolTip = "Browse captures tagged for game photography";
            styleHeaderBtn(photographyTopButton);
            var filenameRulesTopButton = d.Btn("Filename Rules", delegate { d.OpenFilenameConventionEditor(); }, "#20343A", Brushes.White);
            styleHeaderBtn(filenameRulesTopButton);
            var changelogTopButton = d.Btn("Changelog", delegate { ChangelogWindow.ShowDialog(d.OwnerWindow, d.AppVersion, d.ChangelogPath); }, "#20343A", Brushes.White);
            styleHeaderBtn(changelogTopButton);
            var sp = new Border
            {
                Child = statusLocal,
                Background = d.Brush("#1A242C"),
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 10, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
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
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);

            var left = SettingsCardSurface(cardBg, cardBorder);
            left.Margin = new Thickness(0, 0, 14, 0);
            var leftScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 4, 12, 12)
            };
            var leftStack = new StackPanel();
            leftStack.Children.Add(TitleBlock("Overview", textPrimary));
            leftStack.Children.Add(new TextBlock
            {
                Text = "Use Path Settings to change folders and tools. Editors open in their own windows.",
                Foreground = textMuted,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });
            leftStack.Children.Add(BuildSettingsSummary(cardBg, cardBorder, textPrimary, textMuted));
            leftStack.Children.Add(BuildLibraryBehaviorSummary(cardBg, cardBorder, textPrimary, textMuted));
            leftStack.Children.Add(BuildDiagnosticsSummary(cardBg, cardBorder, textPrimary, textMuted, textSoft));
            leftScroll.Content = leftStack;
            left.Child = leftScroll;
            main.Children.Add(left);

            var right = new Border
            {
                Background = d.Brush("#111820"),
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14)
            };
            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.Children.Add(new TextBlock { Text = "Run history", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = d.Brush("#E8EEF2"), Margin = new Thickness(0, 0, 0, 8) });
            var logBoxLocal = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(1),
                BorderBrush = d.Brush("#26313A"),
                Background = d.Brush("#0D1218"),
                Foreground = d.Brush("#D8E4EA"),
                CaretBrush = d.Brush("#D8E4EA"),
                FontFamily = new FontFamily("Cascadia Mono")
            };
            d.SetLogBox(logBoxLocal);
            Grid.SetRow(logBoxLocal, 1);
            rightGrid.Children.Add(logBoxLocal);
            right.Child = rightGrid;
            Grid.SetColumn(right, 1);
            main.Children.Add(right);

            return root;
        }

        static Border SettingsCardSurface(Brush bg, Brush border)
        {
            return new Border
            {
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Effect = new DropShadowEffect { Color = Color.FromArgb(48, 0, 0, 0), BlurRadius = 20, ShadowDepth = 3, Direction = 270, Opacity = 0.35 }
            };
        }

        static double PreferredSettingsWindowHeight()
        {
            var available = Math.Max(600, SystemParameters.WorkArea.Height - 32);
            return Math.Min(available, 1200);
        }

        static TextBlock TitleBlock(string title, Brush foreground)
        {
            return new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), Foreground = foreground };
        }

        Border BuildSettingsSummary(Brush cardBg, Brush cardBorder, Brush textPrimary, Brush textMuted)
        {
            var border = new Border { Background = cardBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = cardBorder, BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Current paths", FontWeight = FontWeights.SemiBold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Sources: " + d.SourceRootsSummary(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Destination: " + d.GetDestinationRoot(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Library: " + d.GetLibraryWorkspaceRoot(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "My Covers: " + d.GetSavedCoversRoot(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "ExifTool: " + d.GetExifToolPath(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "FFmpeg: " + (string.IsNullOrWhiteSpace(d.GetFfmpegPath()) ? "(not configured)" : d.GetFfmpegPath()), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "SteamGridDB: " + (d.HasSteamGridDbApiToken() ? "token configured" : "(token not configured)"), TextWrapping = TextWrapping.Wrap, Foreground = textMuted });
            border.Child = stack;
            return border;
        }

        Border BuildLibraryBehaviorSummary(Brush cardBg, Brush cardBorder, Brush textPrimary, Brush textMuted)
        {
            var border = new Border { Background = cardBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = cardBorder, BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Library", FontWeight = FontWeights.SemiBold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock
            {
                Text = "Optional gestures on captures in the folder detail view (right pane). Folder tiles still use the right-click menu for custom covers.",
                Foreground = textMuted,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            });
            var doubleClickCoverBox = new CheckBox
            {
                Content = "Double-click or right-click → “Use as folder cover” on a still image",
                IsChecked = d.GetLibraryDoubleClickSetsFolderCover(),
                Margin = new Thickness(0, 0, 0, 0),
                Foreground = textPrimary
            };
            doubleClickCoverBox.Checked += delegate
            {
                d.SetLibraryDoubleClickSetsFolderCover(true);
                d.SaveSettings();
                d.Log("Library: double-click / context menu can set folder cover.");
            };
            doubleClickCoverBox.Unchecked += delegate
            {
                d.SetLibraryDoubleClickSetsFolderCover(false);
                d.SaveSettings();
                d.Log("Library: double-click opens files (folder cover gesture off).");
            };
            stack.Children.Add(doubleClickCoverBox);
            border.Child = stack;
            return border;
        }

        Border BuildDiagnosticsSummary(Brush cardBg, Brush cardBorder, Brush textPrimary, Brush textMuted, Brush textSoft)
        {
            var border = new Border { Background = cardBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = cardBorder, BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Diagnostics", FontWeight = FontWeights.SemiBold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Optional extra logging for library and UI timing. Separate from the run history on the right.", Foreground = textMuted, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = "Troubleshooting lines: [UTC] DIAG | S<session> | T<thread> | <Area> | <message>. Session id is fixed for one window instance; use it to grep a single run. Logs are UTC. Large troubleshooting files rotate automatically.", Foreground = textSoft, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var enableTroubleshootingBox = new CheckBox { Content = "Enable troubleshooting logging", IsChecked = d.GetTroubleshootingLoggingEnabled(), Margin = new Thickness(0, 0, 0, 8), Foreground = textPrimary };
            enableTroubleshootingBox.Checked += delegate
            {
                d.SetTroubleshootingLoggingEnabled(true);
                d.SaveSettings();
                d.Log("Troubleshooting logging enabled.");
                d.LogTroubleshooting("Session", "Troubleshooting logging enabled.");
            };
            enableTroubleshootingBox.Unchecked += delegate
            {
                d.LogTroubleshooting("Session", "Troubleshooting logging disabled.");
                d.SetTroubleshootingLoggingEnabled(false);
                d.SaveSettings();
                d.Log("Troubleshooting logging disabled.");
            };
            stack.Children.Add(enableTroubleshootingBox);
            var redactPathsBox = new CheckBox { Content = "Redact folder paths in troubleshooting log (only the last path segment is kept)", IsChecked = d.GetTroubleshootingLogRedactPaths(), Margin = new Thickness(0, 0, 0, 8), Foreground = textPrimary };
            redactPathsBox.Checked += delegate
            {
                d.SetTroubleshootingLogRedactPaths(true);
                d.SaveSettings();
                d.Log("Troubleshooting path redaction enabled.");
            };
            redactPathsBox.Unchecked += delegate
            {
                d.SetTroubleshootingLogRedactPaths(false);
                d.SaveSettings();
                d.Log("Troubleshooting path redaction disabled.");
            };
            stack.Children.Add(redactPathsBox);
            stack.Children.Add(new TextBlock { Text = "Normal log: " + d.LogFilePath(), Foreground = textSoft, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = "Troubleshooting log: " + d.TroubleshootingLogFilePath(), Foreground = textSoft, TextWrapping = TextWrapping.Wrap });
            border.Child = stack;
            return border;
        }

        TextBox SettingsTextBox(Grid panel, int row, string label, string value, Brush labelForeground, Brush boxBackground, Brush boxForeground, Brush boxBorderBrush, Brush boxCaretBrush)
        {
            while (panel.RowDefinitions.Count <= row) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, Margin = new Thickness(0, row == 0 ? 0 : 12, 12, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = labelForeground ?? Brushes.Black };
            Grid.SetRow(text, row);
            panel.Children.Add(text);
            var box = new TextBox { Text = value, Margin = new Thickness(0, row == 0 ? 0 : 12, 12, 0), Padding = new Thickness(8) };
            if (boxBackground != null) box.Background = boxBackground;
            if (boxForeground != null)
            {
                box.Foreground = boxForeground;
                box.CaretBrush = boxCaretBrush ?? boxForeground;
            }
            if (boxBorderBrush != null) box.BorderBrush = boxBorderBrush;
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            panel.Children.Add(box);
            return box;
        }

        void SettingsBrowseButton(Grid panel, int row, RoutedEventHandler click, string label = "Browse")
        {
            var button = d.Btn(label, click, null, Brushes.Black);
            button.Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, 2);
            panel.Children.Add(button);
        }
    }
}
