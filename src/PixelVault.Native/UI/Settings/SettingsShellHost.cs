using System;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    /// <summary>Owns Settings and Path Settings modal UI; <see cref="MainWindow"/> supplies <see cref="SettingsShellDependencies"/>.</summary>
    public sealed partial class SettingsShellHost
    {
        readonly SettingsShellDependencies d;

        public SettingsShellHost(SettingsShellDependencies dependencies)
        {
            d = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public void ShowMainSettingsDialog()
        {
            var opening = Stopwatch.StartNew();
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

            window.Content = BuildUi(window);
            window.ContentRendered += delegate
            {
                d.LogTroubleshooting?.Invoke("Settings", "First render: " + opening.ElapsedMilliseconds + " ms.");
            };
            window.Closed += delegate
            {
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
            var window = new Window
            {
                Title = "PixelVault " + d.AppVersion + " Configuration",
                Width = 1000,
                Height = PreferredSettingsWindowHeight(),
                MinWidth = 800,
                MinHeight = 560,
                Owner = d.OwnerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = d.Brush(DesignTokens.PageBackground)
            };
            window.Content = BuildConfigurationUi(window, true);
            window.ShowDialog();
        }

        Grid BuildConfigurationUi(Window window, bool closeOnSave)
        {
            var pageBg = d.Brush(DesignTokens.PageBackground);
            var borderBrush = d.Brush(DesignTokens.BorderDefault);
            var labelFg = d.Brush(DesignTokens.TextLabelMuted);
            var boxBg = d.Brush(DesignTokens.InputBackground);
            var boxFg = d.Brush(DesignTokens.TextOnInput);

            var root = new Grid { Margin = new Thickness(24), Background = pageBg };
            root.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 13d);
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
            var ffmpegBox = SettingsTextBox(panel, 4, "FFmpeg path (optional)", d.GetFfmpegPath(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            ffmpegBox.ToolTip = "Optional video add-on. Install FFmpeg separately and point PixelVault to ffmpeg.exe to enable video thumbnails, clip previews, and richer clip details.";
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
            var igdbClientIdBox = SettingsTextBox(panel, 10, "IGDB Twitch Client ID (optional)", d.GetIgdbTwitchClientId?.Invoke() ?? string.Empty, labelFg, boxBg, boxFg, borderBrush, boxFg);
            igdbClientIdBox.ToolTip = "Twitch Developer app Client ID for IGDB. Env: PIXELVAULT_IGDB_TWITCH_CLIENT_ID / IGDB_TWITCH_CLIENT_ID / TWITCH_CLIENT_ID.";
            var igdbClientSecretBox = SettingsTextBox(panel, 11, "IGDB Twitch Client Secret (optional)", d.GetIgdbTwitchClientSecret?.Invoke() ?? string.Empty, labelFg, boxBg, boxFg, borderBrush, boxFg);
            igdbClientSecretBox.ToolTip = "Twitch Developer app Client Secret for IGDB app-token auth. Stored locally unless provided by env: PIXELVAULT_IGDB_TWITCH_CLIENT_SECRET / IGDB_TWITCH_CLIENT_SECRET / TWITCH_CLIENT_SECRET.";
            var starredExportBox = SettingsTextBox(panel, 13, "Starred export folder (optional)", d.GetStarredExportFolder(), labelFg, boxBg, boxFg, borderBrush, boxFg);
            starredExportBox.ToolTip =
                "Must be a different folder than the library folder (same path would export onto itself). "
                + "Export Starred copies starred files here using the same subfolders as under the library root, so look inside those subfolders — not only the top level you pick here. "
                + "Only new/changed files copy again; state lives in that library’s SQLite index. "
                + "Replaces existing files when needed.";

            SettingsBrowseButton(panel, 0, delegate { var picked = d.PickFolder(d.PrimarySourceRoot()); if (!string.IsNullOrWhiteSpace(picked)) sourceBox.Text = d.AppendSourceRoot(sourceBox.Text, picked); }, "Add Folder");
            SettingsBrowseButton(panel, 1, delegate { var picked = d.PickFolder(destinationBox.Text); if (!string.IsNullOrWhiteSpace(picked)) destinationBox.Text = picked; });
            SettingsBrowseButton(panel, 2, delegate { var picked = d.PickFolder(libraryBox.Text); if (!string.IsNullOrWhiteSpace(picked)) libraryBox.Text = picked; });
            SettingsBrowseButton(panel, 3, delegate { var picked = d.PickFile(exifBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*", null); if (!string.IsNullOrWhiteSpace(picked)) exifBox.Text = picked; });
            SettingsBrowseButton(panel, 4, delegate { var picked = d.PickFile(ffmpegBox.Text, "Executable (*.exe)|*.exe|All files (*.*)|*.*", null); if (!string.IsNullOrWhiteSpace(picked)) ffmpegBox.Text = picked; });
            var igdbActions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 12, 0) };
            var igdbProbeButton = SettingsButton("Test IGDB fields", null, DesignTokens.ActionSecondaryFill, Brushes.White);
            igdbProbeButton.ToolTip = "Fetches a Twitch app token and runs a tiny IGDB games query for Portal, then writes the returned field paths to Run history.";
            igdbProbeButton.Width = 150;
            var igdbRefreshButton = SettingsButton("Refresh IGDB cache", null, DesignTokens.ActionPrimaryFill, Brushes.White);
            igdbRefreshButton.Margin = new Thickness(10, 0, 0, 0);
            igdbRefreshButton.Width = 180;
            igdbRefreshButton.ToolTip = "Refresh locally stored IGDB summary, release year, developer, series, and cover metadata for saved game-index rows. Profiles use this local data when they open.";
            igdbActions.Children.Add(igdbProbeButton);
            igdbActions.Children.Add(igdbRefreshButton);
            Grid.SetRow(igdbActions, 12);
            Grid.SetColumn(igdbActions, 1);
            while (panel.RowDefinitions.Count <= 12) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(igdbActions);
            igdbProbeButton.Click += async delegate
            {
                if (d.ProbeIgdbFieldsAsync == null) return;
                igdbProbeButton.IsEnabled = false;
                d.Log("IGDB probe: requesting sample game fields...");
                try
                {
                    var result = await d.ProbeIgdbFieldsAsync(
                        (igdbClientIdBox.Text ?? string.Empty).Trim(),
                        (igdbClientSecretBox.Text ?? string.Empty).Trim(),
                        "Portal");
                    d.Log(result);
                    MessageBox.Show(window, result, "PixelVault IGDB probe", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    var msg = "IGDB probe failed: " + ex.Message;
                    d.Log(msg);
                    MessageBox.Show(window, msg, "PixelVault IGDB probe", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    igdbProbeButton.IsEnabled = true;
                }
            };
            igdbRefreshButton.Click += async delegate
            {
                if (d.RefreshIgdbMetadataAsync == null) return;
                igdbRefreshButton.IsEnabled = false;
                igdbProbeButton.IsEnabled = false;
                d.Log("IGDB cache refresh: starting...");
                try
                {
                    var result = await d.RefreshIgdbMetadataAsync(
                        (igdbClientIdBox.Text ?? string.Empty).Trim(),
                        (igdbClientSecretBox.Text ?? string.Empty).Trim());
                    d.Log(result);
                    MessageBox.Show(window, result, "PixelVault IGDB cache", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    var msg = "IGDB cache refresh failed: " + ex.Message;
                    d.Log(msg);
                    MessageBox.Show(window, msg, "PixelVault IGDB cache", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    igdbRefreshButton.IsEnabled = true;
                    igdbProbeButton.IsEnabled = true;
                }
            };
            SettingsBrowseButton(panel, 13, delegate { var picked = d.PickFolder(starredExportBox.Text); if (!string.IsNullOrWhiteSpace(picked)) starredExportBox.Text = picked; });

            var importSubfolderScanBox = new CheckBox
            {
                Content = "During import, also scan subfolders under each source folder",
                IsChecked = d.GetImportSearchSubfoldersForRename?.Invoke() ?? false,
                Margin = new Thickness(0, 18, 0, 4),
                Foreground = boxFg
            };
            importSubfolderScanBox.ToolTip =
                "Default import only inspects the top level of each source folder. Turn this on if you intentionally keep captures in nested folders and want PixelVault to include them in import prep and Steam/non-Steam rename scanning.";
            Grid.SetRow(importSubfolderScanBox, 14);
            Grid.SetColumnSpan(importSubfolderScanBox, 3);
            while (panel.RowDefinitions.Count <= 14) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(importSubfolderScanBox);

            var intakeHeader = new TextBlock
            {
                Text = "Background auto-intake",
                FontWeight = FontWeights.SemiBold,
                Foreground = labelFg,
                Margin = new Thickness(0, 18, 0, 6)
            };
            Grid.SetRow(intakeHeader, 15);
            Grid.SetColumnSpan(intakeHeader, 3);
            while (panel.RowDefinitions.Count <= 15) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            Grid.SetRow(autoIntakeEnableBox, 16);
            Grid.SetColumnSpan(autoIntakeEnableBox, 3);
            while (panel.RowDefinitions.Count <= 16) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(autoIntakeEnableBox);

            var quietSecondsBox = SettingsTextBox(panel, 17, "Stability quiet period (seconds)", (d.GetBackgroundAutoIntakeQuietSeconds?.Invoke() ?? 3).ToString(CultureInfo.InvariantCulture), labelFg, boxBg, boxFg, borderBrush, boxFg);
            quietSecondsBox.ToolTip = "The file must stay unchanged (size and write time) for this long after activity stops. Allowed range: 1–120 seconds.";

            var toastBox = new CheckBox
            {
                Content = "Show toasts for background import",
                IsChecked = d.GetBackgroundAutoIntakeToastsEnabled?.Invoke() ?? true,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            Grid.SetRow(toastBox, 18);
            Grid.SetColumnSpan(toastBox, 3);
            while (panel.RowDefinitions.Count <= 18) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(toastBox);

            var summaryBox = new CheckBox
            {
                Content = "Toast when a background batch finishes (off: only main log + command palette)",
                IsChecked = d.GetBackgroundAutoIntakeShowSummary?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            summaryBox.ToolTip = "Does not open the Background imports window. Use Library command palette → Background imports anytime. Success toasts include a Review button when the library window is active.";
            Grid.SetRow(summaryBox, 19);
            Grid.SetColumnSpan(summaryBox, 3);
            while (panel.RowDefinitions.Count <= 19) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(summaryBox);

            var verboseIntakeBox = new CheckBox
            {
                Content = "Verbose background intake log ([BGINT] in main log)",
                IsChecked = d.GetBackgroundAutoIntakeVerboseLogging?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            verboseIntakeBox.ToolTip = "Writes detailed lines prefixed with [BGINT] to the normal PixelVault log (watchers, queue, stability, eligibility). Turn off after diagnosing.";
            Grid.SetRow(verboseIntakeBox, 20);
            Grid.SetColumnSpan(verboseIntakeBox, 3);
            while (panel.RowDefinitions.Count <= 20) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(verboseIntakeBox);

            var trayHeader = new TextBlock
            {
                Text = "System tray",
                FontWeight = FontWeights.SemiBold,
                Foreground = labelFg,
                Margin = new Thickness(0, 18, 0, 6)
            };
            Grid.SetRow(trayHeader, 21);
            Grid.SetColumnSpan(trayHeader, 3);
            while (panel.RowDefinitions.Count <= 21) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(trayHeader);

            var minimizeToTrayBox = new CheckBox
            {
                Content = "When minimized, hide PixelVault to the system tray instead of leaving it on the taskbar",
                IsChecked = d.GetSystemTrayMinimizeEnabled?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            minimizeToTrayBox.ToolTip = "Useful if you want background auto-intake to keep running without a taskbar window.";
            Grid.SetRow(minimizeToTrayBox, 22);
            Grid.SetColumnSpan(minimizeToTrayBox, 3);
            while (panel.RowDefinitions.Count <= 22) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(minimizeToTrayBox);

            var promptOnCloseBox = new CheckBox
            {
                Content = "When closing PixelVault, ask whether to keep it running in the system tray",
                IsChecked = d.GetSystemTrayPromptOnCloseEnabled?.Invoke() ?? false,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = boxFg
            };
            promptOnCloseBox.ToolTip = "The tray icon shows recent background-import activity and quick actions like Restore and Background Imports.";
            Grid.SetRow(promptOnCloseBox, 23);
            Grid.SetColumnSpan(promptOnCloseBox, 3);
            while (panel.RowDefinitions.Count <= 23) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(promptOnCloseBox);

            var tabs = CreateSettingsTabs();
            AddConfigurationPage(tabs, panel, "Folders & tools", "Choose where captures come from, where they go, and the tools used to process them.", new[] { 0, 1, 2, 13, 3, 4 });
            AddConfigurationPage(tabs, panel, "Accounts", "Connect artwork and achievement services. Credentials are saved locally.", new[] { 5, 6, 8, 7, 9, 10, 11, 12 });
            AddConfigurationPage(tabs, panel, "Import & tray", "Control folder scanning, automatic imports, and how PixelVault runs in the background.", Enumerable.Range(14, 10).ToArray());
            root.Children.Add(tabs);
            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var save = SettingsButton("Save Settings", null, DesignTokens.ActionPrimaryFill, Brushes.White);
            save.Margin = new Thickness(0, 0, 12, 0);
            tabs.SelectionChanged += (_, e) =>
            {
                if (ReferenceEquals(e.Source, tabs)) save.Visibility = tabs.SelectedIndex < 3 ? Visibility.Visible : Visibility.Collapsed;
            };
            var cancel = SettingsButton(closeOnSave ? "Cancel" : "Close", null, DesignTokens.ActionSecondaryFill, Brushes.White);
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            save.Click += delegate
            {
                d.SetSourceRoot(d.SerializeSourceRoots(sourceBox.Text));
                d.SetDestinationRoot(destinationBox.Text);
                d.SetLibraryRoot(libraryBox.Text);
                d.SetStarredExportFolder((starredExportBox.Text ?? string.Empty).Trim());
                d.SetExifToolPath(exifBox.Text);
                d.SetFfmpegPath(ffmpegBox.Text);
                d.SetImportSearchSubfoldersForRename?.Invoke(importSubfolderScanBox.IsChecked == true);
                d.ClearFailedFfmpegPosterKeys();
                d.SetSteamGridDbApiToken((steamGridDbTokenBox.Text ?? string.Empty).Trim());
                d.SetSteamWebApiKey((steamWebApiKeyBox.Text ?? string.Empty).Trim());
                d.SetRetroAchievementsApiKey((retroAchievementsKeyBox.Text ?? string.Empty).Trim());
                d.SetIgdbTwitchClientId((igdbClientIdBox.Text ?? string.Empty).Trim());
                d.SetIgdbTwitchClientSecret((igdbClientSecretBox.Text ?? string.Empty).Trim());
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
                if (closeOnSave) window.Close();
                d.Log("Settings saved.");
                save.Content = "Saved";
            };
            root.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, __) => save.Content = "Save Settings"));
            root.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler((_, __) => save.Content = "Save Settings"));
            root.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler((_, __) => save.Content = "Save Settings"));
            cancel.Click += delegate { window.Close(); };
            return root;
        }

        UIElement BuildUi(Window window)
        {
            var cardBg = d.Brush(DesignTokens.PanelElevated);
            var cardBorder = d.Brush(DesignTokens.BorderDefault);
            var textPrimary = d.Brush(DesignTokens.TextOnInput);
            var textMuted = d.Brush(DesignTokens.TextLabelMuted);
            var root = BuildConfigurationUi(window, false);
            var tabs = root.Children.OfType<TabControl>().Single();
            root.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });
            foreach (UIElement child in root.Children) Grid.SetRow(child, Grid.GetRow(child) + 1);
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            heading.Children.Add(new TextBlock { Text = "Settings", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = textPrimary });
            heading.Children.Add(new TextBlock { Text = "Make PixelVault work your way.", Foreground = textMuted, Margin = new Thickness(0, 6, 0, 0) });
            root.Children.Add(heading);
            AddLazyPage(tabs, "Library", () =>
            {
                var stack = new StackPanel();
                stack.Children.Add(BuildLibraryToolsCard(cardBg, cardBorder, textPrimary, textMuted));
                var export = SettingsButton("Export Starred", null, DesignTokens.ActionSecondaryFill, Brushes.White);
                export.HorizontalAlignment = HorizontalAlignment.Left;
                export.IsEnabled = d.ExportStarredLibraryCaptures != null;
                export.Click += delegate { d.ExportStarredLibraryCaptures?.Invoke(window); };
                export.ToolTip = "Copy starred captures to the saved starred export folder, preserving library subfolders.";
                stack.Children.Add(export);
                stack.Children.Add(BuildLibraryBehaviorSummary(cardBg, cardBorder, textPrimary, textMuted));
                stack.Children.Add(new TextBlock { Text = "Library preferences save immediately.", Foreground = textMuted, Margin = new Thickness(0, 12, 0, 0) });
                return ScrollPage(stack);
            });
            AddLazyPage(tabs, "Diagnostics", () =>
            {
                var stack = new StackPanel();
                var health = SettingsButton("Setup & health", delegate { HealthDashboardWindow.ShowDialog(window, d); }, DesignTokens.ActionSecondaryFill, Brushes.White);
                health.HorizontalAlignment = HorizontalAlignment.Left;
                stack.Children.Add(health);
                stack.Children.Add(BuildDiagnosticsSummary(cardBg, cardBorder, textPrimary, textMuted, textMuted));
                stack.Children.Add(new TextBlock { Text = "Diagnostics preferences save immediately.", Foreground = textMuted, Margin = new Thickness(0, 12, 0, 0) });
                return ScrollPage(stack);
            });
            AddLazyPage(tabs, "Run history", () => BuildHistoryPage(window));
            AddLazyPage(tabs, "Achievements", BuildAchievementsSettings);
            return root;
        }

        TabControl CreateSettingsTabs()
        {
            var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), TabStripPlacement = Dock.Top };
            var style = new Style(typeof(TabItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, d.Brush(DesignTokens.TextLabelMuted)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 11, 16, 11)));
            var template = new ControlTemplate(typeof(TabItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "TabSurface";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8, 8, 0, 0));
            border.SetValue(Border.MarginProperty, new Thickness(0, 0, 4, 0));
            border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            border.AppendChild(content);
            template.VisualTree = border;
            var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, d.Brush(DesignTokens.PanelElevated), "TabSurface"));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, d.Brush(DesignTokens.TextOnInput)));
            selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            template.Triggers.Add(selected);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, d.Brush("#263B49"), "TabSurface"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, d.Brush("#77BDE8"), "TabSurface"));
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2), "TabSurface"));
            template.Triggers.Add(focus);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            tabs.ItemContainerStyle = style;
            return tabs;
        }

        void AddConfigurationPage(TabControl tabs, Grid source, string title, string description, int[] rows)
        {
            var page = new Grid();
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foreach (var row in rows)
            {
                var targetRow = page.RowDefinitions.Count;
                page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                foreach (var child in source.Children.Cast<UIElement>().Where(c => Grid.GetRow(c) == row).ToArray())
                {
                    source.Children.Remove(child);
                    Grid.SetRow(child, targetRow);
                    if (child is TextBlock label) label.TextWrapping = TextWrapping.Wrap;
                    if (child is CheckBox check && check.Content is string text)
                        check.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
                    page.Children.Add(child);
                }
            }
            var stack = new StackPanel();
            stack.Children.Add(TitleBlock(title, d.Brush(DesignTokens.TextOnInput)));
            stack.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = d.Brush(DesignTokens.TextLabelMuted), Margin = new Thickness(0, 0, 0, 16) });
            stack.Children.Add(page);
            tabs.Items.Add(new TabItem { Header = title, Content = ScrollPage(stack) });
        }

        UIElement ScrollPage(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new Border
                {
                    Background = d.Brush(DesignTokens.PanelElevated), BorderBrush = d.Brush(DesignTokens.BorderDefault),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Child = content
                },
                Margin = new Thickness(0, 12, 0, 0)
            };
        }

        static void AddLazyPage(TabControl tabs, string title, Func<UIElement> build)
        {
            var tab = new TabItem { Header = title };
            tabs.SelectionChanged += (_, e) =>
            {
                if (ReferenceEquals(e.Source, tabs) && ReferenceEquals(tabs.SelectedItem, tab) && tab.Content == null)
                    tab.Content = build();
            };
            tabs.Items.Add(tab);
        }

        UIElement BuildHistoryPage(Window window)
        {
            var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var actions = new WrapPanel();
            var refresh = SettingsButton("Refresh history", null, DesignTokens.ActionSecondaryFill, Brushes.White);
            var open = SettingsButton("Open logs folder", delegate { d.OpenFolder(d.LogsRoot); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            open.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(refresh);
            actions.Children.Add(open);
            grid.Children.Add(actions);
            var state = new TextBlock { Text = "Recent history loads when this tab opens. Use Refresh history for new entries.", TextWrapping = TextWrapping.Wrap, Foreground = d.Brush(DesignTokens.TextLabelMuted), Margin = new Thickness(0, 12, 0, 12) };
            Grid.SetRow(state, 1);
            grid.Children.Add(state);
            var history = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = d.Brush(DesignTokens.InputBackground), Foreground = d.Brush(DesignTokens.TextOnInput),
                BorderBrush = d.Brush(DesignTokens.BorderDefault), Padding = new Thickness(12), FontFamily = new FontFamily("Consolas"), FontSize = 12
            };
            System.Windows.Automation.AutomationProperties.SetName(history, "Recent run history");
            Grid.SetRow(history, 2);
            grid.Children.Add(history);
            var closed = false;
            window.Closed += delegate { closed = true; };
            async Task Reload()
            {
                if (!refresh.IsEnabled || closed) return;
                refresh.IsEnabled = false;
                state.Text = "Loading recent history…";
                try
                {
                    var path = d.LogFilePath();
                    var text = await Task.Run(() => TroubleshootingLog.ReadRecentHistory(path));
                    if (closed) return;
                    history.Text = text;
                    history.ScrollToEnd();
                    state.Text = text.Length == 0 ? "No run history yet." : "Recent history · up to 64 KB. Refresh for new entries, or open the logs folder for the complete file.";
                }
                catch (Exception ex)
                {
                    if (!closed) state.Text = "Could not load history: " + ex.Message;
                }
                finally { if (!closed) refresh.IsEnabled = true; }
            }
            refresh.Click += async delegate { await Reload(); };
            grid.Loaded += async delegate { await Reload(); };
            return grid;
        }

        static double PreferredSettingsWindowHeight()
        {
            var available = Math.Max(600, SystemParameters.WorkArea.Height - 32);
            return Math.Min(available, 860);
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
            var gameIndexBtn = SettingsButton("Game Index", delegate { d.OpenGameIndexEditor(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleToolBtn(gameIndexBtn);
            var photoIndexBtn = SettingsButton("Photo Index", delegate { d.OpenPhotoIndexEditor(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleToolBtn(photoIndexBtn);
            var mergeBtn = SettingsButton("Merge folders", delegate { d.OpenLibraryStorageMergeTool?.Invoke(d.OwnerWindow); }, DesignTokens.ActionShortcutDismissFill, Brushes.White);
            mergeBtn.ToolTip = "Preview and apply moving captures into shared storage folders (same storage group).";
            styleToolBtn(mergeBtn);
            var myCoversBtn = SettingsButton("My Covers", delegate { d.OpenSavedCoversFolder(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            styleToolBtn(myCoversBtn);
            var photoGalleryBtn = SettingsButton("Photography", delegate { d.ShowPhotographyGallery(d.OwnerWindow); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            photoGalleryBtn.ToolTip = "Browse captures tagged for game photography";
            styleToolBtn(photoGalleryBtn);
            var renameBtn = SettingsButton("Renaming rules", delegate { d.OpenFilenameConventionEditor(); }, DesignTokens.ActionSecondaryFill, Brushes.White);
            renameBtn.ToolTip = "How capture filenames are parsed for imports and grouping";
            styleToolBtn(renameBtn);
            var fetchCoversBtn = SettingsButton(
                "Fetch covers",
                delegate { d.PromptFetchCoversForLibrary?.Invoke(d.OwnerWindow); },
                DesignTokens.ActionShortcutDismissFill,
                Brushes.White);
            fetchCoversBtn.ToolTip = "Resolve IDs and download cover art for the whole library (occasional maintenance)";
            styleToolBtn(fetchCoversBtn);
            var changelogBtn = SettingsButton("Changelog", delegate { ChangelogWindow.ShowDialog(d.OwnerWindow, d.AppVersion, d.ChangelogPath); }, DesignTokens.ActionSecondaryFill, Brushes.White);
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
                "Removes auto-downloaded wide banner and logo files under My Covers (hero-* / logo-*). Custom banners or logos you set are not deleted. "
                + "After the library reloads, open captures for a game or use Fetch Banner Art to pull SteamGridDB Heroes / logos and Steam hero art again.";
            refreshHeroBannerCacheBox.Checked += delegate
            {
                if (d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen != null) d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen(true);
                d.SaveSettings();
                d.Log("Library: scheduled cached hero banner/logo clear on next folder load.");
            };
            refreshHeroBannerCacheBox.Unchecked += delegate
            {
                if (d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen != null) d.SetLibraryRefreshHeroBannerCacheOnNextLibraryOpen(false);
                d.SaveSettings();
                d.Log("Library: cancelled scheduled hero banner/logo cache clear.");
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
            stack.Children.Add(new TextBlock { Text = "Optional extra logging for library and UI timing. Run history is available in its own tab.", Foreground = textMuted, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
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

        Button SettingsButton(string title, RoutedEventHandler click, string background, Brush foreground)
        {
            var button = d.Btn(title, click, background ?? DesignTokens.ActionSecondaryFill, background == null ? Brushes.White : foreground);
            button.Width = double.NaN;
            button.MinWidth = 88;
            button.Height = 36;
            button.Padding = new Thickness(14, 6, 14, 6);
            button.Margin = new Thickness(0);
            button.Effect = null;
            button.VerticalAlignment = VerticalAlignment.Center;
            return button;
        }

        void SettingsBrowseButton(Grid panel, int row, RoutedEventHandler click, string label = "Browse")
        {
            var button = SettingsButton(label, click, null, Brushes.Black);
            button.Margin = new Thickness(0, row == 0 ? 0 : 12, 0, 0);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, 2);
            panel.Children.Add(button);
        }
    }
}
