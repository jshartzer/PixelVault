using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        UIElement BuildUi()
        {
            var pageBg = Brush("#0F1519");
            var cardBg = Brush("#141B20");
            var cardBorder = Brush("#27313A");
            var textPrimary = Brush("#F4F7FA");
            var textMuted = Brush("#8FA1AD");
            var textSoft = Brush("#A7B5BD");
            var accentHeader = Brush("#161E24");

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
            status = new TextBlock { Text = "Ready", Foreground = textPrimary, VerticalAlignment = VerticalAlignment.Center };
            var headerActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            Action<Button> styleHeaderBtn = delegate(Button b) { b.Margin = new Thickness(0, 0, 10, 8); };
            var pathSettingsTopButton = Btn("Path Settings", delegate { ShowPathSettingsWindow(); }, "#2B7A52", Brushes.White);
            styleHeaderBtn(pathSettingsTopButton);
            var viewLogsTopButton = Btn("View Logs", delegate { OpenFolder(logsRoot); }, "#20343A", Brushes.White);
            styleHeaderBtn(viewLogsTopButton);
            var myCoversTopButton = Btn("My Covers", delegate { OpenSavedCoversFolder(); }, "#20343A", Brushes.White);
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
            var sp = new Border
            {
                Child = status,
                Background = Brush("#1A242C"),
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
            leftStack.Children.Add(BuildDiagnosticsSummary(cardBg, cardBorder, textPrimary, textMuted, textSoft));
            leftScroll.Content = leftStack;
            left.Child = leftScroll;
            main.Children.Add(left);

            var right = new Border
            {
                Background = Brush("#111820"),
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14)
            };
            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.Children.Add(new TextBlock { Text = "Run history", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#E8EEF2"), Margin = new Thickness(0, 0, 0, 8) });
            logBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("#26313A"),
                Background = Brush("#0D1218"),
                Foreground = Brush("#D8E4EA"),
                CaretBrush = Brush("#D8E4EA"),
                FontFamily = new FontFamily("Cascadia Mono")
            };
            Grid.SetRow(logBox, 1);
            rightGrid.Children.Add(logBox);
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

        TextBlock TitleBlock(string title, Brush foreground)
        {
            return new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), Foreground = foreground };
        }

        Border BuildSettingsSummary(Brush cardBg, Brush cardBorder, Brush textPrimary, Brush textMuted)
        {
            var border = new Border { Background = cardBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = cardBorder, BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Current paths", FontWeight = FontWeights.SemiBold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Sources: " + SourceRootsSummary(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Destination: " + destinationRoot, TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Library: " + libraryWorkspace.LibraryRoot, TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "My Covers: " + savedCoversRoot, TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "ExifTool: " + exifToolPath, TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "FFmpeg: " + (string.IsNullOrWhiteSpace(ffmpegPath) ? "(not configured)" : ffmpegPath), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "SteamGridDB: " + (HasSteamGridDbApiToken() ? "token configured" : "(token not configured)"), TextWrapping = TextWrapping.Wrap, Foreground = textMuted });
            border.Child = stack;
            return border;
        }

        Border BuildDiagnosticsSummary(Brush cardBg, Brush cardBorder, Brush textPrimary, Brush textMuted, Brush textSoft)
        {
            var border = new Border { Background = cardBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = cardBorder, BorderThickness = new Thickness(1), Margin = new Thickness(0, 14, 0, 0) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Diagnostics", FontWeight = FontWeights.SemiBold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Optional extra logging for library and UI timing. Separate from the run history on the right.", Foreground = textMuted, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
            var enableTroubleshootingBox = new CheckBox { Content = "Enable troubleshooting logging", IsChecked = troubleshootingLoggingEnabled, Margin = new Thickness(0, 0, 0, 8), Foreground = textPrimary };
            enableTroubleshootingBox.Checked += delegate
            {
                troubleshootingLoggingEnabled = true;
                SaveSettings();
                Log("Troubleshooting logging enabled.");
                LogTroubleshooting("Session", "Troubleshooting logging enabled.");
            };
            enableTroubleshootingBox.Unchecked += delegate
            {
                LogTroubleshooting("Session", "Troubleshooting logging disabled.");
                troubleshootingLoggingEnabled = false;
                SaveSettings();
                Log("Troubleshooting logging disabled.");
            };
            stack.Children.Add(enableTroubleshootingBox);
            stack.Children.Add(new TextBlock { Text = "Normal log: " + LogFilePath(), Foreground = textSoft, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(new TextBlock { Text = "Troubleshooting log: " + TroubleshootingLogFilePath(), Foreground = textSoft, TextWrapping = TextWrapping.Wrap });
            border.Child = stack;
            return border;
        }

        void ShowPathSettingsWindow()
        {
            var pageBg = Brush("#0F1519");
            var panelBg = Brush("#141B20");
            var borderBrush = Brush("#27313A");
            var labelFg = Brush("#A7B5BD");
            var boxBg = Brush("#0D1218");
            var boxFg = Brush("#E8EEF2");

            var window = new Window
            {
                Title = "PixelVault " + AppVersion + " Path Settings",
                Width = 780,
                Height = 660,
                MinWidth = 640,
                MinHeight = 520,
                Owner = this,
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

            var sourceBox = SettingsTextBox(panel, 0, "Source folders", SourceRootsEditorText(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            sourceBox.AcceptsReturn = true;
            sourceBox.TextWrapping = TextWrapping.Wrap;
            sourceBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sourceBox.Height = 96;
            var destinationBox = SettingsTextBox(panel, 1, "Destination folder", destinationRoot, labelFg, boxBg, boxFg, borderBrush, boxFg);
            var libraryBox = SettingsTextBox(panel, 2, "Library folder", libraryRoot, labelFg, boxBg, boxFg, borderBrush, boxFg);
            var exifBox = SettingsTextBox(panel, 3, "ExifTool path", exifToolPath, labelFg, boxBg, boxFg, borderBrush, boxFg);
            var ffmpegBox = SettingsTextBox(panel, 4, "FFmpeg path", ffmpegPath, labelFg, boxBg, boxFg, borderBrush, boxFg);
            var steamGridDbTokenBox = SettingsTextBox(panel, 5, "SteamGridDB token", steamGridDbApiToken, labelFg, boxBg, boxFg, borderBrush, boxFg);
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
            var save = Btn("Save Settings", null, "#2B7A52", Brushes.White);
            save.Margin = new Thickness(0, 0, 12, 0);
            var cancel = Btn("Cancel", null, "#20343A", Brushes.White);
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
                Width = 1200,
                Height = PreferredSettingsWindowHeight(),
                MinWidth = 900,
                MinHeight = 560,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var previousStatus = status;
            var previousLogBox = logBox;
            window.Content = BuildUi();
            LoadLogView();
            window.Closed += delegate
            {
                status = previousStatus;
                logBox = previousLogBox;
                SyncIncludeGameCaptureKeywordsMirror();
            };
            window.ShowDialog();
        }
    }
}
