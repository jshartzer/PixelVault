using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    /// <summary>One achievement from the Achievement Hub: status, counter progress, rarity context, and its guide.</summary>
    sealed class AchievementDetailWindow : Window
    {
        readonly AchievementHubGame game;
        readonly GameAchievementsFetchService.AchievementRow row;
        readonly List<GameAchievementsFetchService.AchievementRow> gameRows;
        readonly IAchievementGuideService guides;
        readonly AchievementTrackingStore trackingStore;
        readonly string trackingAccount;
        readonly TrackerNavigation trackerNavigation;
        internal TrackerNavigation TrackingNavigation=>trackerNavigation;
        readonly Func<bool> isGamePinned;
        readonly Action toggleGamePin;
        readonly StackPanel guidePanel = new StackPanel();
        readonly AchievementInlineTrackers linkedTrackers = new AchievementInlineTrackers();
        readonly GameMapLinks mapLinks = new GameMapLinks();
        readonly Button nextUp = new Button();
        readonly Button gamePin = new Button();
        TextBlock categoryFact;
        TextBlock contentFact;
        internal bool OpenGameRequested { get; private set; }

        internal AchievementDetailWindow(Window owner, AchievementHubGame game, GameAchievementsFetchService.AchievementRow row,
            IEnumerable<GameAchievementsFetchService.AchievementRow> gameRows, long savedUtc, IAchievementGuideService guides,
            Func<bool> isGamePinned = null, Action toggleGamePin = null, AchievementTrackingStore trackingStore = null, string trackingAccount = null)
        {
            this.game = game; this.row = row; this.guides = guides;
            this.trackingStore = trackingStore; this.trackingAccount = trackingAccount;
            if(trackingStore!=null)trackerNavigation=new TrackerNavigation(trackingStore,()=>new AchievementHubSession{Games=new(){game},SteamAccount=game.Provider=="steam"?trackingAccount:null,RetroAccount=game.Provider=="retroachievements"?trackingAccount:null});
            this.isGamePinned = isGamePinned; this.toggleGamePin = toggleGamePin;
            this.gameRows = (gameRows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>()).Where(r => r != null).ToList();
            if (!this.gameRows.Contains(row)) this.gameRows.Add(row);
            if (owner?.IsVisible == true) Owner = owner;
            Title = (string.IsNullOrWhiteSpace(row.Title) ? "Achievement" : row.Title.Trim()) + " — " + game.Title;
            Width = 640; Height = 760; MinWidth = 460; MinHeight = 420;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = Color("#11181D", SystemColors.WindowBrush);
            Foreground = Color("#EFF4F7", SystemColors.WindowTextBrush);
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };

            var body = new StackPanel { Margin = new Thickness(24, 22, 24, 24) };
            body.Children.Add(BuildHeader());
            if(trackerNavigation!=null)body.Children.Add(BuildTrackingAction());
            var counter = BuildCounter();
            if (counter != null) body.Children.Add(counter);
            body.Children.Add(Section("Details", BuildFacts(savedUtc)));
            body.Children.Add(Section("Guide", guidePanel));
            mapLinks.Bind(trackingStore, game.Provider, game.Id, game.Title, game.Provider == "steam" ? trackingAccount : null, game.Provider == "retroachievements" ? trackingAccount : null, achievement: row.ProviderAchievementId);
            body.Children.Add(mapLinks);
            linkedTrackers.Bind(trackingStore, game.Provider, game.Id, row.ProviderAchievementId, trackingAccount);
            body.Children.Add(linkedTrackers);
            Activated += (_, __) =>
            {
                linkedTrackers.Reload();
                mapLinks.Bind(trackingStore, game.Provider, game.Id, game.Title, game.Provider == "steam" ? trackingAccount : null, game.Provider == "retroachievements" ? trackingAccount : null, achievement: row.ProviderAchievementId);
            };
            body.Children.Add(BuildActions());
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            RenderGuide();
            RenderPinButtons();
        }

        FrameworkElement BuildHeader()
        {
            var image = new Image { Width = 88, Height = 88, Stretch = Stretch.UniformToFill };
            var icon = new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(10), Child = image, Padding = new Thickness(3),
                Background = Color("#18232B", SystemColors.WindowBrush), BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 18, 0) };
            AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "PixelVault/AchievementHub");
            var title = Text(string.IsNullOrWhiteSpace(row.Title) ? "Achievement" : row.Title.Trim(), 22, true);
            var text = new StackPanel();
            text.Children.Add(title);
            var gameLine = Text(game.Title + " · " + Provider, 13); gameLine.Margin = new Thickness(0, 4, 0, 10);
            text.Children.Add(gameLine);
            var chips = new WrapPanel();
            chips.Children.Add(Chip(Status, row.ProgressKnown && row.Unlocked ? "#2E5A47" : "#243640"));
            if (row.Points.HasValue) chips.Children.Add(AchievementCollectionUi.ScoreChip(row));
            var rarity = AchievementRarityUi.Label(row);
            if (rarity.Length > 0) chips.Children.Add(Chip(rarity, "#3A3324"));
            if (row.Hidden) chips.Children.Add(Chip("Hidden", "#3A2C3A"));
            text.Children.Add(chips);
            var description = (row.Description ?? "").Trim();
            var desc = Text(description.Length > 0 ? description : row.Hidden ? "Hidden achievement; the provider doesn't share its description." : "No description provided.", 14);
            desc.Margin = new Thickness(0, 12, 0, 0);
            text.Children.Add(desc);
            var layout = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            layout.Children.Add(icon); layout.Children.Add(text);
            var card = new Border { Child = layout, Padding = new Thickness(18), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 16),
                Background = Color("#18232B", SystemColors.WindowBrush), BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1) };
            AchievementRarityUi.Apply(card, icon, title, row);
            return card;
        }

        string Provider => game.Provider == "steam" ? "Steam" : "RetroAchievements";

        string Status => !row.ProgressKnown ? "Progress unknown"
            : !row.Unlocked ? "Not earned yet"
            : row.UnlockUtcTicks > 0 && row.UnlockUtcTicks <= DateTime.UtcNow.Ticks
                ? "Earned " + new DateTime(row.UnlockUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                : "Earned (date not reported)";

        FrameworkElement BuildCounter()
        {
            var percent = AchievementCollectionStats.CounterPercent(row);
            if (!percent.HasValue || row.Unlocked) return null;
            var panel = new StackPanel();
            panel.Children.Add(Text(AchievementsInfoWindow.CounterLabel(row) + " · " + percent.Value.ToString("0.#", CultureInfo.CurrentCulture) + "%", 16, true));
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = percent.Value, Height = 8, Margin = new Thickness(0, 8, 0, 6),
                Foreground = Color("#6EAFBF", SystemColors.HighlightBrush), Background = Color("#263C48", SystemColors.ControlBrush) };
            AutomationProperties.SetName(bar, "Counter progress " + AchievementsInfoWindow.CounterLabel(row));
            panel.Children.Add(bar);
            panel.Children.Add(Text("Latest counter reported to Steam for this account. Unlock status is reported separately.", 12));
            return Section("Current progress", panel);
        }

        FrameworkElement BuildFacts(long savedUtc)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Add(string label, UIElement value)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var name = Text(label, 13); name.Margin = new Thickness(0, 0, 12, 10);
                Grid.SetRow(name, grid.RowDefinitions.Count - 1); Grid.SetRow(value, grid.RowDefinitions.Count - 1); Grid.SetColumn(value, 1);
                grid.Children.Add(name); grid.Children.Add(value);
            }
            TextBlock Value(string text) { var t = Text(text, 13, true); t.Margin = new Thickness(0, 0, 0, 10); return t; }

            if (row.Points.HasValue) Add("Achievement points", Value(AchievementScoring.Explanation(row)));

            var percent = AchievementCollectionStats.ValidPercent(row.GlobalUnlockPercent) ? row.GlobalUnlockPercent : null;
            var tier = AchievementRarityUi.Label(row);
            Add("Global rarity", Value(percent.HasValue
                ? percent.Value.ToString("0.##", CultureInfo.CurrentCulture) + "% of players" + (tier.Length > 0 ? " · " + tier : " · Common")
                : "Rarity unavailable"));
            var earned = gameRows.Where(r => r.ProgressKnown && r.Unlocked).ToList();
            if (row.ProgressKnown && row.Unlocked && percent.HasValue)
            {
                var ranked = earned.Where(r => AchievementCollectionStats.ValidPercent(r.GlobalUnlockPercent))
                    .OrderBy(r => r.GlobalUnlockPercent).ToList();
                var rank = ranked.IndexOf(row) + 1;
                if (rank > 0) Add("Among your earned", Value("#" + rank + " rarest of " + ranked.Count + " in this game"));
            }
            var stats = AchievementCollectionStats.Build(gameRows, DateTime.UtcNow.Ticks);
            if (stats.Total > 0 && stats.Unknown == 0 && stats.Percent.HasValue)
            {
                var progress = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                progress.Children.Add(Text(stats.Earned + " of " + stats.Total + " earned · " + stats.Percent + "%", 13, true));
                progress.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = stats.Percent.Value, Height = 5, Margin = new Thickness(0, 6, 0, 0),
                    Foreground = Color("#79BBA1", SystemColors.HighlightBrush), Background = Color("#2B3C47", SystemColors.ControlBrush) });
                Add("Game completion", progress);
            }
            else Add("Game completion", Value("Progress unavailable"));
            contentFact = Value(AchievementsInfoWindow.AchievementContentName(row));
            Add("Game / DLC", contentFact);
            categoryFact = Value(AchievementOrganization.CategoryName(row.GroupName));
            var category = new WrapPanel();
            categoryFact.Margin = new Thickness(0, 4, 10, 6);
            category.Children.Add(categoryFact);
            if (guides != null)
            {
                var changeCategory = AchievementsInfoWindow.BuildCategoryPickerButton(() => row, () => gameRows,
                    ChangeCategory, () => row.HasStableProviderIdentity, this, () => guides.LoadCategoryNames());
                changeCategory.Margin = new Thickness(0, 0, 0, 8);
                category.Children.Add(changeCategory);
            }
            Add("Category", category);
            Add("Provider", Value(Provider));
            if (savedUtc > 0 && savedUtc <= DateTime.UtcNow.Ticks)
                Add("Progress saved", Value(new DateTime(savedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
            return grid;
        }

        void RenderGuide()
        {
            guidePanel.Children.Clear();
            AchievementGuideEntry entry = null;
            string error = null;
            if (guides != null)
            {
                try { entry = guides.LoadGuideEntry(row.Provider, row.ProviderGameId, row.ProviderAchievementId); }
                catch (Exception ex) { error = "Guide could not be loaded: " + ex.Message; }
            }
            var hasGuide = !string.IsNullOrWhiteSpace(entry?.GuideText);
            if (error != null) guidePanel.Children.Add(Text(error, 13));
            else if (guides == null) guidePanel.Children.Add(Text("Guides are unavailable in this view.", 13));
            else if (!hasGuide) guidePanel.Children.Add(Text("No guide written for this achievement yet.", 13));
            if (entry != null)
            {
                row.GroupName = entry.GroupName; row.DlcName = entry.DlcName; row.GroupSortOrder = entry.GroupSortOrder;
                categoryFact.Text = AchievementOrganization.CategoryName(row.GroupName);
                contentFact.Text = AchievementsInfoWindow.AchievementContentName(row);
                var tags = AchievementGuideWindow.GuideReadTags(entry.Tags, entry.IsMissable, row.Unlocked);
                if (tags.Count > 0)
                {
                    var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                    foreach (var tag in tags)
                        chips.Children.Add(Chip(tag, string.Equals(tag, "missable", StringComparison.OrdinalIgnoreCase) ? "#5A2E2E" : "#243640"));
                    guidePanel.Children.Add(chips);
                }
                if (hasGuide)
                    guidePanel.Children.Add(new TextBox { Text = entry.GuideText.Trim(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                        BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Foreground, FontSize = 14,
                        Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 8) });
                if (Uri.TryCreate(entry.SourceUrl?.Trim(), UriKind.Absolute, out var source)
                    && (source.Scheme == Uri.UriSchemeHttps || source.Scheme == Uri.UriSchemeHttp))
                {
                    var link = new Hyperlink(new Run(string.IsNullOrWhiteSpace(entry.SourceTitle) ? source.Host : entry.SourceTitle.Trim()))
                        { NavigateUri = source, Foreground = Color("#79BBD1", SystemColors.HotTrackBrush) };
                    link.RequestNavigate += (_, e) => { OpenUrl(e.Uri.AbsoluteUri); e.Handled = true; };
                    var line = new TextBlock { FontSize = 13, Foreground = Color("#A4B7C3", SystemColors.WindowTextBrush), Margin = new Thickness(0, 0, 0, 4) };
                    line.Inlines.Add("Source: "); line.Inlines.Add(link);
                    guidePanel.Children.Add(line);
                }
                if (hasGuide && entry.GuideUpdatedUtcTicks > 0)
                    guidePanel.Children.Add(Text("Updated " + new DateTime(entry.GuideUpdatedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture), 12));
            }
            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (guides != null) actions.Children.Add(ActionButton(hasGuide ? "Edit guide" : "Add guide", EditGuide));
            actions.Children.Add(ActionButton("Search the web", () => OpenUrl("https://www.google.com/search?q="
                + Uri.EscapeDataString(game.Title + " \"" + (row.Title ?? "").Trim() + "\" achievement guide"))));
            guidePanel.Children.Add(actions);
        }

        void EditGuide()
        {
            try
            {
                AchievementGuideWindow.ShowModal(this, guides, game.Title, game.Folder?.GameId ?? string.Empty, gameRows, row.ProviderAchievementId,
                    trackingStore, game.Provider == "steam" ? trackingAccount : null, game.Provider == "retroachievements" ? trackingAccount : null);
            }
            catch (Exception ex) { MessageBox.Show(this, "The guide could not be opened: " + ex.Message, "Achievement guide"); }
            RenderGuide();
            RenderPinButtons();
        }

        void ChangeCategory(GameAchievementsFetchService.AchievementRow achievement, string name)
        {
            try
            {
                guides.SyncDefinitionsAndLoadGuides(game.Folder?.GameId ?? "", gameRows);
                var order = gameRows.FirstOrDefault(r => string.Equals(r.GroupName, name, StringComparison.OrdinalIgnoreCase))?.GroupSortOrder ?? 0;
                var saved = AchievementsInfoWindow.SaveCategory(guides, achievement, name, order);
                achievement.GroupName = saved.GroupName; achievement.GroupSortOrder = saved.GroupSortOrder; achievement.DlcName = saved.DlcName;
                RenderGuide();
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save the achievement category. " + ex.Message, "Achievement category", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        FrameworkElement BuildActions()
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(ActionButton("Open game achievements", () => { OpenGameRequested = true; Close(); }));
            if (guides != null)
            {
                Dress(nextUp);
                nextUp.Click += (_, __) => ToggleNextUp();
                panel.Children.Add(nextUp);
            }
            if (toggleGamePin != null)
            {
                Dress(gamePin);
                gamePin.Click += (_, __) => { toggleGamePin(); RenderPinButtons(); };
                panel.Children.Add(gamePin);
            }
            return panel;
        }

        FrameworkElement BuildTrackingAction()
        {
            var panel=new StackPanel { Margin=new Thickness(0,0,0,12) };
            var button=AchievementTrackerWindow.Button("Open tracker","Open tracker linked to this achievement");button.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(button);
            void Update()
            {
                try{var count=trackerNavigation.Linked(game,row.ProviderAchievementId).Count;button.Content=count==0?"Create linked tracker…":count==1?"Open tracker":"Open linked trackers ("+count+")";button.IsEnabled=true;}
                catch(Exception ex){button.Content="Trackers unavailable";button.IsEnabled=false;button.ToolTip=ex.Message;}
            }
            button.Click+=(_,__)=>{if(trackerNavigation.Linked(game,row.ProviderAchievementId).Count==0)trackerNavigation.Create(this,game:game,achievement:row.ProviderAchievementId);else trackerNavigation.OpenAchievement(this,game,row.ProviderAchievementId);Update();};
            Activated+=(_,__)=>Update();Update();return panel;
        }

        bool IsNextUp()
        {
            try { return guides?.LoadPinnedAchievements(row.Provider, row.ProviderGameId)?.Any(e => AchievementPinUi.Matches(e, row)) == true; }
            catch { return false; }
        }

        void RenderPinButtons()
        {
            var pinned = IsNextUp();
            nextUp.Content = pinned ? "Remove from Next Up" : "Add to Next Up";
            // Next Up only takes unearned achievements with known progress; otherwise the action doesn't apply.
            nextUp.Visibility = pinned || (row.ProgressKnown && !row.Unlocked) ? Visibility.Visible : Visibility.Collapsed;
            nextUp.ToolTip = "Next Up holds up to three targets per game on its achievement page.";
            AutomationProperties.SetName(nextUp, (string)nextUp.Content);
            var hub = isGamePinned?.Invoke() == true;
            gamePin.Content = hub ? "Unpin game from hub" : "Pin game to hub";
            AutomationProperties.SetName(gamePin, (string)gamePin.Content);
        }

        void ToggleNextUp()
        {
            var pinned = IsNextUp();
            try
            {
                // Pins attach to catalog definitions; games only seen by background checks may not be catalogued yet.
                if (!pinned && guides.LoadGuideEntry(row.Provider, row.ProviderGameId, row.ProviderAchievementId) == null)
                    guides.SyncDefinitionsAndLoadGuides(game.Folder?.GameId ?? string.Empty, gameRows);
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not prepare this achievement for Next Up.\n\n" + ex.Message, "Next Up"); return; }
            AchievementPinUi.Change(this, guides, row, !pinned);
            RenderPinButtons();
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        Button ActionButton(string label, Action action)
        {
            var button = new Button { Content = label };
            Dress(button);
            AutomationProperties.SetName(button, label);
            button.Click += (_, __) => action();
            return button;
        }

        static void Dress(Button button)
        {
            button.Padding = new Thickness(14, 8, 14, 8);
            button.Margin = new Thickness(0, 0, 10, 10);
            button.Cursor = Cursors.Hand;
            button.Background = Color("#243640", SystemColors.ControlBrush);
            button.Foreground = Color("#EFF4F7", SystemColors.ControlTextBrush);
            button.BorderBrush = Color("#415561", SystemColors.ActiveBorderBrush);
            AccessibilityUi.TryApplyFocusVisualStyle(button);
        }

        static FrameworkElement Section(string heading, UIElement content)
        {
            var panel = new StackPanel();
            var title = Text(heading, 16, true); title.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(title);
            panel.Children.Add(content);
            return new Border { Child = panel, Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 16),
                Background = Color("#18232B", SystemColors.WindowBrush), BorderBrush = Color("#2B3C47", SystemColors.ActiveBorderBrush),
                BorderThickness = new Thickness(1) };
        }

        static Border Chip(string text, string background) => new Border
        {
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = Color("#EFF4F7", SystemColors.WindowTextBrush) },
            Background = Color(background, SystemColors.ControlBrush), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 6)
        };

        static TextBlock Text(string value, double size, bool prominent = false) => new TextBlock
        {
            Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontWeight = prominent ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Color(prominent ? "#EFF4F7" : "#A4B7C3", SystemColors.WindowTextBrush)
        };

        static Brush Color(string hex, Brush highContrast) => SystemParameters.HighContrast ? highContrast : UiBrushHelper.FromHex(hex);
    }
}
