using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    sealed partial class AchievementHubWindow
    {
        /// <summary>Pinned games use large covers; recently played uses compact ones so the two rows read differently.</summary>
        const double PinnedCoverWidth = 220, PinnedCoverHeight = 320, RecentCoverWidth = 116, RecentCoverHeight = 169;

        sealed class ShelfState
        {
            public string Signature;
            public ScrollViewer Scroller;
            public StackPanel Strip;
            public Dictionary<string, FrameworkElement> Cards = new();
        }

        void RenderShelf(StackPanel panel, string heading, string status, List<AchievementHubCard> cards, bool large, string empty)
        {
            var previous = panel.Tag as ShelfState;
            var keys = new List<string>();
            foreach (var card in cards ?? new List<AchievementHubCard>()) keys.Add(CardFingerprint(card, large));
            var signature = string.Join("\n", heading, status, empty, string.Join("\u001e", keys));
            if (previous != null && previous.Signature == signature) return;
            // Loads report several times; keep the row where the user scrolled it, and reuse unchanged cards so covers don't reload.
            var offset = previous?.Scroller?.HorizontalOffset ?? 0;
            previous?.Strip?.Children.Clear();
            panel.Children.Clear();
            var state = new ShelfState { Signature = signature };
            panel.Tag = state;
            panel.Children.Add(Text(heading, 18, true));
            if (!string.IsNullOrWhiteSpace(status))
            {
                var caption = Text(status, 12); caption.Margin = new Thickness(0, 6, 0, 0);
                panel.Children.Add(caption);
            }
            if (cards == null || cards.Count == 0)
            {
                var none = Text(empty, 14); none.Margin = new Thickness(0, 12, 0, 22);
                panel.Children.Add(none);
                return;
            }
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < cards.Count; i++)
            {
                if (state.Cards.ContainsKey(keys[i])) continue;
                var element = previous != null && previous.Cards.TryGetValue(keys[i], out var reused) ? reused : GameCard(cards[i], large);
                state.Cards[keys[i]] = element;
                strip.Children.Add(element);
            }
            var row = HoverScrollRow(strip, heading, out var scroller);
            row.Margin = new Thickness(0, 12, 0, 22);
            panel.Children.Add(row);
            state.Scroller = scroller;
            state.Strip = strip;
            if (offset > 0) scroller.ScrollToHorizontalOffset(offset);
        }

        /// <summary>Everything a game card displays, so identical cards can be kept across load reports.</summary>
        string CardFingerprint(AchievementHubCard card, bool large)
        {
            var rows = card.Snapshot?.Rows;
            var stats = rows == null ? null : AchievementCollectionStats.Build(rows, DateTime.UtcNow.Ticks, card.Snapshot.ScoreTotals);
            return string.Join("\u001f", card.Game?.Key, card.Game?.Title, card.Game?.ArtPath, card.LastPlayedUtc, large,
                stats == null ? "none" : stats.Unknown + "/" + stats.Earned + "/" + stats.Total + "/" + stats.Percent + "/" + stats.HasPoints + "/" + stats.ScoreSummary,
                large ? card.Snapshot?.UpdatedUtc ?? 0 : 0, pins?.IsPinned(card.Game?.Key ?? "") ?? false);
        }

        FrameworkElement GameCard(AchievementHubCard card, bool large)
        {
            var game = card.Game;
            double width = large ? PinnedCoverWidth : RecentCoverWidth, height = large ? PinnedCoverHeight : RecentCoverHeight;
            var content = new StackPanel { Width = width, Margin = new Thickness(large ? 10 : 8) };
            content.Children.Add(cover?.Invoke(game, width, height) ?? new Border { Width = width, Height = height,
                CornerRadius = new CornerRadius(8), Background = Color("#11181D", SystemColors.WindowBrush) });
            var name = Text(game.Title, large ? 15 : 12, true);
            name.Margin = new Thickness(0, large ? 10 : 7, 0, 3);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (large) name.TextWrapping = TextWrapping.NoWrap;
            else { name.MinHeight = 32; name.MaxHeight = 32; }
            content.Children.Add(name);
            var provider = game.Provider == "steam" ? "Steam" : "RetroAchievements";
            var played = card.LastPlayedUtc > 0
                ? " · " + new DateTime(card.LastPlayedUtc, DateTimeKind.Utc).ToLocalTime().ToString(large ? "g" : "d", CultureInfo.CurrentCulture) : "";
            var meta = Text(provider + played, large ? 11 : 10);
            meta.TextWrapping = TextWrapping.NoWrap; meta.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(meta);
            var rows = card.Snapshot?.Rows;
            var stats = rows == null ? null : AchievementCollectionStats.Build(rows, DateTime.UtcNow.Ticks, card.Snapshot.ScoreTotals);
            var progress = stats == null || stats.Unknown > 0 ? (large ? "Achievement progress unavailable" : "Progress unavailable")
                : stats.Total == 0 ? (large ? "No achievements for this game" : "No achievements")
                : stats.Earned + " / " + stats.Total + (large ? " earned · " : " · ") + stats.Percent + "%";
            var summary = Text(progress, large ? 13 : 11, true);
            summary.Margin = new Thickness(0, large ? 8 : 5, 0, 4);
            content.Children.Add(summary);
            if (stats?.HasPoints == true)
            {
                var score = Text(stats.ScoreSummary, large ? 12 : 10);
                score.ToolTip = "Earned points are saved in the database. Available points include current locked values. Provisional awards finalize when rarity is known.";
                content.Children.Add(score);
            }
            if (stats?.Percent != null && stats.Unknown == 0)
                content.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = stats.Percent.Value, Height = large ? 6 : 4,
                    Foreground = Color("#79BBA1", SystemColors.HighlightBrush), Background = Color("#2B3C47", SystemColors.ControlBrush) });
            if (large && card.Snapshot != null && card.Snapshot.UpdatedUtc > 0)
            {
                var saved = Text("Saved " + new DateTime(card.Snapshot.UpdatedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture), 11);
                saved.Margin = new Thickness(0, 4, 0, 0);
                content.Children.Add(saved);
            }
            var button = new Button { Content = content, Padding = new Thickness(0),
                Background = Color("#18232B", SystemColors.WindowBrush), Foreground = Color("#EFF4F7", SystemColors.WindowTextBrush),
                BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush), Cursor = Cursors.Hand,
                ToolTip = "Open " + game.Title + " achievements", VerticalContentAlignment = VerticalAlignment.Top,
                VerticalAlignment = VerticalAlignment.Top };
            if (!SystemParameters.HighContrast)
                button.Style = LibraryButtonChrome.LibraryToolbarButtonStyle("#18232B", "#2B3C47", "#243640", "#11181D");
            AutomationProperties.SetName(button, game.Title + ", " + progress + ". Open achievements.");
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            button.Click += async (_, __) => await OpenGameAsync(game);
            AttachPinMenu(button, game);
            var host = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, large ? 14 : 10, 0) };
            host.Children.Add(button);
            if (pins != null) host.Children.Add(PinButton(game, host, large));
            return host;
        }

        Button PinButton(AchievementHubGame game, Grid host, bool large)
        {
            var isPinned = pins.IsPinned(game.Key);
            var size = large ? 32 : 26;
            var glyph = new TextBlock { Text = isPinned ? "\uE840" : "\uE718", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = large ? 15 : 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Color(isPinned ? "#79BBA1" : "#EFF4F7", SystemColors.ControlTextBrush) };
            var label = (isPinned ? "Unpin " : "Pin ") + game.Title;
            var pin = new Button { Content = glyph, Width = size, Height = size, Padding = new Thickness(0), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, (large ? 10 : 8) + 6, (large ? 10 : 8) + 6, 0),
                ToolTip = isPinned ? "Unpin from the Achievement Hub" : "Pin to the top of the Achievement Hub" };
            if (!SystemParameters.HighContrast)
                pin.Style = LibraryButtonChrome.LibraryCircleToolbarButtonStyle("#D911181D", "#5C3A4D59", "#F0243640", "#F011181D");
            AutomationProperties.SetName(pin, label);
            AccessibilityUi.TryApplyFocusVisualStyle(pin);
            pin.Click += (_, __) => TogglePin(game);
            // Unpinned covers stay clean until hovered or focused; pinned ones always show their state.
            void Sync() => pin.Opacity = isPinned || host.IsMouseOver || host.IsKeyboardFocusWithin ? 1 : 0;
            host.MouseEnter += (_, __) => Sync();
            host.MouseLeave += (_, __) => Sync();
            host.IsKeyboardFocusWithinChanged += (_, __) => Sync();
            Sync();
            return pin;
        }

        void AttachPinMenu(FrameworkElement target, AchievementHubGame game)
        {
            if (pins == null || game == null) return;
            string Header() => pins.IsPinned(game.Key) ? "Unpin game from hub" : "Pin game to hub";
            var item = new MenuItem { Header = Header() };
            item.Click += (_, __) => TogglePin(game);
            target.ContextMenu = new ContextMenu();
            target.ContextMenu.Items.Add(item);
            target.ContextMenuOpening += (_, __) => item.Header = Header();
        }

        internal void TogglePin(AchievementHubGame game)
        {
            if (pins == null || game == null) return;
            try { pins.Toggle(game.Key); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Pinned games could not be saved: " + ex.Message, "Achievement Hub");
                return;
            }
            _ = RefreshAsync(false, restart: true);
        }

        /// <summary>Horizontal row without a scrollbar; edge arrows appear on overflow and scroll while hovered (click jumps a page).</summary>
        FrameworkElement HoverScrollRow(FrameworkElement strip, string name, out ScrollViewer scroller)
        {
            var sv = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.HorizontalOnly, Focusable = false };
            scroller = sv;
            AutomationProperties.SetName(sv, name);
            var host = new Grid();
            sv.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta); return; }
                host.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sv });
            };
            var left = ScrollNudge(true, name);
            var right = ScrollNudge(false, name);
            host.Children.Add(sv); host.Children.Add(left); host.Children.Add(right);
            var direction = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            void Update()
            {
                var overflow = sv.ScrollableWidth > 0.5;
                left.Visibility = overflow && sv.HorizontalOffset > 0.75 ? Visibility.Visible : Visibility.Collapsed;
                right.Visibility = overflow && sv.HorizontalOffset < sv.ScrollableWidth - 0.75 ? Visibility.Visible : Visibility.Collapsed;
                if ((direction < 0 && left.Visibility != Visibility.Visible) || (direction > 0 && right.Visibility != Visibility.Visible))
                { direction = 0; timer.Stop(); }
            }
            timer.Tick += (_, __) =>
            {
                sv.ScrollToHorizontalOffset(Math.Max(0, Math.Min(sv.ScrollableWidth, sv.HorizontalOffset + direction * 10)));
                Update();
            };
            void Hover(Border nudge, int dir)
            {
                nudge.MouseEnter += (_, __) => { direction = dir; timer.Start(); };
                nudge.MouseLeave += (_, __) => { direction = 0; timer.Stop(); };
                nudge.MouseLeftButtonUp += (_, __) =>
                    sv.ScrollToHorizontalOffset(Math.Max(0, Math.Min(sv.ScrollableWidth, sv.HorizontalOffset + dir * sv.ViewportWidth * 0.8)));
            }
            Hover(left, -1); Hover(right, 1);
            sv.ScrollChanged += (_, __) => Update();
            sv.SizeChanged += (_, __) => Update();
            host.Unloaded += (_, __) => { direction = 0; timer.Stop(); };
            return host;
        }

        static Border ScrollNudge(bool leftEdge, string name)
        {
            Brush fade;
            if (SystemParameters.HighContrast) fade = SystemColors.WindowBrush;
            else
            {
                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                var solid = System.Windows.Media.Color.FromArgb(235, 0x11, 0x18, 0x1D);
                var clear = System.Windows.Media.Color.FromArgb(0, 0x11, 0x18, 0x1D);
                gradient.GradientStops.Add(new GradientStop(leftEdge ? solid : clear, 0));
                gradient.GradientStops.Add(new GradientStop(leftEdge ? clear : solid, 1));
                gradient.Freeze();
                fade = gradient;
            }
            var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17),
                Background = Color("#E6243640", SystemColors.ControlBrush), BorderBrush = Color("#415561", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1), HorizontalAlignment = leftEdge ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = leftEdge ? "\uE76B" : "\uE76C", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
                    Foreground = Color("#EFF4F7", SystemColors.ControlTextBrush), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center } };
            var nudge = new Border { Width = 64, Background = fade, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed, Child = badge,
                HorizontalAlignment = leftEdge ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(leftEdge ? 4 : 0, 0, leftEdge ? 0 : 4, 0) };
            Panel.SetZIndex(nudge, 3);
            AutomationProperties.SetName(nudge, (leftEdge ? "Scroll left: " : "Scroll right: ") + name);
            return nudge;
        }
    }
}
