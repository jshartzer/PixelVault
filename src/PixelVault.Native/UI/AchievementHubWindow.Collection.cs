using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        // Pages bound card creation and icon downloads for large libraries.
        internal const int CollectionPageSize = 100;
        const string UserAgent = "PixelVault/AchievementHub";

        readonly TextBlock collectionTotals = Text("", 12);
        readonly TextBlock collectionShowing = Text("", 12);
        readonly ContentControl collectionResults = new ContentControl { Focusable = false };
        readonly TextBox collectionSearch = new TextBox { Width = 230, Padding = new Thickness(6, 4, 6, 4) };
        readonly ComboBox collectionStatus = new ComboBox { Width = 140 };
        readonly ComboBox collectionGame = new ComboBox { Width = 220 };
        readonly ComboBox collectionRarity = new ComboBox { Width = 140 };
        readonly ComboBox collectionMissable = new ComboBox { Width = 170 };
        readonly ComboBox collectionSort = new ComboBox { Width = 170 };
        readonly ComboBox collectionView = new ComboBox { Width = 90 };
        readonly Button collectionShowMore = new Button { Content = "Show more", Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Left };
        readonly DispatcherTimer collectionSearchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        AchievementHubCollection collection;
        List<AchievementHubEntry> collectionMatches = new();
        Panel collectionPanel;
        int collectionShown;
        bool collectionUpdatingGames;

        internal AchievementHubCollection Collection => collection;
        internal IReadOnlyList<AchievementHubEntry> CollectionMatches => collectionMatches;
        internal int CollectionShown => collectionShown;

        Border BuildCollectionSection()
        {
            var content = new StackPanel();
            content.Children.Add(Text("All achievements", 18, true));
            collectionTotals.Margin = new Thickness(0, 4, 0, 12);
            content.Children.Add(collectionTotals);

            Fill(collectionStatus, ("All", AchievementHubStatusFilter.All), ("Earned", AchievementHubStatusFilter.Earned), ("Unearned", AchievementHubStatusFilter.Unearned));
            Fill(collectionRarity, ("All", AchievementHubRarityFilter.All), ("Common", AchievementHubRarityFilter.Common),
                ("Bronze", AchievementHubRarityFilter.Bronze), ("Silver", AchievementHubRarityFilter.Silver), ("Gold", AchievementHubRarityFilter.Gold),
                ("Uber rare", AchievementHubRarityFilter.UberRare), ("Rarity unavailable", AchievementHubRarityFilter.Unavailable));
            Fill(collectionSort, ("Rarest first", AchievementHubSort.RarestFirst), ("Most common first", AchievementHubSort.MostCommonFirst),
                ("Newest earned", AchievementHubSort.NewestEarned), ("Oldest earned", AchievementHubSort.OldestEarned),
                ("Game", AchievementHubSort.Game), ("Achievement name", AchievementHubSort.Name));
            Fill(collectionView, ("Grid", "grid"), ("List", "list"));
            Fill(collectionMissable, ("All achievements", AchievementMissableFilter.All),
                ("Missable only", AchievementMissableFilter.Missable), ("Not marked missable", AchievementMissableFilter.NotMarkedMissable));
            collectionGame.Items.Add(new ComboBoxItem { Content = "All games", Tag = null });
            collectionGame.SelectedIndex = 0;

            var controls = new WrapPanel();
            controls.Children.Add(Labeled("Search", collectionSearch, "Search achievements by name, description, or game"));
            controls.Children.Add(Labeled("Status", collectionStatus, "Filter by earned status"));
            controls.Children.Add(Labeled("Game", collectionGame, "Filter to one game"));
            controls.Children.Add(Labeled("Rarity", collectionRarity, "Filter by rarity tier"));
            controls.Children.Add(Labeled("Missable", collectionMissable, "Filter by missable status"));
            controls.Children.Add(Labeled("Sort", collectionSort, "Sort achievements"));
            controls.Children.Add(Labeled("View", collectionView, "Grid or list view"));
            content.Children.Add(controls);

            collectionShowing.Margin = new Thickness(0, 6, 0, 8);
            content.Children.Add(collectionShowing);
            content.Children.Add(collectionResults);
            collectionShowMore.Background = Color("#243640", SystemColors.ControlBrush);
            collectionShowMore.Foreground = Color("#EFF4F7", SystemColors.ControlTextBrush);
            collectionShowMore.BorderBrush = Color("#415561", SystemColors.ActiveBorderBrush);
            collectionShowMore.Margin = new Thickness(0, 10, 0, 0);
            collectionShowMore.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(collectionShowMore, "Show more achievements");
            collectionShowMore.Click += delegate { AppendCollectionPage(); };
            content.Children.Add(collectionShowMore);

            collectionSearchDelay.Tick += delegate { collectionSearchDelay.Stop(); ApplyCollectionQuery(); };
            collectionSearch.TextChanged += delegate { collectionSearchDelay.Stop(); collectionSearchDelay.Start(); };
            foreach (var box in new[] { collectionStatus, collectionRarity, collectionMissable, collectionSort, collectionView })
                box.SelectionChanged += delegate { ApplyCollectionQuery(); };
            collectionGame.SelectionChanged += delegate { if (!collectionUpdatingGames) ApplyCollectionQuery(); };
            Closed += delegate { collectionSearchDelay.Stop(); };
            ResetCollection();

            return new Border
            {
                Background = Color("#18232B", SystemColors.WindowBrush),
                BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(22, 18, 22, 22),
                Margin = new Thickness(0, 0, 0, 16),
                Child = content
            };
        }

        static void Fill<T>(ComboBox box, params (string Label, T Value)[] items)
        {
            foreach (var (label, value) in items) box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            box.SelectedIndex = 0;
        }

        static FrameworkElement Labeled(string label, Control control, string accessibleName)
        {
            AutomationProperties.SetName(control, accessibleName);
            control.VerticalContentAlignment = VerticalAlignment.Center;
            control.Height = 30;
            var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
            var caption = Text(label, 11);
            caption.Margin = new Thickness(0, 0, 0, 3);
            panel.Children.Add(caption);
            panel.Children.Add(control);
            return panel;
        }

        static T Selected<T>(ComboBox box) => box.SelectedItem is ComboBoxItem { Tag: T value } ? value : default;

        internal AchievementHubQuery CollectionQuery => new AchievementHubQuery
        {
            Search = collectionSearch.Text,
            Status = Selected<AchievementHubStatusFilter>(collectionStatus),
            GameKey = Selected<string>(collectionGame),
            Rarity = Selected<AchievementHubRarityFilter>(collectionRarity),
            Missable = Selected<AchievementMissableFilter>(collectionMissable),
            Sort = Selected<AchievementHubSort>(collectionSort)
        };

        internal void ResetCollection()
        {
            ResetScore();
            collection = null;
            collectionMatches = new List<AchievementHubEntry>();
            collectionShown = 0;
            collectionTotals.Text = "Loading saved achievement progress…";
            collectionShowing.Text = "";
            collectionResults.Content = null;
            collectionShowMore.Visibility = Visibility.Collapsed;
        }

        void RenderCollection(AchievementHubCollection next)
        {
            if (next == null || next.Signature == collection?.Signature) return;
            var sameGames = collection != null && GameList(collection) == GameList(next);
            collection = next;
            var totals = Totals(next);
            if (collectionTotals.Text != totals) collectionTotals.Text = totals;
            if (sameGames)
            {
                // Background fetches re-stamp snapshots constantly; rebuilding identical entries is the hub's main UI-thread cost.
                if (!KeepShownEntries()) ApplyCollectionQuery();
                return;
            }
            var selected = Selected<string>(collectionGame);
            collectionUpdatingGames = true;
            try
            {
                while (collectionGame.Items.Count > 1) collectionGame.Items.RemoveAt(1);
                foreach (var game in next.Games)
                    collectionGame.Items.Add(new ComboBoxItem
                    {
                        Content = game.Title + (next.Games.Any(g => g != game && g.Title == game.Title)
                            ? " (" + (game.Provider == "steam" ? "Steam" : "RetroAchievements") + ")" : ""),
                        Tag = game.Key
                    });
                collectionGame.SelectedItem = collectionGame.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected)
                    ?? collectionGame.Items[0];
            }
            finally { collectionUpdatingGames = false; }
            ApplyCollectionQuery();
        }

        static string GameList(AchievementHubCollection c) => string.Join("\n", c.Games.Select(g => g.Key + "\t" + g.Title));

        /// <summary>Re-queries the new collection and keeps the rendered entries when everything they display is unchanged.</summary>
        bool KeepShownEntries()
        {
            if (collectionPanel == null || collectionShown == 0) return false;
            var matches = collection.Query(CollectionQuery, DateTime.UtcNow.Ticks);
            if (matches.Count < collectionShown || collectionMatches.Count < collectionShown) return false;
            for (var i = 0; i < collectionShown; i++)
                if (EntryFingerprint(matches[i]) != EntryFingerprint(collectionMatches[i])) return false;
            collectionMatches = matches;
            UpdateCollectionShowing();
            return true;
        }

        static string EntryFingerprint(AchievementHubEntry entry)
        {
            var r = entry.Row;
            return string.Join("\u001f", entry.Game.Key, entry.Game.Title, r.ProviderAchievementId, r.Title, r.Description, r.Meta,
                r.ProgressKnown, r.Unlocked, r.UnlockUtcTicks, r.GlobalUnlockPercent, r.CounterCurrent, r.CounterMinimum, r.CounterTarget,
                r.Hidden, r.IconUrlColor, r.Points, r.PointsProvisional, r.AwardedPoints, r.AwardProvisional, r.IsMissable);
        }

        static string Totals(AchievementHubCollection c)
        {
            if (c.LibraryGames == 0) return "Connect a Steam or RetroAchievements account to see your collection.";
            var known = c.Earned + c.Unearned;
            var text = known == 0 ? "No saved achievement progress yet."
                : c.Earned.ToString("N0", CultureInfo.CurrentCulture) + " of " + known.ToString("N0", CultureInfo.CurrentCulture) + " earned ("
                    + Math.Round(c.Earned * 100d / known, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture) + "%) across "
                    + c.Games.Count.ToString("N0", CultureInfo.CurrentCulture) + " games · " + c.Unearned.ToString("N0", CultureInfo.CurrentCulture) + " unearned";
            if (c.HasPoints)
                text += " · " + c.EarnedPoints.ToString("N0", CultureInfo.CurrentCulture) + " / "
                    + c.AvailablePoints.ToString("N0", CultureInfo.CurrentCulture) + " PV pts in synced library games"
                    + (c.ProvisionalAwards > 0 ? " · " + c.ProvisionalAwards + " provisional awards" : "");
            if (c.GamesWithoutAchievements > 0) text += " · " + c.GamesWithoutAchievements + " games have no achievements";
            if (c.GamesWithoutProgress > 0)
                text += " · progress unavailable for " + c.GamesWithoutProgress + " of " + c.LibraryGames
                    + " library games (background achievement checks add them as they succeed)";
            return text + ". Totals ignore filters.";
        }

        void ApplyCollectionQuery()
        {
            if (collection == null) return;
            collectionMatches = collection.Query(CollectionQuery, DateTime.UtcNow.Ticks);
            collectionShown = 0;
            collectionPanel = Selected<string>(collectionView) == "list" ? new StackPanel() : new WrapPanel();
            collectionResults.Content = collectionMatches.Count == 0
                ? Text(collection.Entries.Count == 0 ? "Achievements appear here as progress is saved." : "No achievements match these filters.", 14)
                : collectionPanel;
            AppendCollectionPage();
        }

        void AppendCollectionPage()
        {
            var list = collectionPanel is StackPanel;
            foreach (var entry in collectionMatches.Skip(collectionShown).Take(CollectionPageSize))
                collectionPanel.Children.Add(list ? ListEntry(entry) : GridEntry(entry));
            collectionShown = Math.Min(collectionMatches.Count, collectionShown + CollectionPageSize);
            UpdateCollectionShowing();
        }

        void UpdateCollectionShowing()
        {
            collectionShowing.Text = collectionMatches.Count == 0 ? ""
                : "Showing " + collectionShown.ToString("N0", CultureInfo.CurrentCulture) + " of "
                    + collectionMatches.Count.ToString("N0", CultureInfo.CurrentCulture) + " matching";
            collectionShowMore.Visibility = collectionShown < collectionMatches.Count ? Visibility.Visible : Visibility.Collapsed;
        }

        static string EntryState(GameAchievementsFetchService.AchievementRow row)
        {
            if (!(row.ProgressKnown && row.Unlocked)) return row.ProgressKnown ? "Not earned" : "Progress unknown";
            var date = AchievementsInfoWindow.FormatAchievementEarnedDateLabel(row.UnlockUtcTicks);
            return string.IsNullOrWhiteSpace(date) || !AchievementCollectionStats.ValidUnlock(row, DateTime.UtcNow.Ticks) ? "Earned · date unavailable" : "Earned " + date;
        }

        // The shared toolbar template centers its content, which would keep list rows from filling the width.
        ControlTemplate entryTemplate;
        ControlTemplate EntryTemplate => entryTemplate ??= BuildEntryTemplate();
        static ControlTemplate BuildEntryTemplate()
        {
            var highlight = SystemParameters.HighContrast ? SystemColors.HighlightBrush : UiBrushHelper.FromHex("#304956");
            if (highlight.CanFreeze) highlight.Freeze();
            var template = new ControlTemplate(typeof(Button));
            var chrome = new FrameworkElementFactory(typeof(Border)) { Name = "Chrome" };
            chrome.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            chrome.SetValue(Border.PaddingProperty, new Thickness(2));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            chrome.AppendChild(presenter);
            template.VisualTree = chrome;
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, highlight, "Chrome"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BackgroundProperty, highlight, "Chrome"));
            template.Triggers.Add(focus);
            return template;
        }

        Button EntryButton(AchievementHubEntry entry, Border card)
        {
            var row = entry.Row;
            var rarity = AchievementRarityUi.Label(row);
            var button = new Button
            {
                Content = card, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = entry.Game.Title + Environment.NewLine + AchievementsInfoWindow.BuildAchievementGridToolTip(row)
            };
            button.Template = EntryTemplate;
            AutomationProperties.SetName(button, (string.IsNullOrWhiteSpace(row.Title) ? "Achievement" : row.Title) + ", " + entry.Game.Title
                + ", " + EntryState(row) + (row.Points.HasValue ? ", " + AchievementScoring.Label(row) : "")
                + (AchievementCollectionUi.ShowMissable(row) ? ", Missable" : "")
                + (rarity.Length > 0 ? ", " + rarity : "") + ". Open achievement details.");
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            var game = entry.Game;
            button.Click += async (_, __) => await OpenAchievementAsync(game, row);
            AttachPinMenu(button, game);
            AttachTrackingMenu(button, game, row);
            if (guides != null && row.HasStableProviderIdentity)
            {
                button.ContextMenu ??= new ContextMenu();
                AchievementsInfoWindow.AddCategoryMenu(button.ContextMenu, row,
                    collection.Entries.Where(e => e.Game.Key == game.Key).Select(e => e.Row), name =>
                    {
                        try
                        {
                            var rows = collection.Entries.Where(e => e.Game.Key == game.Key).Select(e => e.Row).ToList();
                            guides.SyncDefinitionsAndLoadGuides(game.Folder?.GameId ?? "", rows);
                            var order = rows.FirstOrDefault(r => string.Equals(AchievementsInfoWindow.EffectiveAchievementGroupName(r),
                                name, StringComparison.OrdinalIgnoreCase))?.GroupSortOrder ?? 0;
                            var saved = AchievementsInfoWindow.SaveCategory(guides, row, name, order);
                            row.GroupName = saved.GroupName;
                            row.GroupSortOrder = saved.GroupSortOrder;
                            ApplyCollectionQuery();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, "Could not save the achievement category. " + ex.Message,
                                "Achievement category", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }, () => true, this);
            }
            return button;
        }

        FrameworkElement GridEntry(AchievementHubEntry entry)
        {
            var row = entry.Row;
            var earned = row.ProgressKnown && row.Unlocked;
            var image = new Image { Stretch = Stretch.UniformToFill };
            var icon = new Border
            {
                Width = 64, Height = 64, Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Background = Color("#0D1418", SystemColors.WindowBrush), BorderBrush = Color(earned ? "#527A64" : "#2B3A44", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, Child = image
            };
            AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, UserAgent);
            var title = new TextBlock
            {
                Text = row.Title ?? "", FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32,
                Foreground = Color(earned ? "#FFFFFF" : "#CCD0D2", SystemColors.WindowTextBrush)
            };
            var game = new TextBlock
            {
                Text = entry.Game.Title, FontSize = 10, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 3, 0, 0), Foreground = Color("#9CB1BC", SystemColors.WindowTextBrush)
            };
            var state = new TextBlock
            {
                Text = AchievementScoring.PointText(row), ToolTip = AchievementScoring.Explanation(row),
                FontSize = 10, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0), Foreground = Color(earned ? "#88B8A0" : "#7D909C", SystemColors.WindowTextBrush)
            };
            var content = new StackPanel();
            content.Children.Add(AchievementCollectionUi.WithMissableOverlay(icon, row)); content.Children.Add(title); content.Children.Add(game); content.Children.Add(state);
            if (AchievementCollectionUi.MissableLabel(row) is TextBlock missable)
            {
                missable.TextAlignment = TextAlignment.Center;
                content.Children.Add(missable);
            }
            var card = new Border
            {
                Width = 154, Height = 190, Padding = new Thickness(8), CornerRadius = new CornerRadius(7), Child = content,
                Background = Color(earned ? "#15231E" : "#11191E", SystemColors.WindowBrush),
                BorderBrush = Color(earned ? "#3E6A54" : "#2B3A44", SystemColors.ActiveBorderBrush), BorderThickness = new Thickness(1)
            };
            AchievementRarityUi.Apply(card, icon, title, row);
            var button = EntryButton(entry, card);
            button.Margin = new Thickness(0, 0, 8, 8);
            return button;
        }

        FrameworkElement ListEntry(AchievementHubEntry entry)
        {
            var row = entry.Row;
            var earned = row.ProgressKnown && row.Unlocked;
            var image = new Image { Stretch = Stretch.UniformToFill };
            var icon = new Border
            {
                Width = 48, Height = 48, Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(6), ClipToBounds = true,
                Background = Color("#141E24", SystemColors.WindowBrush), BorderBrush = Color("#2B3A44", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Child = image
            };
            AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, UserAgent);
            var grid = new Grid { MinHeight = 52 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(AchievementCollectionUi.WithMissableOverlay(icon, row));
            var text = new StackPanel();
            var title = new TextBlock { Text = row.Title ?? "", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                Foreground = Color(earned ? "#FFFFFF" : "#CCD0D2", SystemColors.WindowTextBrush) };
            text.Children.Add(title);
            if (!string.IsNullOrWhiteSpace(row.Description))
                text.Children.Add(new TextBlock { Text = row.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0), Foreground = Color("#9CB1BC", SystemColors.WindowTextBrush) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var meta = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Width = 190 };
            meta.Children.Add(new TextBlock { Text = entry.Game.Title, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Color("#B8C9D4", SystemColors.WindowTextBrush) });
            meta.Children.Add(new TextBlock { Text = AchievementScoring.PointText(row), ToolTip = AchievementScoring.Explanation(row),
                FontSize = 11, Margin = new Thickness(0, 3, 0, 0),
                Foreground = Color(earned ? "#88B8A0" : "#7D909C", SystemColors.WindowTextBrush) });
            var rarity = AchievementCollectionStats.ValidPercent(row.GlobalUnlockPercent)
                ? row.GlobalUnlockPercent.Value.ToString(row.GlobalUnlockPercent < .01 ? "0.########" : "0.##", CultureInfo.CurrentCulture) + "% of players"
                : "Rarity unavailable";
            meta.Children.Add(new TextBlock { Text = rarity, FontSize = 11, Margin = new Thickness(0, 3, 0, 0),
                Foreground = Color("#7D909C", SystemColors.WindowTextBrush) });
            if (AchievementCollectionUi.MissableLabel(row) is TextBlock missable) meta.Children.Add(missable);
            Grid.SetColumn(meta, 2);
            grid.Children.Add(meta);
            var card = new Border
            {
                Padding = new Thickness(10, 8, 10, 8), Child = grid,
                Background = earned ? Color("#15231E", SystemColors.WindowBrush) : Brushes.Transparent,
                BorderBrush = Color(earned ? "#3E6A54" : "#24323A", SystemColors.ActiveBorderBrush),
                BorderThickness = earned ? new Thickness(2, 0, 0, 1) : new Thickness(0, 0, 0, 1)
            };
            AchievementRarityUi.Apply(card, icon, title, row, list: true);
            var button = EntryButton(entry, card);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            return button;
        }
    }
}
