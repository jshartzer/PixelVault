using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        AchievementNotifications _achievementNotifications;
        readonly CancellationTokenSource _achievementMonitorCancellation = new();
        readonly Queue<AchievementNotifications.Notice> _achievementToastQueue = new();
        readonly List<(Window Window, DateTime Expires)> _achievementToasts = new();
        readonly Dictionary<string, DateTime> _achievementLastCheck = new();
        DispatcherTimer _achievementNotificationTimer;
        IDisposable _achievementFullscreenWatcher;
        DateTime _nextAchievementPoll = DateTime.UtcNow.AddSeconds(10);
        bool _achievementPolling;
        Window _achievementHistoryWindow;
        Action _reloadAchievementHistory;
        Button _achievementNotificationsButton;
        string _achievementMonitorStatus = "Waiting for startup check…";
        event Action<LibraryFolderInfo> AchievementCompletionUpdated;

        void InitializeAchievementNotifications()
        {
            try { _achievementNotifications = new AchievementNotifications(Path.Combine(dataRoot, "achievement-notifications.json")); }
            catch (Exception ex) { Log("Could not load achievement notifications: " + ex.Message); return; }
            GameAchievementsFetchService.ProgressObserved += AchievementProgressObserved;
            _achievementFullscreenWatcher = AchievementFullscreenGuard.Watch(() =>
            {
                if (_achievementToasts.Count > 0 && AchievementFullscreenGuard.Suppress()) CloseAchievementToasts();
            });
            _achievementNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _achievementNotificationTimer.Tick += (_, __) =>
            {
                PumpAchievementToasts();
                if (!_achievementPolling && DateTime.UtcNow >= _nextAchievementPoll && _achievementNotifications.Data.Enabled)
                    _ = PollAchievementNotificationsAsync();
            };
            _achievementNotificationTimer.Start();
            Closed += (_, __) =>
            {
                GameAchievementsFetchService.ProgressObserved -= AchievementProgressObserved;
                _achievementMonitorCancellation.Cancel();
                _achievementNotificationTimer.Stop();
                _achievementFullscreenWatcher?.Dispose();
                CloseAchievementToasts();
                _achievementHistoryWindow?.Close();
            };
            UpdateAchievementNotificationBadge();
        }

        void SaveAchievementNotifications()
        {
            try { _achievementNotifications.Save(); }
            catch (Exception ex) { _achievementMonitorStatus = "Could not save notification history: " + ex.Message; Log(_achievementMonitorStatus); }
        }

        void AchievementProgressObserved(GameAchievementsFetchService.FetchResult result, string account)
        {
            // All mutation and persistence is serialized on the UI dispatcher.
            if (_achievementMonitorCancellation.IsCancellationRequested) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_achievementMonitorCancellation.IsCancellationRequested) return;
                _ = SyncAchievementCompletionAsync(result, account, libraryRoot);
                var notices = _achievementNotifications.Observe(result, account);
                SaveAchievementNotifications();
                if (!AchievementFullscreenGuard.Suppress())
                    foreach (var notice in notices) _achievementToastQueue.Enqueue(notice);
                UpdateAchievementNotificationBadge();
                _reloadAchievementHistory?.Invoke();
            }));
        }

        async Task SyncAchievementCompletionAsync(GameAchievementsFetchService.FetchResult result, string account, string root)
        {
            if (result == null || result.IsError || !string.IsNullOrEmpty(result.ProgressErrorMessage)
                || result.Rows == null || result.Rows.Count == 0 || string.IsNullOrWhiteSpace(account)) return;
            // Provider results are also consumed by open achievement windows; group an independent copy.
            var copy = new GameAchievementsFetchService.FetchResult { Rows = result.Rows.Select(r => r == null ? null :
                new GameAchievementsFetchService.AchievementRow { Provider = r.Provider, ProviderGameId = r.ProviderGameId,
                    ProviderAchievementId = r.ProviderAchievementId, ProgressKnown = r.ProgressKnown,
                    Unlocked = r.Unlocked, UnlockUtcTicks = r.UnlockUtcTicks }).ToList() };
            try
            {
                if (achievementGuideService == null) return;
                await Task.Run(() => achievementGuideService.LoadSavedAchievementGroups(copy.Rows));
                if (_achievementMonitorCancellation.IsCancellationRequested || libraryRoot != root || librarySession?.HasLibraryRoot != true) return;
                var provider = copy.Rows.FirstOrDefault()?.Provider;
                var currentAccount = provider == GameAchievementsFetchService.SteamProvider ? CurrentSteamUserId64() : CurrentRetroAchievementsUsername();
                if (!string.Equals(account.Trim(), currentAccount?.Trim(), StringComparison.OrdinalIgnoreCase)) return;
                var rows = librarySession.LoadSavedGameIndexRows();
                var changed = MarkCompletedAchievementGames(copy, rows);
                if (changed.Count == 0) return;
                librarySession.PersistGameIndexRows(rows);
                var ws = _libraryBrowserLiveWorkingSet;
                foreach (var row in changed)
                {
                    var completed = BuildLibraryFolderInfoFromGameIndexRow(row);
                    void Update(LibraryFolderInfo folder)
                    {
                        if (folder != null && folder.GameId == row.GameId)
                        { folder.IsCompleted100Percent = true; folder.CompletedUtcTicks = row.CompletedUtcTicks; }
                    }
                    if (ws != null)
                    {
                        foreach (var folder in ws.Folders) Update(folder);
                        foreach (var view in ws.ViewFolders.Concat(new[] { ws.Current }).Where(v => v != null).Distinct())
                        {
                            Update(view.PrimaryFolder);
                            foreach (var folder in view.SourceFolders) Update(folder);
                            if (view.GameId == row.GameId || view.SourceFolders.Any(f => f?.GameId == row.GameId))
                            { view.IsCompleted100Percent = true; view.CompletedUtcTicks = row.CompletedUtcTicks; }
                        }
                    }
                    AchievementCompletionUpdated?.Invoke(completed);
                }
                ws?.RerenderFolderList?.Invoke();
            }
            catch (Exception ex) { LogException("AchievementCompletion.Refresh", ex); }
        }

        internal static List<GameIndexEditorRow> MarkCompletedAchievementGames(
            GameAchievementsFetchService.FetchResult result, IEnumerable<GameIndexEditorRow> games)
        {
            var changed = new List<GameIndexEditorRow>();
            if (result == null || result.IsError || !string.IsNullOrEmpty(result.ProgressErrorMessage)
                || result.Rows == null || result.Rows.Any(r => r == null || !r.HasStableProviderIdentity)
                || !LibraryGameProfileAchievementsAreFullyUnlocked(result.Rows)) return changed;
            var identity = result.Rows[0];
            if (result.Rows.Any(r => r.Provider != identity.Provider || r.ProviderGameId != identity.ProviderGameId)) return changed;
            foreach (var row in games ?? Enumerable.Empty<GameIndexEditorRow>())
            {
                if (row == null || row.IsCompleted100Percent) continue;
                var folder = BuildLibraryFolderInfoFromGameIndexRow(row);
                if (!GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(row.PlatformLabel), folder, out var steam)) continue;
                if ((steam ? GameAchievementsFetchService.SteamProvider : GameAchievementsFetchService.RetroAchievementsProvider) != identity.Provider) continue;
                if (!string.Equals((steam ? row.SteamAppId : row.RetroAchievementsGameId)?.Trim(), identity.ProviderGameId.Trim(), StringComparison.Ordinal)) continue;
                row.IsCompleted100Percent = true;
                var earnedTicks = result.Rows.Where(r => string.Equals(AchievementsInfoWindow.EffectiveAchievementGroupName(r), "Base Game", StringComparison.OrdinalIgnoreCase))
                    .Max(r => r.UnlockUtcTicks);
                row.CompletedUtcTicks = earnedTicks > 0 && earnedTicks <= DateTime.UtcNow.Ticks ? earnedTicks : DateTime.UtcNow.Ticks;
                changed.Add(row);
            }
            return changed;
        }

        async Task PollAchievementNotificationsAsync()
        {
            if (_achievementPolling) return;
            _achievementPolling = true;
            var token = _achievementMonitorCancellation.Token;
            try
            {
                _achievementMonitorStatus = "Checking achievement progress…";
                _reloadAchievementHistory?.Invoke();
                var root = libraryRoot;
                var selected = _libraryBrowserLiveWorkingSet?.Current == null ? null
                    : ResolveLibraryBrowserAchievementLookupFolder(_libraryBrowserLiveWorkingSet.Current);
                var recent = (_libraryBrowserLiveWorkingSet?.Folders ?? new List<LibraryFolderInfo>())
                    .Where(f => !string.IsNullOrEmpty(f.GameId)).GroupBy(f => f.GameId)
                    .ToDictionary(g => g.Key, g => g.Max(f => f.NewestCaptureUtcTicks));
                var games = (GetSavedGameIndexRowsForRoot(root) ?? new List<GameIndexEditorRow>())
                    .Where(g => !string.IsNullOrWhiteSpace(g.SteamAppId) || !string.IsNullOrWhiteSpace(g.RetroAchievementsGameId))
                    .Select(g => new LibraryFolderInfo { Name = g.Name, PlatformLabel = g.PlatformLabel, SteamAppId = g.SteamAppId,
                        RetroAchievementsGameId = g.RetroAchievementsGameId, GameId = g.GameId })
                    .GroupBy(g => g.PlatformLabel + "|" + g.SteamAppId + "|" + g.RetroAchievementsGameId).Select(g => g.First())
                    .OrderByDescending(g => (!string.IsNullOrWhiteSpace(g.SteamAppId) && g.SteamAppId == selected?.SteamAppId)
                        || (!string.IsNullOrWhiteSpace(g.RetroAchievementsGameId) && g.RetroAchievementsGameId == selected?.RetroAchievementsGameId))
                    .ThenByDescending(g => g.GameId != null && recent.TryGetValue(g.GameId, out var ticks) ? ticks : 0).ToList();
                int checkedCount = 0, failures = 0;
                foreach (var game in games)
                {
                    token.ThrowIfCancellationRequested();
                    if (!_achievementNotifications.Data.Enabled || libraryRoot != root) break;
                    var platform = NormalizeConsoleLabel(game.PlatformLabel);
                    if (!GameAchievementsFetchService.TryPickAchievementsBackend(platform, game, out var steam)) continue;
                    var account = steam ? CurrentSteamUserId64() : CurrentRetroAchievementsUsername();
                    if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(steam ? CurrentSteamWebApiKey() : CurrentRetroAchievementsApiKey())) continue;
                    var key = (steam ? "steam|" + game.SteamAppId : "retro|" + game.RetroAchievementsGameId) + "|" + account;
                    if (_achievementLastCheck.TryGetValue(key, out var next) && DateTime.UtcNow < next) continue;
                    var result = await GameAchievementsFetchService.FetchAsync(platform, game, CurrentSteamWebApiKey(),
                        CurrentRetroAchievementsApiKey(), CurrentSteamUserId64(), CurrentRetroAchievementsUsername(), "PixelVault/" + AppVersion, token, notificationsOnly: true);
                    var failed = result.IsError || !string.IsNullOrEmpty(result.ProgressErrorMessage);
                    checkedCount++; if (failed) failures++;
                    _achievementLastCheck[key] = DateTime.UtcNow.AddMinutes(failed ? 30 : Math.Max(5, _achievementNotifications.Data.IntervalMinutes));
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                }
                _achievementMonitorStatus = "Last check " + DateTime.Now.ToString("t") + ": " + checkedCount + " game(s), " + failures + " unavailable.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _achievementMonitorStatus = "Achievement check failed; will retry later."; Log("Achievement monitor: " + ex.Message); }
            finally
            {
                _achievementPolling = false;
                var interval = _achievementNotifications.Data.IntervalMinutes;
                _nextAchievementPoll = interval == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(interval);
                _reloadAchievementHistory?.Invoke();
            }
        }

        Button CreateAchievementNotificationsButton()
        {
            _achievementNotificationsButton = Btn("Notifications", (_, __) => ShowAchievementNotifications(), "#20343A", Brushes.White);
            _achievementNotificationsButton.ToolTip = "Achievement notifications, history and refresh settings";
            UpdateAchievementNotificationBadge();
            return _achievementNotificationsButton;
        }
        void UpdateAchievementNotificationBadge()
        {
            if (_achievementNotificationsButton == null) return;
            var unread = _achievementNotifications?.Data.History.Count(n => !n.Read) ?? 0;
            _achievementNotificationsButton.Content = unread > 0 ? "Notifications (" + unread + ")" : "Notifications";
        }
        void CloseAchievementToasts()
        {
            _achievementToastQueue.Clear();
            foreach (var toast in _achievementToasts.ToArray()) toast.Window.Close();
            _achievementToasts.Clear();
        }
        void PumpAchievementToasts()
        {
            if (_achievementToasts.Count == 0 && _achievementToastQueue.Count == 0) return;
            if (!_achievementNotifications.Data.Enabled || AchievementFullscreenGuard.Suppress()) { CloseAchievementToasts(); return; }
            foreach (var toast in _achievementToasts.Where(t => t.Expires <= DateTime.UtcNow).ToArray())
            { toast.Window.Close(); _achievementToasts.Remove(toast); }
            while (_achievementToasts.Count < 3 && _achievementToastQueue.Count > 0)
            {
                var notice = _achievementToastQueue.Dequeue();
                var text = new TextBlock { Text = notice.Title + "\n" + notice.Message, TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(16) };
                var toast = new Window { Width = 350, Height = 112, WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                    Background = Brush("#172127"), Content = text, WindowStartupLocation = WindowStartupLocation.Manual };
                toast.MouseLeftButtonUp += (_, __) => { notice.Read = true; SaveAchievementNotifications(); CloseAchievementToasts(); OpenAchievementNotice(notice); };
                _achievementToasts.Add((toast, DateTime.UtcNow.AddSeconds(7)));
                PositionAchievementToasts();
                if (AchievementFullscreenGuard.Suppress()) { CloseAchievementToasts(); return; }
                toast.Show();
            }
            PositionAchievementToasts();
        }
        void PositionAchievementToasts()
        {
            var work = SystemParameters.WorkArea;
            for (var i = 0; i < _achievementToasts.Count; i++)
            { _achievementToasts[i].Window.Left = work.Right - 366; _achievementToasts[i].Window.Top = work.Bottom - 128 - i * 120; }
        }
        void OpenAchievementNotice(AchievementNotifications.Notice notice)
        {
            if (string.IsNullOrEmpty(notice.Game)) { ShowAchievementNotifications(); return; }
            RestoreMainWindowFromSystemTray();
            UpdateAchievementNotificationBadge();
            var steam = notice.Provider == GameAchievementsFetchService.SteamProvider;
            if (!string.Equals(notice.Account, steam ? CurrentSteamUserId64() : CurrentRetroAchievementsUsername(), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "This notification belongs to a different achievement account. Switch to that account in Settings to view its current progress.", "Achievement account");
                return;
            }
            var folder = new LibraryFolderInfo { Name = notice.Title, SteamAppId = steam ? notice.Game : null,
                RetroAchievementsGameId = steam ? null : notice.Game, PlatformLabel = steam ? "Steam" : "Emulation" };
            AchievementsInfoWindow.ShowModal(this, folder.PlatformLabel, folder, CurrentSteamWebApiKey(), CurrentRetroAchievementsApiKey(),
                CurrentSteamUserId64(), CurrentRetroAchievementsUsername(), "PixelVault/" + AppVersion, achievementGuideService);
        }

        void ShowAchievementNotifications()
        {
            if (_achievementNotifications == null) return;
            if (_achievementHistoryWindow != null) { _achievementHistoryWindow.Activate(); return; }
            var window = new Window { Title = "Achievement notifications", Width = 620, Height = 650, MinWidth = 460, MinHeight = 420,
                Background = Brush("#0F1519"), Foreground = Brushes.White, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            _achievementHistoryWindow = window;
            var root = new DockPanel { Margin = new Thickness(18) };
            var settings = new StackPanel(); DockPanel.SetDock(settings, Dock.Top); root.Children.Add(settings);
            var enabled = new CheckBox { Content = "Enable achievement notifications and checks", IsChecked = _achievementNotifications.Data.Enabled, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
            var individual = new CheckBox { Content = "Also notify for individual achievement unlocks", IsChecked = _achievementNotifications.Data.IndividualUnlocks, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
            settings.Children.Add(enabled); settings.Children.Add(individual);
            settings.Children.Add(new TextBlock { Text = "Refresh in background (startup always checks when enabled):", Margin = new Thickness(0, 0, 0, 6) });
            var interval = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            var choices = new[] { 0, 5, 15, 30, 60 };
            foreach (var minutes in choices) interval.Items.Add(minutes == 0 ? "Startup / manual only" : "Every " + minutes + " minutes");
            interval.SelectedIndex = Math.Max(0, Array.IndexOf(choices, _achievementNotifications.Data.IntervalMinutes));
            settings.Children.Add(interval);
            settings.Children.Add(new TextBlock { Text = "Milestone percentages (100% is always included):" });
            var thresholds = new TextBox { Text = string.Join(", ", _achievementNotifications.Data.Thresholds), Margin = new Thickness(0, 6, 0, 10) }; settings.Children.Add(thresholds);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
            var actions = new WrapPanel(); settings.Children.Add(actions); settings.Children.Add(status);
            actions.Children.Add(Btn("Save settings", (_, __) =>
            {
                var parts = thresholds.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Any(p => !int.TryParse(p, out var t) || t < 1 || t > 100)) { status.Text = "Enter whole percentages from 1 to 100, separated by commas."; return; }
                _achievementNotifications.Data.Enabled = enabled.IsChecked == true;
                _achievementNotifications.Data.IndividualUnlocks = individual.IsChecked == true;
                _achievementNotifications.Data.IntervalMinutes = choices[interval.SelectedIndex];
                _achievementNotifications.Data.Thresholds = parts.Select(int.Parse).Append(100).Distinct().OrderBy(t => t).ToList();
                _nextAchievementPoll = _achievementNotifications.Data.IntervalMinutes == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(_achievementNotifications.Data.IntervalMinutes);
                SaveAchievementNotifications(); status.Text = "Settings saved. Fullscreen notifications are always silenced and kept in history.";
            }, "#31505D", Brushes.White));
            var check = Btn("Check now", async (_, __) => { if (_achievementNotifications.Data.Enabled && !_achievementPolling) { _achievementLastCheck.Clear(); await PollAchievementNotificationsAsync(); } }, "#31505D", Brushes.White);
            actions.Children.Add(check);
            actions.Children.Add(Btn("Preview popups", (_, __) =>
            {
                if (!_achievementNotifications.Data.Enabled) { status.Text = "Enable notifications and save settings to preview popups."; return; }
                if (AchievementFullscreenGuard.Suppress()) { status.Text = "Popups suppressed: fullscreen or Windows quiet mode is active."; return; }
                for (var i = 1; i <= 4; i++) _achievementToastQueue.Enqueue(new AchievementNotifications.Notice
                    { Title = "PixelVault preview " + i, Message = "Achievement milestone reached! This preview is not saved." });
                status.Text = "Four preview cards queued; up to three appear at once.";
            }, "#31505D", Brushes.White));
            actions.Children.Add(Btn("Mark all read", (_, __) => { foreach (var n in _achievementNotifications.Data.History) n.Read = true; SaveAchievementNotifications(); UpdateAchievementNotificationBadge(); _reloadAchievementHistory?.Invoke(); }, "#31505D", Brushes.White));
            var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _reloadAchievementHistory = () =>
            {
                check.IsEnabled = !_achievementPolling && _achievementNotifications.Data.Enabled;
                status.Text = _achievementMonitorStatus + "\nFullscreen popups are always silenced. The first check establishes a baseline. History keeps the latest 500 entries.";
                list.Children.Clear();
                if (_achievementNotifications.Data.History.Count == 0) list.Children.Add(new TextBlock { Text = "No notifications yet.", Margin = new Thickness(0, 12, 0, 0) });
                foreach (var notice in _achievementNotifications.Data.History)
                {
                    var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brush("#172127"), Foreground = Brushes.White,
                        Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Content = new TextBlock { TextWrapping = TextWrapping.Wrap,
                            Text = (notice.Read ? "" : "● ") + notice.Title + "\n" + notice.Message
                                + (notice.CrossedThresholds?.Count > 1 ? "\nMilestones crossed: " + string.Join(", ", notice.CrossedThresholds.Select(t => t + "%")) : "")
                                + "\n" + notice.Utc.ToLocalTime().ToString("g") } };
                    button.Click += (_, __) => { notice.Read = true; SaveAchievementNotifications(); UpdateAchievementNotificationBadge(); _reloadAchievementHistory?.Invoke(); OpenAchievementNotice(notice); };
                    list.Children.Add(button);
                }
            };
            window.Content = root;
            window.Closed += (_, __) => { _achievementHistoryWindow = null; _reloadAchievementHistory = null; };
            _reloadAchievementHistory(); window.Show();
        }
    }
}
