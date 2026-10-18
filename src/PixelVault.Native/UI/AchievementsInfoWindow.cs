using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow : Window
    {
        internal sealed class AchievementGroupSummary
        {
            public string Name;
            public int SortOrder;
            public List<GameAchievementsFetchService.AchievementRow> Rows;
            public int Total => Rows?.Count ?? 0;
            public int Earned => Rows?.Count(row => row.ProgressKnown && row.Unlocked) ?? 0;
            public bool ProgressKnown => Rows != null && Rows.Count > 0 && Rows.All(row => row.ProgressKnown);
            public int? Percent => ProgressKnown && Total > 0
                ? (int?)Math.Round(Earned * 100d / Total, MidpointRounding.AwayFromZero)
                : null;
        }

        readonly TextBlock _heading;
        readonly TextBlock _earnedSummary;
        readonly TextBlock _detail;
        readonly TextBlock _error;
        readonly ListBox _list;
        readonly Border _gridHost;
        readonly StackPanel _gridContent;
        readonly TextBlock _empty;
        readonly Button _guide;
        readonly Button _listView;
        readonly Button _gridView;
        readonly Button _refresh;
        readonly CheckBox _remainingOnly;
        readonly TextBlock _updated;
        readonly Func<Task<GameAchievementsFetchService.FetchResult>> _fetchAchievements;
        readonly IAchievementGuideService _guideService;
        readonly LibraryFolderInfo _folder;
        readonly string _userAgent;
        readonly HashSet<string> _collapsedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        GameAchievementsFetchService.FetchResult _loadedResult;
        bool _showGrid = true;
        bool _isLoading;
        bool _isClosed;
        string _scoreSteamAccount, _scoreRetroAccount;

        AchievementsInfoWindow(
            Window owner,
            string initialHeading,
            IAchievementGuideService guideService,
            LibraryFolderInfo folder,
            string userAgent,
            Func<Task<GameAchievementsFetchService.FetchResult>> fetchAchievements)
        {
            Owner = owner;
            _guideService = guideService;
            _folder = folder;
            _userAgent = userAgent;
            _fetchAchievements = fetchAchievements;
            Closed += delegate { _isClosed = true; ClearUnlockFeedback(); };
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = "Achievements";
            Width = 960;
            Height = 800;
            MinWidth = 680;
            MinHeight = 520;
            Background = UiBrushHelper.FromHex("#11181D");

            var root = new Grid { Margin = new Thickness(22, 18, 22, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _heading = new TextBlock
            {
                Text = initialHeading ?? "Loading…",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(_heading);

            _earnedSummary = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiBrushHelper.FromHex("#C8D8E2"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_earnedSummary, 1);
            root.Children.Add(_earnedSummary);

            _detail = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 13,
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_detail, 2);
            root.Children.Add(_detail);

            var detailArea = new StackPanel();
            root.Children.Remove(_detail);
            detailArea.Children.Add(_detail);
            _updated = new TextBlock
            {
                FontSize = 11,
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed
            };
            detailArea.Children.Add(_updated);
            detailArea.Children.Add(BuildSortControls());
            detailArea.Children.Add(_nextUpHost);
            detailArea.Children.Add(_unlockNotice);
            Grid.SetRow(detailArea, 2);
            root.Children.Add(detailArea);

            _error = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 13,
                Foreground = UiBrushHelper.FromHex("#E8A598"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_error, 3);
            root.Children.Add(_error);

            _empty = new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 13,
                Foreground = UiBrushHelper.FromHex("#7D909C"),
                Text = "No achievement definitions were returned for this game.",
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_empty, 4);
            root.Children.Add(_empty);

            _list = new ListBox
            {
                Margin = new Thickness(0, 12, 0, 12),
                Background = UiBrushHelper.FromHex("#0D1216"),
                BorderBrush = UiBrushHelper.FromHex("#27313A"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            var listItemStyle = new Style(typeof(ListBoxItem));
            listItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            _list.ItemContainerStyle = listItemStyle;
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            Grid.SetRow(_list, 5);
            root.Children.Add(_list);

            _gridContent = new StackPanel { Margin = new Thickness(10, 4, 10, 12) };
            var gridScroll = new ScrollViewer
            {
                Content = _gridContent,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _gridHost = new Border
            {
                Margin = new Thickness(0, 12, 0, 12),
                Background = UiBrushHelper.FromHex("#0D1216"),
                BorderBrush = UiBrushHelper.FromHex("#27313A"),
                BorderThickness = new Thickness(1),
                Child = gridScroll,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_gridHost, 5);
            root.Children.Add(_gridHost);

            var footer = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var viewButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            viewButtons.Children.Add(new TextBlock
            {
                Text = "View",
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            _gridView = BuildViewButton("Grid", true);
            _listView = BuildViewButton("List", false);
            viewButtons.Children.Add(_gridView);
            _listView.Margin = new Thickness(6, 0, 0, 0);
            viewButtons.Children.Add(_listView);
            _refresh = new Button
            {
                Content = new TextBlock
                {
                    Text = "\uE72C",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Width = 36,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brushes.White,
                Background = UiBrushHelper.FromHex("#172127"),
                BorderBrush = UiBrushHelper.FromHex("#33434D"),
                BorderThickness = new Thickness(1),
                ToolTip = "Refresh achievement status (F5)"
            };
            _refresh.Click += async (_, __) => { await LoadAsync(); };
            AccessibilityUi.TryApplyFocusVisualStyle(_refresh);
            AutomationProperties.SetName(_refresh, "Refresh achievement status");
            viewButtons.Children.Add(_refresh);
            _remainingOnly = new CheckBox
            {
                Content = "Remaining Only",
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(0, 8, 0, 8),
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White,
                ToolTip = "Show achievements with known locked progress."
            };
            _remainingOnly.Click += delegate
            {
                if (_loadedResult != null)
                    RenderAchievementRows(_loadedResult.Rows, _userAgent, _loadedResult.SourceLabel);
            };
            AccessibilityUi.TryApplyFocusVisualStyle(_remainingOnly);
            AutomationProperties.SetName(_remainingOnly, "Show remaining achievements only");
            viewButtons.Children.Add(_remainingOnly);
            footer.Children.Add(viewButtons);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _guide = new Button
            {
                Content = "Guide",
                Width = 120,
                Height = 40,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = Brushes.White,
                Background = UiBrushHelper.FromHex("#20343A"),
                BorderBrush = UiBrushHelper.FromHex("#C0CCD6"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                IsEnabled = false
            };
            _guide.Click += delegate
            {
                OpenAchievementGuide(null, userAgent);
            };
            AccessibilityUi.TryApplyFocusVisualStyle(_guide);
            AutomationProperties.SetName(_guide, "Open achievement guides");
            buttons.Children.Add(_guide);
            var close = new Button
            {
                Content = "Close",
                Width = 120,
                Height = 40,
                Foreground = Brushes.White,
                Background = UiBrushHelper.FromHex("#20343A"),
                BorderBrush = UiBrushHelper.FromHex("#C0CCD6"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Effect = new DropShadowEffect { Color = Color.FromArgb(64, 18, 27, 36), BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.55 }
            };
            close.Click += delegate { Close(); };
            AccessibilityUi.TryApplyFocusVisualStyle(close);
            buttons.Children.Add(close);
            Grid.SetColumn(buttons, 2);
            footer.Children.Add(buttons);
            Grid.SetRow(footer, 6);
            root.Children.Add(footer);

            UpdateViewButtonAppearance();

            InputBindings.Add(new KeyBinding(NavigationCommands.Refresh, Key.F5, ModifierKeys.None));
            CommandBindings.Add(new CommandBinding(NavigationCommands.Refresh,
                async (_, __) => { await LoadAsync(); },
                (_, e) => { e.CanExecute = !_isLoading && !_isClosed; e.Handled = true; }));

            Content = root;
            AchievementCollectionUi.ApplyHighContrast(this);
        }

        Button BuildViewButton(string label, bool grid)
        {
            var button = new Button
            {
                Content = label,
                Width = 76,
                Height = 36,
                Margin = new Thickness(0),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold
            };
            button.Click += delegate
            {
                if (_showGrid == grid) return;
                _showGrid = grid;
                UpdateViewButtonAppearance();
                if (_loadedResult?.Rows != null)
                    RenderAchievementRows(_loadedResult.Rows, _userAgent, _loadedResult.SourceLabel);
            };
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            AutomationProperties.SetName(button, "Show achievements as a " + label.ToLowerInvariant());
            return button;
        }

        void UpdateViewButtonAppearance()
        {
            if (_listView == null || _gridView == null) return;
            _listView.Background = UiBrushHelper.FromHex(_showGrid ? "#172127" : "#31505D");
            _gridView.Background = UiBrushHelper.FromHex(_showGrid ? "#31505D" : "#172127");
            _listView.BorderBrush = UiBrushHelper.FromHex(_showGrid ? "#33434D" : "#7FA593");
            _gridView.BorderBrush = UiBrushHelper.FromHex(_showGrid ? "#7FA593" : "#33434D");
        }

        internal static void ShowModal(
            Window owner,
            string normalizedPlatform,
            LibraryFolderInfo folder,
            string steamKey,
            string retroKey,
            string steamUserId64,
            string retroAchievementsUsername,
            string userAgent,
            IAchievementGuideService guideService,
            bool remainingOnly = false)
        {
            var title = folder == null ? string.Empty : (folder.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title)) title = "Achievements";
            var dlg = new AchievementsInfoWindow(owner, title, guideService, folder, userAgent,
                () => GameAchievementsFetchService.FetchAsync(
                    normalizedPlatform, folder, steamKey, retroKey, steamUserId64,
                    retroAchievementsUsername, userAgent, default));
            dlg._remainingOnly.IsChecked = remainingOnly;
            dlg._scoreSteamAccount = steamUserId64 ?? "";
            dlg._scoreRetroAccount = retroAchievementsUsername ?? "";
            dlg._feedbackAccountScope = (normalizedPlatform ?? "") + "|" + (steamUserId64 ?? "") + "|" + (retroAchievementsUsername ?? "");
            AutomationProperties.SetName(dlg, "Achievements");
            dlg.Loaded += async (_, __) => { await dlg.LoadAsync(); };
            dlg.ShowDialog();
        }

        async Task LoadAsync()
        {
            if (_isLoading || _isClosed) return;
            ClearUnlockFeedback();
            _isLoading = true;
            _refresh.IsEnabled = false;
            _refresh.ToolTip = "Refreshing achievement status…";
            _guide.IsEnabled = false;
            _error.Visibility = Visibility.Collapsed;
            string scoreError = null;
            try
            {
                GameAchievementsFetchService.FetchResult result;
                try
                {
                    result = await _fetchAchievements()
                        ?? new GameAchievementsFetchService.FetchResult { ErrorMessage = "No achievement data was returned." };
                }
                catch (Exception ex)
                {
                    result = new GameAchievementsFetchService.FetchResult { ErrorMessage = ex.Message };
                }
                if (_isClosed) return;
                if ((result.IsError || !string.IsNullOrEmpty(result.ProgressErrorMessage)) && _loadedResult != null)
                {
                    _error.Text = "Could not refresh achievement status. Showing the last loaded status. " + (result.ErrorMessage ?? result.ProgressErrorMessage);
                    _error.Visibility = Visibility.Visible;
                    return;
                }
                if (!result.IsError && string.IsNullOrEmpty(result.ProgressErrorMessage)
                    && (_scoreSteamAccount != null || _scoreRetroAccount != null) && _guideService is IAchievementScoreStore scoreStore)
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            foreach (var group in (result.Rows ?? new()).Where(r => r != null).GroupBy(r => r.Provider))
                            {
                                var account = group.Key == GameAchievementsFetchService.SteamProvider ? _scoreSteamAccount : _scoreRetroAccount;
                                scoreStore.SyncAchievementScores(account, group);
                                var first = group.First();
                                if (scoreStore.LoadAchievementScoreTotals(account).TryGetValue(group.Key + ":" + first.ProviderGameId, out var totals))
                                    result.ScoreTotals = totals;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        scoreError = "Achievement points could not be saved. " + ex.Message;
                    }
                    if (_isClosed) return;
                }
                _newUnlocks = _unlockTracker.Observe(result, _feedbackAccountScope);
                ApplyLoadedResult(result);
                if (scoreError != null) { _error.Text = scoreError; _error.Visibility = Visibility.Visible; }
                ShowUnlockFeedback();
                if (!result.IsError && string.IsNullOrEmpty(result.ProgressErrorMessage))
                {
                    // An absolute label stays truthful without a timer and does not advance on failure.
                    var updated = DateTimeOffset.Now;
                    _updated.Text = "Updated " + updated.ToString("g");
                    _updated.ToolTip = "Last successful retrieval: " + updated.ToString("F") + " " + updated.ToString("zzz");
                    _updated.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                _isLoading = false;
                _refresh.IsEnabled = true;
                _refresh.ToolTip = "Refresh achievement status (F5)";
                _guide.IsEnabled = _guideService != null
                    && _loadedResult?.Rows?.Any(row => row != null && row.HasStableProviderIdentity) == true;
            }
        }

        void ApplyLoadedResult(GameAchievementsFetchService.FetchResult result)
        {
            Title = result.IsError
                ? "Achievements"
                : ("Achievements — " + (result.SourceLabel ?? ""));

            var displayTitle = result.IsError
                ? (_folder == null ? string.Empty : (_folder.Name ?? string.Empty).Trim())
                : (result.GameTitle ?? string.Empty);
            if (string.IsNullOrWhiteSpace(displayTitle) && _folder != null)
                displayTitle = _folder.Name ?? string.Empty;
            _heading.Text = string.IsNullOrWhiteSpace(displayTitle) ? "Achievements" : displayTitle;
            _detail.Text = result.IsError ? string.Empty : (result.DetailLine ?? string.Empty);
            _earnedSummary.Visibility = Visibility.Collapsed;

            if (result.IsError)
            {
                _error.Visibility = Visibility.Visible;
                _error.Text = result.ErrorMessage ?? "Unknown error.";
                _list.Visibility = Visibility.Collapsed;
                _gridHost.Visibility = Visibility.Collapsed;
                _empty.Visibility = Visibility.Collapsed;
                return;
            }

            _error.Visibility = Visibility.Collapsed;
            _loadedResult = result;
            var rows = result.Rows ?? new List<GameAchievementsFetchService.AchievementRow>();
            _guide.IsEnabled = _guideService != null && rows.Any(row => row != null && row.HasStableProviderIdentity);
            UpdateProgressDetails(rows);
            TryApplyStoredAchievementGroups(rows);
            ReloadPins(true);
            RenderAchievementRows(rows, _userAgent, result.SourceLabel);
        }

        void TryApplyStoredAchievementGroups(List<GameAchievementsFetchService.AchievementRow> rows)
        {
            if (_guideService == null || rows == null || rows.Count == 0) return;
            try
            {
                _guideService.SyncDefinitionsAndLoadGuides(_folder?.GameId ?? string.Empty, rows);
            }
            catch
            {
                // Group metadata is optional; a catalog failure must not hide live provider achievements.
            }
        }

        internal static List<GameAchievementsFetchService.AchievementRow> FilterAchievementRows(
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows,
            bool remainingOnly)
        {
            return (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null && (!remainingOnly || (row.ProgressKnown && !row.Unlocked)))
                .ToList();
        }

        internal static string BuildRemainingAchievementMessage(
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows)
        {
            var all = FilterAchievementRows(rows, false);
            if (all.Count == 0) return "No achievement definitions were returned for this game.";
            var unknown = all.Count(row => !row.ProgressKnown);
            var remaining = all.Count(row => row.ProgressKnown && !row.Unlocked);
            if (unknown > 0)
                return (remaining > 0 ? "Showing " + remaining + " known remaining. " : "No known remaining achievements. ")
                    + "Progress unavailable for " + unknown + " achievement" + (unknown == 1 ? "." : "s.");
            return remaining == 0 ? "All " + all.Count + " achievements have been obtained." : string.Empty;
        }

        static TextBlock BuildFilteredGroupMessage(IEnumerable<GameAchievementsFetchService.AchievementRow> rows)
        {
            return new TextBlock
            {
                Text = BuildRemainingAchievementMessage(rows),
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                Margin = new Thickness(8, 8, 8, 8),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
        }

        void RenderAchievementRows(
            List<GameAchievementsFetchService.AchievementRow> rows,
            string userAgent,
            string sourceLabel)
        {
            _feedbackRestores.Clear();
            _list.Items.Clear();
            _gridContent.Children.Clear();
            rows = FilterAchievementRows(rows, false);
            if (rows.Count == 0)
            {
                _empty.Text = "No achievement definitions were returned for this game.";
                _earnedSummary.Visibility = Visibility.Collapsed;
                _list.Visibility = Visibility.Collapsed;
                _gridHost.Visibility = Visibility.Collapsed;
                _empty.Visibility = Visibility.Visible;
                return;
            }

            _earnedSummary.Text = (_stats ?? AchievementCollectionStats.Build(rows, DateTime.UtcNow.Ticks, _loadedResult?.ScoreTotals)).Summary;
            _earnedSummary.Visibility = Visibility.Visible;

            _empty.Visibility = Visibility.Collapsed;
            _list.Visibility = _showGrid ? Visibility.Collapsed : Visibility.Visible;
            _gridHost.Visibility = _showGrid ? Visibility.Visible : Visibility.Collapsed;
            if (_showGrid) _gridContent.Children.Add(_progressDetails);
            else _list.Items.Add(_progressDetails);

            if (_remainingOnly.IsChecked == true)
            {
                _empty.Text = BuildRemainingAchievementMessage(rows);
                _empty.Visibility = string.IsNullOrEmpty(_empty.Text) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (_showGrid)
            {
                RenderAchievementGrid(rows, userAgent, sourceLabel);
                AchievementCollectionUi.ApplyHighContrast(this);
                HighlightUnlocks();
                return;
            }

            foreach (var group in BuildAchievementGroups(rows))
            {
                var collapsed = _collapsedGroups.Contains(group.Name);
                _list.Items.Add(BuildAchievementGroupHeader(group, collapsed, () =>
                {
                    if (!_collapsedGroups.Add(group.Name)) _collapsedGroups.Remove(group.Name);
                    RenderAchievementRows(rows, userAgent, sourceLabel);
                }));
                if (collapsed) continue;
                var visibleRows = VisibleSortedRows(group.Rows);
                if (visibleRows.Count == 0)
                    _list.Items.Add(BuildFilteredGroupMessage(group.Rows));
                var lastSection = string.Empty;
                foreach (var row in visibleRows)
                {
                    var section = !row.ProgressKnown ? "unknown" : (row.Unlocked ? "unlocked" : "locked");
                    if (SelectedSort == AchievementCollectionSort.Provider && !string.Equals(section, lastSection, StringComparison.Ordinal))
                    {
                        lastSection = section;
                        var headerText = section == "unlocked"
                            ? "Unlocked"
                            : section == "locked"
                                ? "Locked"
                                : UnknownProgressSectionTitle(sourceLabel);
                        _list.Items.Add(new TextBlock
                        {
                            Text = headerText,
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 12,
                            Foreground = UiBrushHelper.FromHex("#7D909C"),
                            Margin = new Thickness(8, section == "unlocked" ? 4 : 14, 0, 4)
                        });
                    }

                    var card = BuildAchievementRowCard(row, userAgent);
                    AddPinAction(card, row);
                    AttachGuideDoubleClick(card, row, userAgent);
                    _list.Items.Add(card);
                }
            }
            AchievementCollectionUi.ApplyHighContrast(this);
            HighlightUnlocks();
        }

        void RenderAchievementGrid(
            List<GameAchievementsFetchService.AchievementRow> rows,
            string userAgent,
            string sourceLabel)
        {
            foreach (var group in BuildAchievementGroups(rows))
            {
                var collapsed = _collapsedGroups.Contains(group.Name);
                _gridContent.Children.Add(BuildAchievementGroupHeader(group, collapsed, () =>
                {
                    if (!_collapsedGroups.Add(group.Name)) _collapsedGroups.Remove(group.Name);
                    RenderAchievementRows(rows, userAgent, sourceLabel);
                }));
                if (collapsed) continue;

                var visibleRows = VisibleSortedRows(group.Rows);
                if (visibleRows.Count == 0)
                    _gridContent.Children.Add(BuildFilteredGroupMessage(group.Rows));
                foreach (var section in VisibleSections(visibleRows))
                {
                    _gridContent.Children.Add(new TextBlock
                    {
                        Text = section.Title + " · " + section.Rows.Count,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 12,
                        Foreground = section.Key == "obtained"
                            ? UiBrushHelper.FromHex("#88B8A0")
                            : UiBrushHelper.FromHex("#9CB1BC"),
                        Margin = new Thickness(8, 14, 0, 5)
                    });

                    if (section.Rows.Count == 0)
                    {
                        _gridContent.Children.Add(new TextBlock
                        {
                            Text = group.ProgressKnown
                                ? "All achievements in this group have been obtained."
                                : "No known locked achievements in this group.",
                            Foreground = UiBrushHelper.FromHex("#88B8A0"),
                            FontSize = 12,
                            Margin = new Thickness(8, 3, 0, 8)
                        });
                        continue;
                    }

                    var panel = new WrapPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        ItemWidth = 164,
                        ItemHeight = 164
                    };
                    foreach (var row in section.Rows)
                    {
                        var card = BuildAchievementGridCard(row, userAgent);
                        AddPinAction(card, row);
                        AttachGuideDoubleClick(card, row, userAgent);
                        panel.Children.Add(card);
                    }
                    _gridContent.Children.Add(panel);
                }
            }
        }

        void AttachGuideDoubleClick(
            FrameworkElement visual,
            GameAchievementsFetchService.AchievementRow row,
            string userAgent)
        {
            if (visual == null || row == null || _guideService == null || !row.HasStableProviderIdentity) return;
            visual.Cursor = Cursors.Hand;
            visual.Focusable = true;
            visual.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "PixelVaultFocusVisual");
            visual.KeyDown += (_, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, visual) || (e.Key != Key.Enter && e.Key != Key.Space)) return;
                e.Handled = true;
                OpenAchievementGuide(row, userAgent);
            };
            const string hint = "Double-click to open this achievement in Guide.";
            visual.ToolTip = visual.ToolTip is string existing && !string.IsNullOrWhiteSpace(existing)
                ? existing + Environment.NewLine + Environment.NewLine + hint
                : hint;
            visual.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                OpenAchievementGuide(row, userAgent);
            };
        }

        void OpenAchievementGuide(GameAchievementsFetchService.AchievementRow preferredRow, string userAgent)
        {
            if (_isLoading || _isClosed) return;
            var rows = _loadedResult?.Rows;
            if (_guideService == null || rows == null || rows.Count == 0) return;
            try
            {
                AchievementGuideWindow.ShowModal(
                    this,
                    _guideService,
                    _loadedResult.GameTitle ?? _folder?.Name ?? "Game",
                    _folder?.GameId ?? string.Empty,
                    rows,
                    preferredRow?.ProviderAchievementId);
                TryApplyStoredAchievementGroups(rows);
                ReloadPins(false);
                RenderAchievementRows(rows, userAgent, _loadedResult.SourceLabel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Could not open the achievement guide.\n\n" + ex.Message,
                    "Achievement Guide",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        internal sealed class AchievementGridSection
        {
            public string Key;
            public string Title;
            public List<GameAchievementsFetchService.AchievementRow> Rows;
        }

        internal static List<AchievementGridSection> BuildAchievementGridSections(
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows)
        {
            var all = (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null)
                .ToList();
            var sections = new List<AchievementGridSection>();
            var progressKnown = all.Any(row => row.ProgressKnown);
            if (progressKnown)
            {
                sections.Add(new AchievementGridSection
                {
                    Key = "not-obtained",
                    Title = "Not obtained",
                    Rows = all.Where(row => row.ProgressKnown && !row.Unlocked).ToList()
                });
                var obtained = all.Where(row => row.ProgressKnown && row.Unlocked).ToList();
                if (obtained.Count > 0)
                {
                    sections.Add(new AchievementGridSection
                    {
                        Key = "obtained",
                        Title = "Obtained",
                        Rows = obtained
                    });
                }
            }

            var unknown = all.Where(row => !row.ProgressKnown).ToList();
            if (unknown.Count > 0)
            {
                sections.Add(new AchievementGridSection
                {
                    Key = "unknown",
                    Title = "Progress unknown",
                    Rows = unknown
                });
            }
            return sections;
        }

        internal static List<AchievementGroupSummary> BuildAchievementGroups(
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows)
        {
            return (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null)
                .GroupBy(row => EffectiveAchievementGroupName(row), StringComparer.OrdinalIgnoreCase)
                .Select(group => new AchievementGroupSummary
                {
                    Name = group.Key,
                    SortOrder = group.Min(row => row.GroupSortOrder),
                    Rows = group.ToList()
                })
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => string.Equals(group.Name, "Base Game", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string EffectiveAchievementGroupName(GameAchievementsFetchService.AchievementRow row)
        {
            var name = (row?.GroupName ?? string.Empty).Trim();
            return name.Length == 0 ? "Base Game" : name;
        }

        internal static Button BuildAchievementGroupHeader(
            AchievementGroupSummary group,
            bool collapsed,
            Action toggle)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chevron = new TextBlock
            {
                Text = collapsed ? "\u25B8" : "\u25BE",
                Width = 20,
                Margin = new Thickness(0, 0, 7, 0),
                FontSize = 14,
                Foreground = UiBrushHelper.FromHex("#9FC7B0"),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetRowSpan(chevron, 2);
            grid.Children.Add(chevron);
            var title = new TextBlock
            {
                Text = group?.Name ?? "Base Game",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);
            var progress = new TextBlock
            {
                Text = group?.Percent.HasValue == true
                    ? group.Earned + " / " + group.Total + " · " + group.Percent.Value + "%"
                    : (group?.Total ?? 0) + " achievements · progress unavailable",
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = UiBrushHelper.FromHex("#9FC7B0"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(progress, 1);
            Grid.SetRow(progress, 1);
            grid.Children.Add(progress);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var progressBar = AchievementCollectionUi.GroupProgress(group);
            Grid.SetRow(progressBar, 2);
            Grid.SetColumn(progressBar, 1);
            grid.Children.Add(progressBar);
            var button = new Button
            {
                Content = grid,
                Margin = new Thickness(0, 8, 0, 2),
                Padding = new Thickness(10, 7, 10, 7),
                Background = UiBrushHelper.FromHex("#17242B"),
                BorderBrush = UiBrushHelper.FromHex("#31505D"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Foreground = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = (collapsed ? "Expand " : "Collapse ") + (group?.Name ?? "Base Game")
            };
            button.Click += delegate { toggle?.Invoke(); };
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            AutomationProperties.SetName(
                button,
                (collapsed ? "Expand " : "Collapse ") + (group?.Name ?? "Base Game") + " achievement group");
            return button;
        }

        static string UnknownProgressSectionTitle(string sourceLabel)
        {
            if (string.Equals(sourceLabel, "RetroAchievements", StringComparison.OrdinalIgnoreCase))
                return "Progress unknown — add your RetroAchievements site username in Path Settings (under RetroAchievements API key).";
            if (string.Equals(sourceLabel, "Steam", StringComparison.OrdinalIgnoreCase))
                return "Progress unknown — add your SteamID64 in Path Settings.";
            return "Progress unknown";
        }

        internal static string FormatAchievementEarnedDateLabel(long unlockUtcTicks)
        {
            if (unlockUtcTicks <= 0) return string.Empty;
            try
            {
                return new DateTime(unlockUtcTicks, DateTimeKind.Utc)
                    .ToLocalTime()
                    .ToString("MMM d, yyyy");
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static string FormatAchievementEarnedMeta(long unlockUtcTicks)
        {
            var date = FormatAchievementEarnedDateLabel(unlockUtcTicks);
            return string.IsNullOrWhiteSpace(date) ? string.Empty : "Achieved " + date;
        }

        Border BuildAchievementGridCard(GameAchievementsFetchService.AchievementRow row, string userAgent)
        {
            var obtained = row.ProgressKnown && row.Unlocked;
            var card = new Border
            {
                Width = 154,
                Height = 154,
                Margin = new Thickness(5),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(7),
                Background = UiBrushHelper.FromHex(obtained ? "#15231E" : "#11191E"),
                BorderBrush = UiBrushHelper.FromHex(obtained ? "#3E6A54" : "#2B3A44"),
                BorderThickness = new Thickness(1),
                Tag = row,
                ToolTip = BuildAchievementGridToolTip(row)
            };

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var badgeHost = new Border
            {
                Width = 76,
                Height = 76,
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Background = UiBrushHelper.FromHex("#0D1418"),
                BorderBrush = UiBrushHelper.FromHex(obtained ? "#527A64" : "#2B3A44"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var image = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeHost.Child = image;
            StartAchievementBadgeDownload(image, row, userAgent);
            content.Children.Add(badgeHost);

            var title = new TextBlock
            {
                Text = row.Title ?? string.Empty,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(title, 1);
            content.Children.Add(title);

            var earnedDate = obtained ? FormatAchievementEarnedDateLabel(row.UnlockUtcTicks) : string.Empty;
            var state = obtained
                ? (string.IsNullOrWhiteSpace(earnedDate) ? "Obtained" : earnedDate)
                : (row.ProgressKnown ? "Not obtained" : "Progress unknown");
            var meta = new TextBlock
            {
                Text = AchievementScoring.PointText(row),
                ToolTip = AchievementScoring.Explanation(row),
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = UiBrushHelper.FromHex(obtained ? "#88B8A0" : "#7D909C"),
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(meta, 2);
            content.Children.Add(meta);

            if (SortingByCounterProgress && BuildCounterProgress(row) is FrameworkElement counter)
            {
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(counter, 3);
                content.Children.Add(counter);
                card.Height = 190;
            }

            card.Child = content;
            AchievementRarityUi.Apply(card, badgeHost, title, row);
            AutomationProperties.SetName(card, (row.Title ?? "Achievement") + ", " + state +
                (row.Points.HasValue ? ", " + AchievementScoring.Label(row) : "") +
                (AchievementRarityUi.Label(row).Length > 0 ? ", " + AchievementRarityUi.Label(row) : ""));
            return card;
        }

        internal static string BuildAchievementGridToolTip(GameAchievementsFetchService.AchievementRow row)
        {
            if (row == null) return string.Empty;
            var parts = new List<string>();
            if (row.Points.HasValue) parts.Add(AchievementScoring.Explanation(row));
            if (!string.IsNullOrWhiteSpace(row.Title)) parts.Add(row.Title.Trim());
            parts.Add(!row.ProgressKnown ? "Progress unknown" : row.Unlocked ? "Obtained" : "Not obtained");
            if (AchievementRarityUi.Label(row).Length > 0) parts.Add(AchievementRarityUi.Label(row));
            if (!string.IsNullOrWhiteSpace(row.Description)) parts.Add(row.Description.Trim());
            if (AchievementCollectionStats.CounterPercent(row).HasValue) parts.Add("Counter progress: " + CounterLabel(row));
            var earned = FormatAchievementEarnedMeta(row.UnlockUtcTicks);
            if (!string.IsNullOrWhiteSpace(earned)) parts.Add(earned);
            parts.Add(AchievementCollectionStats.ValidPercent(row.GlobalUnlockPercent)
                ? "Achieved by " + row.GlobalUnlockPercent.Value.ToString(row.GlobalUnlockPercent < .01 ? "0.########" : "0.##", System.Globalization.CultureInfo.CurrentCulture) + "% of players globally."
                : "Global unlock percentage unavailable.");
            if (!string.IsNullOrWhiteSpace(row.Meta))
            {
                var meta = string.Join(" · ", row.Meta.Split('·').Select(part => part.Trim())
                    .Where(part => part.Length > 0 && !part.EndsWith("% global unlocks", StringComparison.OrdinalIgnoreCase)));
                if (meta.Length > 0) parts.Add(meta);
            }
            return string.Join(Environment.NewLine, parts);
        }

        Border BuildAchievementRowCard(GameAchievementsFetchService.AchievementRow row, string userAgent)
        {
            var obtained = row.ProgressKnown && row.Unlocked;
            var card = new Border
            {
                Tag = row,
                BorderBrush = UiBrushHelper.FromHex(obtained ? "#3E6A54" : "#24323A"),
                BorderThickness = obtained ? new Thickness(2, 0, 0, 1) : new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 8, 10, 8),
                Background = obtained ? UiBrushHelper.FromHex("#15231E") : Brushes.Transparent,
                ToolTip = BuildAchievementGridToolTip(row)
            };
            var grid = new Grid { MinHeight = 52 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badgeHost = new Border
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Background = UiBrushHelper.FromHex("#141E24"),
                BorderBrush = UiBrushHelper.FromHex("#2B3A44"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top
            };
            var img = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badgeHost.Child = img;
            StartAchievementBadgeDownload(img, row, userAgent);
            Grid.SetColumn(badgeHost, 0);
            grid.Children.Add(badgeHost);

            var stack = new StackPanel { MinWidth = 0 };
            var title = new TextBlock
            {
                Text = row.Title ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(title);
            if (!string.IsNullOrWhiteSpace(row.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = row.Description,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (BuildCounterProgress(row) is FrameworkElement counter) stack.Children.Add(counter);
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            var earnedMeta = FormatAchievementEarnedMeta(row.UnlockUtcTicks);
            if (!string.IsNullOrWhiteSpace(row.Meta) || !string.IsNullOrWhiteSpace(earnedMeta) || row.Points.HasValue)
            {
                var metaStack = new StackPanel
                {
                    Margin = new Thickness(14, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    MaxWidth = 144
                };
                if (row.Points.HasValue) metaStack.Children.Add(AchievementCollectionUi.ScoreChip(row));
                if (!string.IsNullOrWhiteSpace(row.Meta))
                {
                    metaStack.Children.Add(new TextBlock
                    {
                        Text = row.Meta,
                        Foreground = UiBrushHelper.FromHex("#B8C9D4"),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                if (!string.IsNullOrWhiteSpace(earnedMeta))
                {
                    metaStack.Children.Add(new TextBlock
                    {
                        Text = earnedMeta,
                        Foreground = UiBrushHelper.FromHex("#88B8A0"),
                        FontSize = 11,
                        Margin = new Thickness(0, string.IsNullOrWhiteSpace(row.Meta) ? 0 : 4, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                Grid.SetColumn(metaStack, 2);
                grid.Children.Add(metaStack);
            }

            card.Child = grid;
            AchievementRarityUi.Apply(card, badgeHost, title, row, list: true);
            AutomationProperties.SetName(card, (row.Title ?? "Achievement") + ", " + (obtained ? "Obtained" : row.ProgressKnown ? "Not obtained" : "Progress unknown") +
                (row.Points.HasValue ? ", " + AchievementScoring.Label(row) : "") +
                (AchievementRarityUi.Label(row).Length > 0 ? ", " + AchievementRarityUi.Label(row) : ""));
            return card;
        }

        /// <summary>Steam CDN and similar hosts often reject anonymous <see cref="BitmapImage"/> URI downloads; use HttpClient with the same User-Agent as API fetches.</summary>
        const int AchievementIconDownloadMaxBytes = 786432;

        internal static void StartAchievementBadgeDownload(Image img, GameAchievementsFetchService.AchievementRow row, string userAgent, int decodePixelWidth = 108)
        {
            if (img == null || row == null) return;
            var grayOut = row.ProgressKnown && !row.Unlocked;
            string primary;
            string fallback = null;
            if (grayOut && !string.IsNullOrWhiteSpace(row.IconUrlGray))
            {
                primary = row.IconUrlGray;
                if (!string.IsNullOrWhiteSpace(row.IconUrlColor)
                    && !string.Equals(row.IconUrlGray, row.IconUrlColor, StringComparison.OrdinalIgnoreCase))
                    fallback = row.IconUrlColor;
            }
            else if (!string.IsNullOrWhiteSpace(row.IconUrlColor))
                primary = row.IconUrlColor;
            else
            {
                img.Visibility = Visibility.Collapsed;
                return;
            }

            img.Visibility = Visibility.Visible;
            var ua = string.IsNullOrWhiteSpace(userAgent) ? "PixelVault" : userAgent.Trim();
            _ = LoadAchievementBadgeIntoImageAsync(img, row, primary, fallback, grayOut, ua, decodePixelWidth);
        }

        static async Task LoadAchievementBadgeIntoImageAsync(
            Image img,
            GameAchievementsFetchService.AchievementRow row,
            string primaryUrl,
            string fallbackUrl,
            bool grayOut,
            string userAgent, int decodePixelWidth)
        {
            var bytes = await DownloadAchievementIconBytesAsync(primaryUrl, userAgent).ConfigureAwait(false);
            var usedFallback = false;
            if ((bytes == null || bytes.Length == 0) && !string.IsNullOrWhiteSpace(fallbackUrl))
            {
                bytes = await DownloadAchievementIconBytesAsync(fallbackUrl, userAgent).ConfigureAwait(false);
                usedFallback = bytes != null && bytes.Length > 0;
            }

            var fakeGrayscale = grayOut && (string.IsNullOrWhiteSpace(row.IconUrlGray) || usedFallback);

            if (bytes == null || bytes.Length == 0)
            {
                await img.Dispatcher.InvokeAsync(() => { img.Visibility = Visibility.Collapsed; });
                return;
            }

            var copy = bytes;
            await img.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    using (var ms = new MemoryStream(copy))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = ms;
                        bmp.DecodePixelWidth = Math.Clamp(decodePixelWidth, 64, 1024);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        if (fakeGrayscale) ApplyGrayscaleSource(img, bmp);
                        else img.Source = bmp;
                    }
                }
                catch
                {
                    img.Visibility = Visibility.Collapsed;
                }
            });
        }

        // Hub filters re-render many cards; reuse recent icon bytes and bound parallel downloads.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> AchievementIconCache = new(StringComparer.Ordinal);
        static readonly System.Threading.SemaphoreSlim AchievementIconDownloads = new System.Threading.SemaphoreSlim(8, 8);

        static async Task<byte[]> DownloadAchievementIconBytesAsync(string url, string userAgent)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            if (AchievementIconCache.TryGetValue(url, out var cached)) return cached;
            foreach (var candidate in GameAchievementsFetchService.SteamAchievementIconCandidates(url))
            {
                var bytes = await DownloadAchievementIconCandidateAsync(candidate, userAgent).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) continue;
                AchievementIconCache[url] = bytes;
                return bytes;
            }
            return null;
        }

        static async Task<byte[]> DownloadAchievementIconCandidateAsync(string url, string userAgent)
        {
            if (AchievementIconCache.TryGetValue(url, out var cached)) return cached;
            await AchievementIconDownloads.WaitAsync().ConfigureAwait(false);
            try
            {
                if (AchievementIconCache.TryGetValue(url, out cached)) return cached;
                using (var wc = new TimeoutWebClient())
                {
                    GameAchievementsFetchService.TrySetUa(wc, userAgent);
                    wc.TimeoutMilliseconds = 20000;
                    var bytes = await wc.DownloadBytesAsync(url, AchievementIconDownloadMaxBytes, default).ConfigureAwait(false);
                    if (bytes != null && bytes.Length > 0)
                    {
                        if (AchievementIconCache.Count >= 1500) AchievementIconCache.Clear();
                        AchievementIconCache[url] = bytes;
                    }
                    return bytes;
                }
            }
            catch
            {
                return null;
            }
            finally { AchievementIconDownloads.Release(); }
        }

        static void ApplyGrayscaleSource(Image img, BitmapSource colorSource)
        {
            try
            {
                var fmt = new FormatConvertedBitmap();
                fmt.BeginInit();
                fmt.Source = colorSource;
                fmt.DestinationFormat = PixelFormats.Gray8;
                fmt.EndInit();
                fmt.Freeze();
                img.Source = fmt;
            }
            catch
            {
                img.Source = colorSource;
            }
        }
    }
}
