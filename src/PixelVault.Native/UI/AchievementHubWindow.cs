using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    /// <summary>PV-PLN-ACHG-003: recent unlocks, recent library games, and the library-wide achievement collection.</summary>
    sealed partial class AchievementHubWindow : Window
    {
        readonly AchievementHubService service;
        readonly Func<AchievementHubSession> getSession;
        readonly Func<AchievementHubGame, double, double, FrameworkElement> cover;
        readonly Action<Window, AchievementHubGame> open;
        readonly AchievementHubPins pins;
        readonly IAchievementGuideService guides;
        readonly AchievementTrackingStore trackingStore;
        readonly StackPanel pinned = new StackPanel();
        readonly StackPanel recent = new StackPanel();
        readonly StackPanel unlocks = new StackPanel();
        AchievementHubTimeline lastTimeline;
        readonly Button refresh = new Button { Content = "Refresh", Padding = new Thickness(16, 10, 16, 10), VerticalAlignment = VerticalAlignment.Center };
        CancellationTokenSource refreshCancellation;
        string scope;
        bool closed, loading;

        internal static void ShowFor(Window owner, AchievementHubService service, Func<AchievementHubSession> getSession,
            Func<AchievementHubGame, double, double, FrameworkElement> cover, Action<Window, AchievementHubGame> open, AchievementHubPins pins,
            IAchievementGuideService guides, AchievementTrackingStore trackingStore = null)
        {
            if (owner == null) return;
            var existing = owner.OwnedWindows.OfType<AchievementHubWindow>().FirstOrDefault();
            if (existing != null)
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            new AchievementHubWindow(owner, service, getSession, cover, open, pins, guides, trackingStore).Show();
        }

        internal AchievementHubWindow(Window owner, AchievementHubService service = null, Func<AchievementHubSession> getSession = null,
            Func<AchievementHubGame, double, double, FrameworkElement> cover = null, Action<Window, AchievementHubGame> open = null,
            AchievementHubPins pins = null, IAchievementGuideService guides = null, AchievementTrackingStore trackingStore = null)
        {
            this.service = service; this.getSession = getSession; this.cover = cover; this.open = open; this.pins = pins; this.guides = guides;
            this.trackingStore = trackingStore;
            Owner = owner;
            Title = "Achievement Hub";
            Width = 1040;
            Height = 780;
            MinWidth = 680;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Color("#11181D", SystemColors.WindowBrush);
            Foreground = Color("#EFF4F7", SystemColors.WindowTextBrush);

            var root = new DockPanel { LastChildFill = true, Background = Background };
            Content = root;
            var header = new Grid { Margin = new Thickness(24, 22, 24, 20) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            titles.Children.Add(Text("Achievement Hub", 28, true));
            var subtitle = Text("Your library’s achievements, together.", 14);
            subtitle.Margin = new Thickness(0, 6, 0, 0);
            titles.Children.Add(subtitle);
            header.Children.Add(titles);
            var back = new Button
            {
                Content = "Back to Library",
                Padding = new Thickness(16, 10, 16, 10),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Color("#243640", SystemColors.ControlBrush),
                Foreground = Color("#EFF4F7", SystemColors.ControlTextBrush),
                BorderBrush = Color("#415561", SystemColors.ActiveBorderBrush),
                ToolTip = "Close the hub and return to your library (Esc)"
            };
            AutomationProperties.SetName(back, "Back to Library");
            back.Click += delegate { Close(); };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            refresh.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(refresh);
            if (trackingStore != null)
            {
                var trackers = AchievementTrackerWindow.Button("Trackers", "Open achievement trackers across your library");
                trackers.Margin = new Thickness(0, 0, 10, 0);
                trackers.Click += (_, __) => OpenTracking();
                actions.Children.Add(trackers);
            }
            actions.Children.Add(back);
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var sections = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
            sections.Children.Add(BuildScoreSection());
            sections.Children.Add(new Border
            {
                Background = Color("#18232B", SystemColors.WindowBrush),
                BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 18, 22, 18),
                Margin = new Thickness(0, 0, 0, 16),
                Child = unlocks
            });
            sections.Children.Add(pinned);
            sections.Children.Add(recent);
            sections.Children.Add(BuildCollectionSection());
            RenderRecent(new AchievementHubState { Status = "Loading recent play…" });
            root.Children.Add(new ScrollViewer
            {
                Content = sections,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.VerticalOnly
            });
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
            };
            refresh.Background = back.Background;
            refresh.Foreground = back.Foreground;
            refresh.BorderBrush = back.BorderBrush;
            refresh.ToolTip = "Refresh recent play and achievement progress";
            AutomationProperties.SetName(refresh, "Refresh achievements");
            refresh.Click += async (_, __) => await RefreshAsync(true);
            Loaded += async delegate { back.Focus(); await RefreshAsync(false); };
            Activated += async delegate
            {
                if (IsLoaded && getSession != null && scope != getSession().Scope) await RefreshAsync(false);
            };
            Closed += delegate { closed = true; refreshCancellation?.Cancel(); renderThrottle?.Stop(); };
        }

        internal async Task RefreshAsync(bool force, bool restart = false)
        {
            if (closed || getSession == null || service == null) return;
            var session = getSession();
            if (pins != null) session.Pinned = pins.Keys.ToList();
            if (loading && scope == session.Scope && !restart) return;
            refreshCancellation?.Cancel();
            pendingRender = null;
            var cts = new CancellationTokenSource();
            refreshCancellation = cts;
            if (scope != session.Scope)
            {
                lastTimeline = null; ResetCollection();
                RenderRecent(new AchievementHubState { Status = "Loading recent play…" });
            }
            scope = session.Scope; loading = true; refresh.IsEnabled = false;
            try
            {
                await service.LoadAsync(session, force, state =>
                {
                    if (!closed && !cts.IsCancellationRequested && scope == session.Scope) QueueRender(state);
                }, cts.Token);
                if (!closed && !cts.IsCancellationRequested) FlushRender();
            }
            catch (OperationCanceledException) { }
            catch { if (!closed && !cts.IsCancellationRequested) RenderRecent(new AchievementHubState { Status = "Recent games could not be loaded. Try Refresh." }); }
            finally
            {
                if (ReferenceEquals(refreshCancellation, cts)) { loading = false; refresh.IsEnabled = true; }
                cts.Dispose();
                if (ReferenceEquals(refreshCancellation, cts)) refreshCancellation = null;
            }
        }

        // Loading reports after every fetched game; a full render costs hundreds of milliseconds, so coalesce them.
        static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(350);
        AchievementHubState pendingRender;
        DispatcherTimer renderThrottle;

        void QueueRender(AchievementHubState state)
        {
            if (renderThrottle?.IsEnabled == true) { pendingRender = state; return; }
            RenderRecent(state);
            renderThrottle ??= new DispatcherTimer(RenderInterval, DispatcherPriority.Background, (_, __) => FlushRender(), Dispatcher);
            renderThrottle.Start();
        }

        void FlushRender()
        {
            renderThrottle?.Stop();
            var state = pendingRender;
            pendingRender = null;
            if (state == null || closed) return;
            RenderRecent(state);
            renderThrottle.Start();
        }

        internal void RenderRecent(AchievementHubState state)
        {
            if (state.Collection != null) RenderScore(state.Collection);
            if (state.Timeline != null) lastTimeline = state.Timeline;
            RenderUnlocks(state.Timeline ?? lastTimeline, state.Timeline == null && lastTimeline != null);
            RenderCollection(state.Collection);
            pinned.Visibility = pins == null ? Visibility.Collapsed : Visibility.Visible;
            if (pins != null)
                RenderShelf(pinned, "Pinned games", null, state.Pinned, true,
                    "Pin games with the pin button on a game card, or right-click an achievement and choose Pin game.");
            RenderShelf(recent, "Recently played", state.Status, state.Cards, false,
                "No recent library games with available play dates to show yet.");
        }

        /// <summary>Shows the detail window modally; replaceable so tests can inspect it without blocking.</summary>
        internal Func<AchievementDetailWindow, bool?> ShowAchievementDetail = window => window.ShowDialog();

        async Task OpenAchievementAsync(AchievementHubGame game, GameAchievementsFetchService.AchievementRow row)
        {
            if (getSession != null && getSession().Scope != scope) { await RefreshAsync(false); return; }
            var rows = collection?.Entries.Where(e => e.Game.Key == game.Key).Select(e => e.Row).ToList();
            long saved = 0;
            collection?.SnapshotUpdated.TryGetValue(game.Key, out saved);
            var detail = new AchievementDetailWindow(this, game, row, rows, saved, guides,
                pins == null ? null : () => pins.IsPinned(game.Key), pins == null ? null : () => TogglePin(game),
                trackingStore, getSession?.Invoke().Account(game.Provider));
            ShowAchievementDetail(detail);
            if (guides != null && collection != null)
            {
                try
                {
                    await Task.Run(() => guides.LoadSavedAchievementGroups(collection.Entries.Select(e => e.Row)));
                    ApplyCollectionQuery();
                }
                catch { /* Guide edits must not prevent returning to the collection. */ }
            }
            if (detail.OpenGameRequested) await OpenGameAsync(game);
        }

        async Task OpenGameAsync(AchievementHubGame game)
        {
            if (getSession != null && getSession().Scope != scope) { await RefreshAsync(false); return; }
            open?.Invoke(this, game);
            await RefreshAsync(false);
        }

        string unlocksSignature;
        void RenderUnlocks(AchievementHubTimeline timeline, bool refreshFailed)
        {
            var signature = timeline == null ? "none" : string.Join("\u001e", refreshFailed, timeline.LibraryGames, timeline.CoveredGames,
                timeline.UndatedEarned, timeline.OldestSnapshotUtc, string.Join("\u001f", timeline.Unlocks.Select(u => u.Game?.Key + "|" + u.Game?.Title
                    + "|" + u.Row?.ProviderAchievementId + "|" + u.Row?.Title + "|" + u.Row?.Description + "|" + u.Row?.UnlockUtcTicks
                    + "|" + u.Row?.GlobalUnlockPercent + "|" + u.Row?.IconUrlColor)));
            if (signature == unlocksSignature && unlocks.Children.Count > 0) return;
            unlocksSignature = signature;
            unlocks.Children.Clear();
            unlocks.Children.Add(Text("Recent unlocks", 18, true));
            var notice = timeline == null ? "Loading saved achievement progress…"
                : timeline.LibraryGames == 0 ? "Connect a Steam or RetroAchievements account to see unlocks."
                : refreshFailed ? "Refresh failed; showing previously loaded unlocks." : null;
            if (notice != null)
            {
                var caption = Text(notice, 12);
                caption.Margin = new Thickness(0, 4, 0, 12);
                unlocks.Children.Add(caption);
            }
            if (timeline == null) return;
            if (timeline.Unlocks.Count == 0)
            {
                unlocks.Children.Add(Text(timeline.CoveredGames == 0
                    ? "No saved achievement progress yet. Background achievement checks add games over time, or use Refresh."
                    : "No dated unlocks in saved progress yet.", 14));
                return;
            }
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            var scroller = new ScrollViewer
            {
                Content = strip,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                PanningMode = PanningMode.HorizontalOnly,
                Focusable = false,
                Margin = new Thickness(0, 8, 0, 0)
            };
            AutomationProperties.SetName(scroller, "Recent unlocks, newest first");
            // The inner horizontal scroller would otherwise swallow wheel input meant for the page.
            scroller.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); return; }
                unlocks.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = scroller });
            };
            unlocks.Children.Add(scroller);
            foreach (var unlock in timeline.Unlocks)
            {
                var row = unlock.Row;
                var date = new DateTime(row.UnlockUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                var title = Text(string.IsNullOrWhiteSpace(row.Title) ? "Achievement" : row.Title, 13, true);
                title.MaxHeight = 36; title.TextTrimming = TextTrimming.CharacterEllipsis;
                var game = Text(unlock.Game.Title, 11); game.TextWrapping = TextWrapping.NoWrap; game.TextTrimming = TextTrimming.CharacterEllipsis;
                game.Margin = new Thickness(0, 3, 0, 0);
                var when = Text(date, 11); when.Margin = new Thickness(0, 2, 0, 0);
                var image = new Image { Width = 44, Height = 44, Stretch = Stretch.UniformToFill };
                var icon = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(6), Child = image,
                    Background = Color("#11181D", SystemColors.WindowBrush), BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush),
                    BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0) };
                AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "PixelVault/AchievementHub");
                var text = new StackPanel();
                text.Children.Add(title); text.Children.Add(game); text.Children.Add(when);
                var layout = new DockPanel();
                DockPanel.SetDock(icon, Dock.Left);
                layout.Children.Add(icon); layout.Children.Add(text);
                var tile = new Border { Width = 236, Height = 86, Padding = new Thickness(10), CornerRadius = new CornerRadius(8), Child = layout,
                    Background = Color("#1D2B34", SystemColors.WindowBrush), BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush),
                    BorderThickness = new Thickness(1) };
                AchievementRarityUi.Apply(tile, icon, title, row);
                var rarity = AchievementRarityUi.Label(row);
                var button = new Button { Content = tile, Margin = new Thickness(0, 0, 10, 8), Padding = new Thickness(0),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                    ToolTip = ((row.Description ?? "").Trim() is var d && d.Length > 0 ? d + "\n" : "") + unlock.Game.Title + " · view achievement details" };
                if (!SystemParameters.HighContrast)
                    button.Style = LibraryButtonChrome.LibraryToolbarButtonStyle("#00000000", "#00000000", "#304956", "#18242B");
                AutomationProperties.SetName(button, title.Text + ", " + unlock.Game.Title + ", earned " + date
                    + (rarity.Length > 0 ? ", " + rarity : "") + ". Open achievement details.");
                AccessibilityUi.TryApplyFocusVisualStyle(button);
                var target = unlock.Game;
                button.Click += async (_, __) => await OpenAchievementAsync(target, row);
                AttachPinMenu(button, target);
                strip.Children.Add(button);
            }
        }

        static TextBlock Text(string value, double size, bool prominent = false) => new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = prominent ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Color(prominent ? "#EFF4F7" : "#A4B7C3", SystemColors.WindowTextBrush),
            TextWrapping = TextWrapping.Wrap
        };

        static Brush Color(string hex, Brush highContrast) =>
            SystemParameters.HighContrast ? highContrast : UiBrushHelper.FromHex(hex);
    }
}
