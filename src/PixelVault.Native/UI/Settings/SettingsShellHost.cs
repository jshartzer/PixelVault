using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PixelVaultNative.UI.Design;

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

        public void ShowHealthDashboardDialog()
        {
            HealthDashboardWindow.ShowDialog(d.OwnerWindow, d);
        }

        public void ShowPathSettingsDialog()
        {
            var pageBg = d.Brush(DesignTokens.PageBackground);
            var panelBg = d.Brush(DesignTokens.PanelElevated);
            var borderBrush = d.Brush(DesignTokens.BorderDefault);
            var labelFg = d.Brush(DesignTokens.TextLabelMuted);
            var boxBg = d.Brush(DesignTokens.InputBackground);
            var boxFg = d.Brush(DesignTokens.TextOnInput);

            var window = new Window
            {
                Title = "PixelVault " + d.AppVersion + " Path Settings",
                Width = 780,
                Height = 860,
                MinWidth = 640,
                MinHeight = 560,
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
            var steamWebApiKeyBox = SettingsTextBox(panel, 6, "Steam Web API key (optional)", d.GetSteamWebApiKey(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            steamWebApiKeyBox.ToolTip = "Valve Steam Web API key (steam_web_api_key in settings.ini). Optional PIXELVAULT_STEAM_WEB_API_KEY / STEAM_WEB_API_KEY env vars override the file.";
            var retroAchievementsKeyBox = SettingsTextBox(panel, 7, "RetroAchievements API key (optional)", d.GetRetroAchievementsApiKey(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            retroAchievementsKeyBox.ToolTip = "RetroAchievements.org API key (retroachievements_api_key). Optional PIXELVAULT_RETROACHIEVEMENTS_API_KEY / RETROACHIEVEMENTS_API_KEY env vars override the file.";
            var steamUserIdBox = SettingsTextBox(panel, 8, "Your SteamID64 (optional)", d.GetSteamUserId64?.Invoke() ?? string.Empty, labelFg, boxBg, boxFg, borderBrush, boxFg);
            steamUserIdBox.ToolTip = "Steam Web API: used with GetPlayerAchievements so the library can show which Steam achievements you unlocked (steam_user_id_64). Env: PIXELVAULT_STEAM_USER_ID / STEAMID64.";
            var retroUserBox = SettingsTextBox(panel, 9, "RetroAchievements username (optional)", d.GetRetroAchievementsUsername?.Invoke() ?? string.Empty, labelFg, boxBg, boxFg, borderBrush, boxFg);
            retroUserBox.ToolTip = "retro_user on RetroAchievements.org — required for unlock/progress in the achievements viewer (retroachievements_username). Env: PIXELVAULT_RETROACHIEVEMENTS_USERNAME / RA_USERNAME.";
            var starredExportBox = SettingsTextBox(panel, 10, "Starred export folder (optional)", d.GetStarredExportFolder(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            starredExportBox.ToolTip = "Library → Export Starred copies starred files here (mirrored paths under the library root). Only files that are new to the export or whose metadata/file stamp changed are copied again; tracking lives in that library’s SQLite index. Replaces existing files when needed.";

            SettingsBrowseButton(panel, 0, delegate { var picked = d.PickFolder(d.PrimarySourceRoot()); if (!string.IsNullOrWhiteSpace(picked)) sourceBox.Text = d.AppendSourceRoot(sourceBox.Text, picked); }, "Add Folder");
            SettingsBrowseButton(panel, 1, delegate { var picked = d.PickFolder(destinationBox.Text); if (!string.IsNullOrWhiteSpace(picked)) destinationBox.Text = picked; });
            SettingsBrowseButton(panel, 2, delegate { var picked = d.PickFolder(libraryBox.Text); if (!string.IsNullOrWhiteSpace(picked)) libraryBox.Text = picked; });
            SettingsBrowseButton(panel, 3, delegate { var picked = d.PickFile(exifBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*", null); if (!string.IsNullOrWhiteSpace(picked)) exifBox.Text = picked; });
            SettingsBrowseButton(panel, 4, delegate { var picked = d.PickFile(ffmpegBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*", null); if (!string.IsNullOrWhiteSpace(picked)) ffmpegBox.Text = picked; });
            SettingsBrowseButton(panel, 10, delegate { var picked = d.PickFolder(starredExportBox.Text); if (!string.IsNullOrWhiteSpace(picked)) starredExportBox.Text = picked; });

            var intakeHeader = new TextBlock
            {
                Text = "Background auto-intake",
                FontWeight = FontWeights.SemiBold,
                Foreground = labelFg,
                Margin = new Thickness(0, 18, 0, 6)
            };
            Grid.SetRow(intakeHeader, 11);
            Grid.SetColumnSpan(intakeHeader, 3);
            while (panel.RowDefinitions.Count <= 11) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(intakeHeader);

            var autoIntakeEnableBox = new CheckBox
            {
                Content = "Watch source folders and auto-import eligible files (rules + metadata)",
                IsChecked = d.GetBackgroundAutoIntakeEnabled?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            autoIntakeEnableBox.ToolTip =
                "Runs the same standard import pipeline as the Library for matching top-level files. Custom rules need 'Trusted exact match' in Filename Conventions; built-ins import when automatic metadata is fully possible. "
                + "Review and undo: Library command palette → Background imports.";
            Grid.SetRow(autoIntakeEnableBox, 12);
            Grid.SetColumnSpan(autoIntakeEnableBox, 3);
            while (panel.RowDefinitions.Count <= 12) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(autoIntakeEnableBox);

            var quietSecondsBox = SettingsTextBox(panel, 13, "Stability quiet period (seconds)", (d.GetBackgroundAutoIntakeQuietSeconds?.Invoke() ?? 3).ToString(CultureInfo.InvariantCulture), labelFg, boxBg, boxFg, borderBrush, boxFg);
            quietSecondsBox.ToolTip = "The file must stay unchanged (size and write time) for this long after activity stops. Allowed range: 1–120 seconds.";

            var toastBox = new CheckBox
            {
                Content = "Show toasts for background import",
                IsChecked = d.GetBackgroundAutoIntakeToastsEnabled?.Invoke() ?? true,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            Grid.SetRow(toastBox, 14);
            Grid.SetColumnSpan(toastBox, 3);
            while (panel.RowDefinitions.Count <= 14) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(toastBox);

            var summaryBox = new CheckBox
            {
                Content = "Toast when a background batch finishes (off: only main log + command palette)",
                IsChecked = d.GetBackgroundAutoIntakeShowSummary?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            summaryBox.ToolTip = "Does not open the Background imports window. Use Library command palette → Background imports anytime. Success toasts include a Review button when the library window is active.";
            Grid.SetRow(summaryBox, 15);
            Grid.SetColumnSpan(summaryBox, 3);
            while (panel.RowDefinitions.Count <= 15) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(summaryBox);

            var verboseIntakeBox = new CheckBox
            {
                Content = "Verbose background intake log ([BGINT] in main log)",
                IsChecked = d.GetBackgroundAutoIntakeVerboseLogging?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            verboseIntakeBox.ToolTip = "Writes detailed lines prefixed with [BGINT] to the normal PixelVault log (watchers, queue, stability, eligibility). Turn off after diagnosing.";
            Grid.SetRow(verboseIntakeBox, 16);
            Grid.SetColumnSpan(verboseIntakeBox, 3);
            while (panel.RowDefinitions.Count <= 16) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(verboseIntakeBox);

            var trayHeader = new TextBlock
            {
                Text = "System tray",
                FontWeight = FontWeights.SemiBold,
                Foreground = labelFg,
                Margin = new Thickness(0, 18, 0, 6)
            };
            Grid.SetRow(trayHeader, 17);
            Grid.SetColumnSpan(trayHeader, 3);
            while (panel.RowDefinitions.Count <= 17) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(trayHeader);

            var minimizeToTrayBox = new CheckBox
            {
                Content = "When minimized, hide PixelVault to the system tray instead of leaving it on the taskbar",
                IsChecked = d.GetSystemTrayMinimizeEnabled?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            minimizeToTrayBox.ToolTip = "Useful if you want background auto-intake to keep running without a taskbar window.";
            Grid.SetRow(minimizeToTrayBox, 18);
            Grid.SetColumnSpan(minimizeToTrayBox, 3);
            while (panel.RowDefinitions.Count <= 18) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(minimizeToTrayBox);

            var promptOnCloseBox = new CheckBox
            {
                Content = "When closing PixelVault, ask whether to keep it running in the system tray",
                IsChecked = d.GetSystemTrayPromptOnCloseEnabled?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            promptOnCloseBox.ToolTip = "The tray icon shows recent background-import activity and quick actions like Restore and Background Imports.";
            Grid.SetRow(promptOnCloseBox, 19);
            Grid.SetColumnSpan(promptOnCloseBox, 3);
            while (panel.RowDefinitions.Count <= 19) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(promptOnCloseBox);

            var pathScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };
            Grid.SetRow(pathScroll, 0);
            root.Children.Add(pathScroll);
            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            var save = d.Btn("Save Settings", null, DesignTokens.ActionPrimaryFill, Brushes.White);
            save.Margin = new Thickness(0, 0, 12, 0);
            var cancel = d.Btn("Cancel", null, DesignTokens.ActionSecondaryFill, Brushes.White);
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
                d.SetSteamWebApiKey((steamWebApiKeyBox.Text ?? string.Empty).Trim());
                d.SetRetroAchievementsApiKey((retroAchievementsKeyBox.Text ?? string.Empty).Trim());
                d.SetSteamUserId64((steamUserIdBox.Text ?? string.Empty).Trim());
                d.SetRetroAchievementsUsername((retroUserBox.Text ?? string.Empty).Trim());
                d.SetBackgroundAutoIntakeEnabled(autoIntakeEnableBox.IsChecked == true);
                if (int.TryParse((quietSecondsBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quietParsed))
                    d.SetBackgroundAutoIntakeQuietSeconds(quietParsed);
                else
                    d.SetBackgroundAutoIntakeQuietSeconds(3);
                d.SetBackgroundAutoIntakeToastsEnabled(toastBox.IsChecked == true);
                d.SetBackgroundAutoIntakeShowSummary(summaryBox.IsChecked == true);
                d.SetBackgroundAutoIntakeVerboseLogging(verboseIntakeBox.IsChecked == true);
                d.SetSystemTrayMinimizeEnabled(minimizeToTrayBox.IsChecked == true);
                d.SetSystemTrayPromptOnCloseEnabled(promptOnCloseBox.IsChecked == true);
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
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var hs = new StackPanel();
            hs.Children.Add(new TextBlock { Text = "PixelVault Settings", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = textPrimary });
            hs.Children.Add(new TextBlock
            {
                Text = "Paths, health, and logs here; editors and storage merge live in the panel below. Import and queue review stay on the Library toolbar.",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = textSoft,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });
            var statusLocal = new TextBlock { Text = "Ready", Foreground = textPrimary, VerticalAlignment = VerticalAlignment.Center };
            d.SetStatusLine(statusLocal);
            var quickActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            Action<Button> styleHeaderBtn = delegate(Button b) { b.Margin = new Thickness(0, 0, 10, 8); };
            var pathSettingsTopButton = d.Btn("Path Settings", delegate { d.OpenPathSettingsDialog?.Invoke(); }, DesignTokens.ActionPrimaryFill, Brushes.White);
            styleHeaderBtn(pathSettingsTopButton);
            var healthTopButton = d.Btn("Setup & health", delegate { ShowHealthDashboardDialog(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleHeaderBtn(healthTopButton);
            var viewLogsTopButton = d.Btn("View Logs", delegate { d.OpenFolder(d.LogsRoot); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleHeaderBtn(viewLogsTopButton);
            quickActions.Children.Add(pathSettingsTopButton);
            quickActions.Children.Add(healthTopButton);
            quickActions.Children.Add(viewLogsTopButton);
            var statusBar = new Border
            {
                Child = statusLocal,
                Background = d.Brush("#1A242C"),
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(hs, 0);
            headerGrid.Children.Add(hs);
            Grid.SetRow(quickActions, 1);
            headerGrid.Children.Add(quickActions);
            Grid.SetRow(statusBar, 2);
            headerGrid.Children.Add(statusBar);
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
                Text = "Path Settings (header) opens the full dialog for folders and credentials. Use Library tools for editors, cover fetch, and merging storage folders.",
                Foreground = textMuted,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });
            leftStack.Children.Add(BuildLibraryToolsCard(cardBg, cardBorder, textPrimary, textMuted));
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

        Border BuildLibraryToolsCard(Brush cardBg, Brush cardBorder, Brush textPrimary, Brush textMuted)
        {
            var border = new Border { Background = cardBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = cardBorder, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Library tools", FontWeight = FontWeights.SemiBold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock
            {
                Text = "Editors and maintenance for the library on disk. Merge folders consolidates captures into shared storage folders when one platform uses multiple disk folders.",
                Foreground = textMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            Action<Button> styleToolBtn = delegate(Button b) { b.Margin = new Thickness(0, 0, 10, 8); };
            var gameIndexBtn = d.Btn("Game Index", delegate { d.OpenGameIndexEditor(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleToolBtn(gameIndexBtn);
            var photoIndexBtn = d.Btn("Photo Index", delegate { d.OpenPhotoIndexEditor(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleToolBtn(photoIndexBtn);
            var mergeBtn = d.Btn("Merge folders", delegate { d.OpenLibraryStorageMergeTool?.Invoke(d.OwnerWindow); }, DesignTokens.ActionShortcutDismissFill, Brushes.White);
            mergeBtn.ToolTip = "Preview and apply moving captures into shared storage folders (same storage group).";
            styleToolBtn(mergeBtn);
            var myCoversBtn = d.Btn("My Covers", delegate { d.OpenSavedCoversFolder(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleToolBtn(myCoversBtn);
            var photoGalleryBtn = d.Btn("Photography", delegate { d.ShowPhotographyGallery(d.OwnerWindow); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            photoGalleryBtn.ToolTip = "Browse captures tagged for game photography";
            styleToolBtn(photoGalleryBtn);
            var renameBtn = d.Btn("Renaming rules", delegate { d.OpenFilenameConventionEditor(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            renameBtn.ToolTip = "How capture filenames are parsed for imports and grouping";
            styleToolBtn(renameBtn);
            var fetchCoversBtn = d.Btn(
                "Fetch covers",
                delegate { d.PromptFetchCoversForLibrary?.Invoke(d.OwnerWindow); },
                DesignTokens.ActionShortcutDismissFill,
                Brushes.White);
            fetchCoversBtn.ToolTip = "Resolve IDs and download cover art for the whole library (occasional maintenance)";
            styleToolBtn(fetchCoversBtn);
            var changelogBtn = d.Btn("Changelog", delegate { ChangelogWindow.ShowDialog(d.OwnerWindow, d.AppVersion, d.ChangelogPath); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleToolBtn(changelogBtn);
            wrap.Children.Add(gameIndexBtn);
            wrap.Children.Add(photoIndexBtn);
            wrap.Children.Add(mergeBtn);
            wrap.Children.Add(myCoversBtn);
            wrap.Children.Add(photoGalleryBtn);
            wrap.Children.Add(renameBtn);
            wrap.Children.Add(fetchCoversBtn);
            wrap.Children.Add(changelogBtn);
            stack.Children.Add(wrap);
            border.Child = stack;
            return border;
        }

        Border BuildSettingsSummary(Brush cardBg, Brush cardBorder, Brush textPrimary, Brush textMuted)
        {
            var border = new Border { Background = cardBg, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), BorderBrush = cardBorder, BorderThickness = new Thickness(1) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "Paths & credentials", FontWeight = FontWeights.SemiBold, Foreground = textPrimary, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = "Sources: " + d.SourceRootsSummary(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Destination: " + d.GetDestinationRoot(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "Library: " + d.GetLibraryWorkspaceRoot(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "My Covers: " + d.GetSavedCoversRoot(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "ExifTool: " + d.GetExifToolPath(), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "FFmpeg: " + (string.IsNullOrWhiteSpace(d.GetFfmpegPath()) ? "(not configured)" : d.GetFfmpegPath()), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "SteamGridDB: " + (d.HasSteamGridDbApiToken() ? "token configured" : "(token not configured)"), TextWrapping = TextWrapping.Wrap, Foreground = textMuted });
            stack.Children.Add(new TextBlock { Text = "Steam Web API: " + ((d.HasSteamWebApiKey?.Invoke() ?? false) ? "key configured" : "(not configured)"), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 4, 0, 0) });
            stack.Children.Add(new TextBlock { Text = "RetroAchievements: " + ((d.HasRetroAchievementsApiKey?.Invoke() ?? false) ? "API key configured" : "(not configured)"), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 4, 0, 0) });
            stack.Children.Add(new TextBlock { Text = "Steam user (achievements): " + (string.IsNullOrWhiteSpace(d.GetSteamUserId64?.Invoke()) ? "(not set)" : "SteamID64 set"), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 4, 0, 0) });
            stack.Children.Add(new TextBlock { Text = "RetroAchievements user: " + (string.IsNullOrWhiteSpace(d.GetRetroAchievementsUsername?.Invoke()) ? "(not set)" : "username set"), TextWrapping = TextWrapping.Wrap, Foreground = textMuted, Margin = new Thickness(0, 4, 0, 0) });
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
            var refreshHeroBannerCacheBox = new CheckBox
            {
                Content = "On next Library load: clear cached capture hero banners (re-download with current sources)",
                IsChecked = d.GetLibraryRefreshHeroBannerCacheOnNextLibraryOpen?.Invoke() ?? false,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = textPrimary
            };
            refreshHeroBannerCacheBox.ToolTip =
                "Removes auto-downloaded wide banner files under My Covers (hero-*). Custom banners you set are not deleted. "
                + "After the library reloads, open captures for a game or use Fetch Banner Art to pull SteamGridDB Heroes / Steam library hero again.";
            refreshHeroBannerCacheBox.Checked += delegate
            {
                if (d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen != null) d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen(true);
                d.SaveSettings();
                d.Log("Library: scheduled cached hero banner clear on next folder load.");
            };
            refreshHeroBannerCacheBox.Unchecked += delegate
            {
                if (d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen != null) d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen(false);
                d.SaveSettings();
                d.Log("Library: cancelled scheduled hero banner cache clear.");
            };
            stack.Children.Add(refreshHeroBannerCacheBox);
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
