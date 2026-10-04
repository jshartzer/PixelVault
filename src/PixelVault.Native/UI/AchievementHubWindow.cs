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

namespace PixelVaultNative
{
    /// <summary>PV-PLN-ACHG-003: recent unlocks, recent library games, and the library-wide achievement collection.</summary>
    sealed partial class AchievementHubWindow : Window
    {
        readonly AchievementHubService service;
        readonly Func<AchievementHubSession> getSession;
        readonly Func<AchievementHubGame, FrameworkElement> cover;
        readonly Action<Window, AchievementHubGame> open;
        readonly StackPanel recent = new StackPanel();
        readonly StackPanel unlocks = new StackPanel();
        AchievementHubTimeline lastTimeline;
        readonly Button refresh = new Button { Content = "Refresh", Padding = new Thickness(16, 10, 16, 10), VerticalAlignment = VerticalAlignment.Center };
        CancellationTokenSource refreshCancellation;
        string scope;
        bool closed, loading;

        internal static void ShowFor(Window owner, AchievementHubService service, Func<AchievementHubSession> getSession,
            Func<AchievementHubGame, FrameworkElement> cover, Action<Window, AchievementHubGame> open)
        {
            if (owner == null) return;
            var existing = owner.OwnedWindows.OfType<AchievementHubWindow>().FirstOrDefault();
            if (existing != null)
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            new AchievementHubWindow(owner, service, getSession, cover, open).Show();
        }

        internal AchievementHubWindow(Window owner, AchievementHubService service = null, Func<AchievementHubSession> getSession = null,
            Func<AchievementHubGame, FrameworkElement> cover = null, Action<Window, AchievementHubGame> open = null)
        {
            this.service = service; this.getSession = getSession; this.cover = cover; this.open = open;
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
            actions.Children.Add(back);
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var sections = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
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
            Closed += delegate { closed = true; refreshCancellation?.Cancel(); };
        }

        internal async Task RefreshAsync(bool force)
        {
            if (closed || getSession == null || service == null) return;
            var session = getSession();
            if (loading && scope == session.Scope) return;
            refreshCancellation?.Cancel();
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
                    if (!closed && !cts.IsCancellationRequested && scope == session.Scope && getSession().Scope == session.Scope)
                        RenderRecent(state);
                }, cts.Token);
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

        internal void RenderRecent(AchievementHubState state)
        {
            if (state.Timeline != null) lastTimeline = state.Timeline;
            RenderUnlocks(state.Timeline ?? lastTimeline, state.Timeline == null && lastTimeline != null);
            RenderCollection(state.Collection);
            recent.Children.Clear();
            recent.Children.Add(Text("Recently played", 18, true));
            var status = Text(state.Status, 12); status.Margin = new Thickness(0, 6, 0, 10);
            recent.Children.Add(status);
            var cards = new WrapPanel { Margin = new Thickness(0, 12, 0, 18) };
            recent.Children.Add(cards);
            if (state.Cards.Count == 0)
                cards.Children.Add(Text("No recent library games with available play dates to show yet.", 14));
            foreach (var card in state.Cards)
            {
                var content = new StackPanel { Margin = new Thickness(12), Width = 250 };
                if (cover != null) content.Children.Add(cover(card.Game));
                var name = Text(card.Game.Title, 16, true); name.Margin = new Thickness(0, 10, 0, 6);
                name.MinHeight = 44; name.MaxHeight = 44; name.TextTrimming = TextTrimming.CharacterEllipsis;
                content.Children.Add(name);
                content.Children.Add(Text((card.Game.Provider == "steam" ? "Steam" : "RetroAchievements") + " · "
                    + new DateTime(card.LastPlayedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g"), 11));
                var rows = card.Snapshot?.Rows;
                var stats = rows == null ? null : AchievementCollectionStats.Build(rows, DateTime.UtcNow.Ticks);
                var progress = stats == null ? "Achievement progress unavailable"
                    : stats.Total == 0 ? "No achievements for this game"
                    : stats.Unknown > 0 ? "Achievement progress unavailable"
                    : stats.Earned + " / " + stats.Total + " earned · " + stats.Percent + "%";
                var summary = Text(progress, 13, true); summary.Margin = new Thickness(0, 10, 0, 5);
                content.Children.Add(summary);
                if (stats?.Percent != null)
                    content.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = stats.Percent.Value, Height = 6,
                        Foreground = Color("#79BBA1", SystemColors.HighlightBrush), Background = Color("#2B3C47", SystemColors.ControlBrush) });
                if (card.Snapshot != null && card.Snapshot.UpdatedUtc > 0 && card.Snapshot.UpdatedUtc <= DateTime.MaxValue.Ticks)
                    content.Children.Add(Text("Saved " + new DateTime(card.Snapshot.UpdatedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g"), 11));
                var button = new Button { Content = content, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(0),
                    Background = Color("#18232B", SystemColors.WindowBrush), Foreground = Color("#EFF4F7", SystemColors.WindowTextBrush),
                    BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush), Cursor = Cursors.Hand,
                    ToolTip = "Open " + card.Game.Title + " achievements", VerticalContentAlignment = VerticalAlignment.Top,
                    VerticalAlignment = VerticalAlignment.Top };
                if (!SystemParameters.HighContrast)
                    button.Style = LibraryButtonChrome.LibraryToolbarButtonStyle("#18232B", "#2B3C47", "#243640", "#11181D");
                AutomationProperties.SetName(button, card.Game.Title + ", " + progress + ". Open achievements.");
                AccessibilityUi.TryApplyFocusVisualStyle(button);
                button.Click += async (_, __) => await OpenGameAsync(card.Game);
                cards.Children.Add(button);
            }
        }

        async Task OpenGameAsync(AchievementHubGame game)
        {
            if (getSession != null && getSession().Scope != scope) { await RefreshAsync(false); return; }
            open?.Invoke(this, game);
            await RefreshAsync(false);
        }

        void RenderUnlocks(AchievementHubTimeline timeline, bool refreshFailed)
        {
            unlocks.Children.Clear();
            unlocks.Children.Add(Text("Recent unlocks", 18, true));
            string coverage;
            if (timeline == null) coverage = "Loading saved achievement progress…";
            else if (timeline.LibraryGames == 0) coverage = "Connect a Steam or RetroAchievements account to see unlocks.";
            else
            {
                coverage = "Provider unlock dates from saved progress for " + timeline.CoveredGames + " of " + timeline.LibraryGames
                    + " library games";
                if (timeline.OldestSnapshotUtc > 0)
                    coverage += " · oldest saved " + new DateTime(timeline.OldestSnapshotUtc, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                coverage += ".";
                if (timeline.CoveredGames < timeline.LibraryGames) coverage += " Partial: other games are added as background achievement checks run.";
                if (timeline.UndatedEarned > 0) coverage += " " + timeline.UndatedEarned + " earned without a provider date are not placed on the timeline.";
            }
            if (refreshFailed) coverage = "Refresh failed; showing previously loaded unlocks. " + coverage;
            var caption = Text(coverage, 12); caption.Margin = new Thickness(0, 4, 0, 12);
            unlocks.Children.Add(caption);
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
                Focusable = false
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
                    ToolTip = (row.Description ?? "").Trim() is var d && d.Length > 0 ? d + "\nOpen " + unlock.Game.Title + " achievements"
                        : "Open " + unlock.Game.Title + " achievements" };
                if (!SystemParameters.HighContrast)
                    button.Style = LibraryButtonChrome.LibraryToolbarButtonStyle("#00000000", "#00000000", "#304956", "#18242B");
                AutomationProperties.SetName(button, title.Text + ", " + unlock.Game.Title + ", earned " + date
                    + (rarity.Length > 0 ? ", " + rarity : "") + ". Open achievements.");
                AccessibilityUi.TryApplyFocusVisualStyle(button);
                var target = unlock.Game;
                button.Click += async (_, __) => await OpenGameAsync(target);
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
