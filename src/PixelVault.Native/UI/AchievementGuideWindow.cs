using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed class AchievementGuideWindow : Window
    {
        sealed class GuideListItem
        {
            public AchievementGuideEntry Entry;
            public GameAchievementsFetchService.AchievementRow LiveRow;
            public FrameworkElement Visual;
            public TextBlock GuideStatus;

            public override string ToString()
            {
                return Entry?.Title ?? "Achievement";
            }
        }

        sealed class GuideListItemVisual : Grid
        {
            public string AccessibleName;
            public override string ToString() => AccessibleName ?? "Achievement";
        }

        sealed class GuideFilterOption
        {
            public string Key;
            public string Label;
            public override string ToString() => Label ?? Key ?? "All achievements";
        }

        sealed class GuideListGroup
        {
            public string Name;
            public int SortOrder;
            public List<GuideListItem> Items;
        }

        readonly IAchievementGuideService _service;
        readonly string _pixelVaultGameId;
        readonly List<GameAchievementsFetchService.AchievementRow> _liveRows;
        readonly ListBox _achievementList;
        readonly TextBox _search;
        readonly ComboBox _filter;
        readonly List<GuideListItem> _allItems = new List<GuideListItem>();
        readonly HashSet<string> _collapsedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Image _officialBadge;
        readonly TextBlock _officialTitle;
        readonly TextBlock _officialDescription;
        readonly TextBlock _progressPillText;
        readonly TextBlock _groupPillText;
        readonly Border _progressPill;
        readonly Border _groupPill;
        readonly TextBox _guideText;
        readonly TextBox _sourceUrl;
        readonly TextBox _sourceTitle;
        readonly TextBox _tags;
        readonly TextBox _groupName;
        readonly TextBox _groupSortOrder;
        readonly CheckBox _missable;
        readonly Button _save;
        readonly Button _openSource;
        readonly Button _importFile;
        readonly Button _pasteJson;
        readonly StackPanel _reader;
        readonly StackPanel _editorFields;
        readonly TextBlock _readGuide;
        readonly WrapPanel _readTags;
        readonly Button _readSource;
        readonly Button _edit;
        readonly Button _cancelEdit;
        readonly StackPanel _pinActionHost;
        AchievementGuideEntry _storedEntry;
        readonly Func<MessageBoxResult> _confirmUnsavedChanges;
        bool _isEditing;
        string _provider;
        string _providerGameId;
        GuideListItem _current;
        bool _suppressSelection;
        bool _allowClose;

        AchievementGuideWindow(
            Window owner,
            IAchievementGuideService service,
            string title,
            string pixelVaultGameId,
            IReadOnlyList<GameAchievementsFetchService.AchievementRow> rows,
            string preferredProviderAchievementId,
            Func<MessageBoxResult> confirmUnsavedChanges = null)
        {
            Owner = owner;
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _confirmUnsavedChanges = confirmUnsavedChanges ?? (() => MessageBox.Show(
                this,
                "Save changes to this achievement's guide and group details?",
                "Unsaved achievement changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question));
            _pixelVaultGameId = (pixelVaultGameId ?? string.Empty).Trim();
            _liveRows = (rows ?? Array.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null && row.HasStableProviderIdentity)
                .ToList();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = "Achievement Guide — " + (string.IsNullOrWhiteSpace(title) ? "Game" : title.Trim());
            Width = 1120;
            Height = 820;
            MinWidth = 900;
            MinHeight = 620;
            Background = UiBrushHelper.FromHex("#11181D");
            AutomationProperties.SetName(this, "Achievement Guide");

            var root = new Grid { Margin = new Thickness(22, 18, 22, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            heading.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(title) ? "Achievement Guide" : title.Trim(),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(heading);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var listColumn = new Grid();
            listColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var listBorder = new Border
            {
                Background = UiBrushHelper.FromHex("#0D1216"),
                BorderBrush = UiBrushHelper.FromHex("#27313A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12),
                Child = listColumn
            };
            body.Children.Add(listBorder);

            var listHeading = new TextBlock
            {
                Text = "ACHIEVEMENTS",
                Margin = new Thickness(2, 0, 0, 9),
                Foreground = UiBrushHelper.FromHex("#88B8A0"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold
            };
            listColumn.Children.Add(listHeading);

            _search = EditorTextBox(34, false);
            _search.Margin = new Thickness(0, 0, 0, 8);
            _search.ToolTip = "Search achievement names, IDs, guide text, descriptions, and tags (Ctrl+F).";
            AutomationProperties.SetName(_search, "Search achievement guides");
            _search.TextChanged += delegate { ApplyListFilter(); };
            Grid.SetRow(_search, 1);
            listColumn.Children.Add(_search);

            _filter = new ComboBox
            {
                Height = 34,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 3, 8, 3),
                Background = UiBrushHelper.FromHex("#151F25"),
                BorderBrush = UiBrushHelper.FromHex("#34454F"),
                Foreground = Brushes.Black,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _filter.Items.Add(new GuideFilterOption { Key = "all", Label = "All achievements" });
            _filter.Items.Add(new GuideFilterOption { Key = "guided", Label = "Guided" });
            _filter.Items.Add(new GuideFilterOption { Key = "unguided", Label = "Unguided" });
            _filter.Items.Add(new GuideFilterOption { Key = "missable", Label = "Missable" });
            _filter.Items.Add(new GuideFilterOption { Key = "locked", Label = "Locked" });
            _filter.SelectedIndex = 0;
            AutomationProperties.SetName(_filter, "Achievement guide filter");
            _filter.SelectionChanged += delegate { ApplyListFilter(); };
            Grid.SetRow(_filter, 2);
            listColumn.Children.Add(_filter);

            _achievementList = new ListBox
            {
                Background = UiBrushHelper.FromHex("#0D1216"),
                BorderBrush = UiBrushHelper.FromHex("#27313A"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Padding = new Thickness(4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_achievementList, ScrollBarVisibility.Disabled);
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 8, 9, 8)));
            _achievementList.ItemContainerStyle = itemStyle;
            AutomationProperties.SetName(_achievementList, "Achievements");
            _achievementList.SelectionChanged += AchievementSelectionChanged;
            Grid.SetRow(_achievementList, 3);
            listColumn.Children.Add(_achievementList);

            var editorBorder = new Border
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            Grid.SetColumn(editorBorder, 2);
            body.Children.Add(editorBorder);

            var editorScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            editorBorder.Child = editorScroll;
            var editor = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            editorScroll.Content = editor;

            var selectedHeader = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16),
                Background = UiBrushHelper.FromHex("#122027"),
                BorderBrush = UiBrushHelper.FromHex("#31505D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10)
            };
            var selectedHeaderGrid = new Grid();
            selectedHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selectedHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            selectedHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badgeHost = new Border
            {
                Width = 76,
                Height = 76,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Background = UiBrushHelper.FromHex("#0B1216"),
                BorderBrush = UiBrushHelper.FromHex("#3A5260"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top
            };
            _officialBadge = new Image { Stretch = Stretch.UniformToFill };
            badgeHost.Child = _officialBadge;
            selectedHeaderGrid.Children.Add(badgeHost);

            var selectedDetails = new StackPanel();
            _officialTitle = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            var titleRow = new DockPanel();
            _pinActionHost = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(_pinActionHost, Dock.Right);
            titleRow.Children.Add(_pinActionHost);
            titleRow.Children.Add(_officialTitle);
            selectedDetails.Children.Add(titleRow);

            var pills = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
            _progressPillText = new TextBlock();
            _progressPill = InfoPill(_progressPillText, "#20343A", "#3B5965");
            pills.Children.Add(_progressPill);
            _groupPillText = new TextBlock();
            _groupPill = InfoPill(_groupPillText, "#1B2B25", "#355746");
            _groupPill.Margin = new Thickness(7, 0, 0, 0);
            pills.Children.Add(_groupPill);
            selectedDetails.Children.Add(pills);

            _officialDescription = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            };
            selectedDetails.Children.Add(_officialDescription);
            Grid.SetColumn(selectedDetails, 2);
            selectedHeaderGrid.Children.Add(selectedDetails);
            selectedHeader.Child = selectedHeaderGrid;
            editor.Children.Add(selectedHeader);

            _reader = new StackPanel();
            _readGuide = new TextBlock
            {
                FontSize = 15,
                LineHeight = 24,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiBrushHelper.FromHex("#D5E3E8"),
                Margin = new Thickness(0, 10, 0, 8)
            };
            AutomationProperties.SetName(_readGuide, "Completion guide");
            _readTags = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            _readSource = ActionButton("Open Source", double.NaN);
            _readSource.HorizontalAlignment = HorizontalAlignment.Left;
            _readSource.Padding = new Thickness(10, 4, 10, 4);
            _readSource.Height = double.NaN;
            _readSource.MinHeight = 32;
            _readSource.Click += delegate { OpenCurrentSource(); };
            _reader.Children.Add(EditorSection("GUIDE", "Completion guide", _readGuide, _readTags, _readSource));
            editor.Children.Add(_reader);
            _editorFields = new StackPanel { Visibility = Visibility.Collapsed };
            editor.Children.Add(_editorFields);

            _guideText = EditorTextBox(190, true);
            AutomationProperties.SetName(_guideText, "Guide text");
            _editorFields.Children.Add(EditorSection(
                "GUIDE NOTES",
                "Completion guide",
                LabeledEditor("Notes", _guideText)));

            _sourceUrl = EditorTextBox(34, false);
            AutomationProperties.SetName(_sourceUrl, "Guide source URL");
            _sourceTitle = EditorTextBox(34, false);
            AutomationProperties.SetName(_sourceTitle, "Guide source title");
            _editorFields.Children.Add(EditorSection(
                "REFERENCE",
                "Source",
                LabeledEditor("Source URL", _sourceUrl),
                LabeledEditor("Source title", _sourceTitle)));

            _tags = EditorTextBox(34, false);
            _tags.ToolTip = "Comma-separated, for example: collectible, multiplayer, difficulty";
            AutomationProperties.SetName(_tags, "Guide tags");
            _groupName = EditorTextBox(34, false);
            _groupName.ToolTip = "Examples: Base Game, The Frozen Wilds, New Game+. Leave blank to use Base Game.";
            AutomationProperties.SetName(_groupName, "Achievement group name");
            _groupSortOrder = EditorTextBox(34, false);
            _groupSortOrder.ToolTip = "A number from 0 to 100000. Lower-numbered groups appear first.";
            AutomationProperties.SetName(_groupSortOrder, "Achievement group display order");

            _missable = new CheckBox
            {
                Content = "Missable",
                Margin = new Thickness(2, 12, 0, 0),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            AutomationProperties.SetName(_missable, "Missable achievement");

            var groupFields = new Grid();
            groupFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            groupFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            groupFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            var groupNameField = LabeledEditor("Achievement group", _groupName);
            groupFields.Children.Add(groupNameField);
            var groupOrderField = LabeledEditor("Display order", _groupSortOrder);
            Grid.SetColumn(groupOrderField, 2);
            groupFields.Children.Add(groupOrderField);
            _editorFields.Children.Add(EditorSection(
                "ORGANIZATION",
                "Catalog details",
                LabeledEditor("Tags", _tags),
                groupFields,
                _missable));

            var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftButtons = new StackPanel { Orientation = Orientation.Horizontal };
            _importFile = ActionButton("Import JSON...", 126);
            _importFile.ToolTip = "Import versioned achievement guide and grouping metadata (Ctrl+I).";
            _importFile.IsEnabled = false;
            _importFile.Click += delegate { ImportGuideJsonFile(); };
            AutomationProperties.SetName(_importFile, "Import achievement guide JSON file");
            leftButtons.Children.Add(_importFile);

            _pasteJson = ActionButton("Paste JSON", 112);
            _pasteJson.Margin = new Thickness(8, 0, 0, 0);
            _pasteJson.IsEnabled = false;
            _pasteJson.Click += delegate { ImportGuideJsonFromClipboard(); };
            AutomationProperties.SetName(_pasteJson, "Import achievement guide JSON from clipboard");
            leftButtons.Children.Add(_pasteJson);

            _openSource = ActionButton("Open Source", 118);
            _openSource.Margin = new Thickness(8, 0, 0, 0);
            _openSource.IsEnabled = false;
            _openSource.Click += delegate { OpenCurrentSource(); };
            leftButtons.Children.Add(_openSource);
            buttons.Children.Add(leftButtons);

            _save = ActionButton("Save Details", 126);
            _save.ToolTip = "Save this achievement's guide and group details (Ctrl+S).";
            _save.Background = UiBrushHelper.FromHex("#315946");
            _save.BorderBrush = UiBrushHelper.FromHex("#78A98E");
            _save.IsEnabled = false;
            _save.Click += delegate { SaveCurrent(false); };
            var editingActions = new StackPanel { Orientation = Orientation.Horizontal };
            _edit = ActionButton("Edit Guide", 110);
            _edit.Click += delegate { SetEditing(true); _guideText.Focus(); };
            AutomationProperties.SetName(_edit, "Edit achievement guide");
            _cancelEdit = ActionButton("Cancel", 90);
            _cancelEdit.Margin = new Thickness(8, 0, 0, 0);
            _cancelEdit.Click += delegate { LoadCurrent(); _edit.Focus(); };
            AutomationProperties.SetName(_cancelEdit, "Cancel guide edits");
            editingActions.Children.Add(_edit);
            editingActions.Children.Add(_save);
            editingActions.Children.Add(_cancelEdit);
            Grid.SetColumn(editingActions, 2);
            buttons.Children.Add(editingActions);

            var close = ActionButton("Close", 112);
            close.Margin = new Thickness(10, 0, 0, 0);
            close.Click += delegate { Close(); };
            Grid.SetColumn(close, 3);
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
            PreviewKeyDown += (_, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
                {
                    e.Handled = true;
                    if (_save.IsEnabled) SaveCurrent(false);
                    return;
                }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
                {
                    e.Handled = true;
                    _search.Focus();
                    _search.SelectAll();
                    return;
                }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.I)
                {
                    e.Handled = true;
                    if (_importFile.IsEnabled) ImportGuideJsonFile();
                    return;
                }
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
            };
            Closing += OnClosing;

            ReloadEntries(0, preferredProviderAchievementId);
        }

        void ReloadEntries(long preferredAchievementId = 0, string preferredProviderAchievementId = null)
        {
            var entries = _storedEntry == null
                ? _service.SyncDefinitionsAndLoadGuides(_pixelVaultGameId, _liveRows)
                : new[] { _storedEntry };
            if (_storedEntry == null) _service.ReconcileAchievementPins(_liveRows);
            var liveByKey = _liveRows
                .GroupBy(ProviderKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            _suppressSelection = true;
            _achievementList.Items.Clear();
            _allItems.Clear();
            GuideListItem preferred = null;
            foreach (var entry in entries)
            {
                liveByKey.TryGetValue(ProviderKey(entry), out var liveRow);
                var item = new GuideListItem { Entry = entry, LiveRow = liveRow };
                item.Visual = BuildGuideListItemVisual(item);
                _allItems.Add(item);
                if (IsPreferredGuideEntry(entry, preferredAchievementId, preferredProviderAchievementId)) preferred = item;
            }

            _current = preferred ?? _allItems.FirstOrDefault();
            _suppressSelection = false;
            var firstEntry = entries.FirstOrDefault();
            _provider = firstEntry?.Provider ?? string.Empty;
            _providerGameId = firstEntry?.ProviderGameId ?? string.Empty;
            var canImport = _storedEntry == null && _provider.Length > 0 && _providerGameId.Length > 0;
            _importFile.IsEnabled = canImport;
            _pasteJson.IsEnabled = canImport;

            // Prime the editor before filtering so an existing guide is not mistaken for a dirty
            // blank form on the initial load.
            if (_current != null) LoadCurrent();
            ApplyListFilter(preferredAchievementId);
        }

        void ApplyListFilter(long preferredAchievementId = 0)
        {
            if (_achievementList == null || _search == null || _filter == null) return;
            var filterKey = (_filter.SelectedItem as GuideFilterOption)?.Key ?? "all";
            var searchText = _search.Text ?? string.Empty;
            var previousCurrent = _current;
            var previousWasDirty = IsDirty();
            var keepDirty = previousWasDirty ? _current : null;
            var visible = _allItems
                .Where(item => ReferenceEquals(item, keepDirty)
                    || MatchesGuideFilter(item?.Entry, item?.LiveRow, filterKey, searchText))
                .ToList();
            var visibleGroups = BuildGuideListGroups(visible);
            var fullGroups = BuildGuideListGroups(_allItems)
                .ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
            var selectable = visibleGroups
                .Where(group => !_collapsedGroups.Contains(group.Name))
                .SelectMany(group => group.Items)
                .ToList();
            var preferred = selectable.FirstOrDefault(item => item?.Entry?.AchievementId == preferredAchievementId)
                ?? selectable.FirstOrDefault(item => ReferenceEquals(item, _current))
                ?? selectable.FirstOrDefault();

            _suppressSelection = true;
            _achievementList.Items.Clear();
            foreach (var group in visibleGroups)
            {
                var fullGroup = fullGroups.TryGetValue(group.Name, out var found) ? found : group;
                var summaryRows = fullGroup.Items
                    .Select(item => item.LiveRow ?? new GameAchievementsFetchService.AchievementRow
                    {
                        GroupName = group.Name,
                        GroupSortOrder = group.SortOrder,
                        ProgressKnown = false
                    })
                    .ToList();
                var summary = new AchievementsInfoWindow.AchievementGroupSummary
                {
                    Name = group.Name,
                    SortOrder = group.SortOrder,
                    Rows = summaryRows
                };
                var collapsed = _collapsedGroups.Contains(group.Name);
                _achievementList.Items.Add(AchievementsInfoWindow.BuildAchievementGroupHeader(
                    summary,
                    collapsed,
                    () => ToggleGuideGroup(group.Name)));
                if (collapsed) continue;
                foreach (var item in group.Items) _achievementList.Items.Add(item.Visual);
            }
            _current = preferred;
            _achievementList.SelectedItem = _current?.Visual;
            _suppressSelection = false;

            if (_current != null)
            {
                if (!(ReferenceEquals(_current, previousCurrent) && previousWasDirty)) LoadCurrent();
            }
            else
            {
                _officialTitle.Text = _allItems.Count == 0 ? "No guide-compatible achievements" : "No matching achievements";
                _officialDescription.Text = _allItems.Count == 0
                    ? "PixelVault could not assign stable provider IDs to the returned achievements."
                    : "Adjust the search or filter to show achievements.";
                _officialBadge.Source = null;
                _officialBadge.Visibility = Visibility.Collapsed;
                _progressPill.Visibility = Visibility.Collapsed;
                _groupPill.Visibility = Visibility.Collapsed;
                SetEditorEnabled(false);
            }
        }

        static List<GuideListGroup> BuildGuideListGroups(IEnumerable<GuideListItem> items)
        {
            return (items ?? Enumerable.Empty<GuideListItem>())
                .Where(item => item?.Entry != null)
                .GroupBy(item => EffectiveGuideGroupName(item.Entry), StringComparer.OrdinalIgnoreCase)
                .Select(group => new GuideListGroup
                {
                    Name = group.Key,
                    SortOrder = group.Min(item => item.Entry.GroupSortOrder),
                    Items = group.ToList()
                })
                .OrderBy(group => group.SortOrder)
                .ThenBy(group => string.Equals(group.Name, "Base Game", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static string EffectiveGuideGroupName(AchievementGuideEntry entry)
        {
            var name = (entry?.GroupName ?? string.Empty).Trim();
            return name.Length == 0 ? "Base Game" : name;
        }

        void ToggleGuideGroup(string groupName)
        {
            var name = string.IsNullOrWhiteSpace(groupName) ? "Base Game" : groupName.Trim();
            var collapsing = !_collapsedGroups.Contains(name);
            if (collapsing
                && _current?.Entry != null
                && string.Equals(EffectiveGuideGroupName(_current.Entry), name, StringComparison.OrdinalIgnoreCase)
                && !ResolveUnsavedChanges())
                return;

            if (!_collapsedGroups.Add(name)) _collapsedGroups.Remove(name);
            ApplyListFilter();
        }

        internal static bool MatchesGuideFilter(
            AchievementGuideEntry entry,
            GameAchievementsFetchService.AchievementRow liveRow,
            string filterKey,
            string searchText)
        {
            if (entry == null) return false;
            var filter = (filterKey ?? "all").Trim().ToLowerInvariant();
            var hasGuide = !string.IsNullOrWhiteSpace(entry.GuideText);
            if (filter == "guided" && !hasGuide) return false;
            if (filter == "unguided" && hasGuide) return false;
            if (filter == "missable" && !entry.IsMissable) return false;
            if (filter == "locked" && !(liveRow != null && liveRow.ProgressKnown && !liveRow.Unlocked)) return false;

            var query = (searchText ?? string.Empty).Trim();
            if (query.Length == 0) return true;
            return ContainsIgnoreCase(entry.Title, query)
                || ContainsIgnoreCase(entry.Description, query)
                || ContainsIgnoreCase(entry.ProviderAchievementId, query)
                || ContainsIgnoreCase(entry.GuideText, query)
                || ContainsIgnoreCase(entry.Tags, query)
                || ContainsIgnoreCase(entry.GroupName, query);
        }

        static bool ContainsIgnoreCase(string value, string query)
        {
            return (value ?? string.Empty).IndexOf(query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static FrameworkElement BuildGuideListItemVisual(GuideListItem item)
        {
            var root = new GuideListItemVisual
            {
                Tag = item,
                MinHeight = 40
            };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badgeHost = new Border
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 10, 0),
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Background = UiBrushHelper.FromHex("#141E24"),
                BorderBrush = UiBrushHelper.FromHex("#2B3A44"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            var badge = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            badgeHost.Child = badge;
            if (item.LiveRow != null)
                AchievementsInfoWindow.StartAchievementBadgeDownload(badge, item.LiveRow, "PixelVault");
            else
                badge.Visibility = Visibility.Collapsed;
            root.Children.Add(badgeHost);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = item.Entry?.Title ?? "Achievement",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            item.GuideStatus = new TextBlock
            {
                Text = "Guide added",
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = UiBrushHelper.FromHex("#88B8A0"),
                FontSize = 10.5
            };
            text.Children.Add(item.GuideStatus);
            Grid.SetColumn(text, 1);
            root.Children.Add(text);

            item.Visual = root;
            UpdateGuideListItemState(item);
            return root;
        }

        static void UpdateGuideListItemState(GuideListItem item)
        {
            if (item == null) return;
            var hasGuide = !string.IsNullOrWhiteSpace(item.Entry?.GuideText);
            var groupName = (item.Entry?.GroupName ?? string.Empty).Trim();
            if (item.GuideStatus != null)
            {
                item.GuideStatus.Text = string.Join(" · ", new[]
                {
                    groupName.Length > 0 ? groupName : null,
                    hasGuide ? "Guide added" : null
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                item.GuideStatus.Visibility = item.GuideStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (item.Visual != null)
            {
                var status = GuideAchievementProgressLabel(item.LiveRow);
                var group = groupName.Length > 0 ? ", group " + groupName : string.Empty;
                var guide = hasGuide ? ", guide added" : string.Empty;
                var accessibleName = (item.Entry?.Title ?? "Achievement") + ", " + status + group + guide;
                if (item.Visual is GuideListItemVisual visual) visual.AccessibleName = accessibleName;
                AutomationProperties.SetName(item.Visual, accessibleName);
            }
        }

        internal static string GuideAchievementProgressLabel(GameAchievementsFetchService.AchievementRow row)
        {
            if (row == null || !row.ProgressKnown) return "progress unknown";
            return row.Unlocked ? "unlocked" : "locked";
        }

        internal static void ShowModal(
            Window owner,
            IAchievementGuideService service,
            string title,
            string pixelVaultGameId,
            IReadOnlyList<GameAchievementsFetchService.AchievementRow> rows,
            string preferredProviderAchievementId = null)
        {
            new AchievementGuideWindow(owner, service, title, pixelVaultGameId, rows, preferredProviderAchievementId).ShowDialog();
        }

        internal static void ShowStoredModal(Window owner, IAchievementGuideService service, string title, AchievementGuideEntry entry)
        {
            // Reading a retired target must not submit a fabricated definition to provider sync.
            var window = new AchievementGuideWindow(owner, service, title, entry.PixelVaultGameId,
                Array.Empty<GameAchievementsFetchService.AchievementRow>(), null);
            window._storedEntry = entry;
            window.ReloadEntries(entry.AchievementId);
            window.ShowDialog();
        }

        internal static bool IsPreferredGuideEntry(
            AchievementGuideEntry entry,
            long preferredAchievementId,
            string preferredProviderAchievementId)
        {
            if (entry == null) return false;
            if (preferredAchievementId > 0 && entry.AchievementId == preferredAchievementId) return true;
            return preferredAchievementId <= 0
                && !string.IsNullOrWhiteSpace(preferredProviderAchievementId)
                && string.Equals(
                    entry.ProviderAchievementId,
                    preferredProviderAchievementId.Trim(),
                    StringComparison.Ordinal);
        }

        static Border InfoPill(TextBlock text, string background, string border)
        {
            text.Foreground = UiBrushHelper.FromHex("#D5E3E8");
            text.FontSize = 9.5;
            text.FontWeight = FontWeights.Bold;
            return new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                Background = UiBrushHelper.FromHex(background),
                BorderBrush = UiBrushHelper.FromHex(border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Child = text
            };
        }

        static Border EditorSection(string eyebrow, string title, params UIElement[] fields)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = eyebrow ?? string.Empty,
                Foreground = UiBrushHelper.FromHex("#88B8A0"),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            });
            content.Children.Add(new TextBlock
            {
                Text = title ?? string.Empty,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            });
            foreach (var field in fields ?? Array.Empty<UIElement>())
            {
                if (field != null) content.Children.Add(field);
            }
            return new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(16, 14, 16, 16),
                Background = UiBrushHelper.FromHex("#0D151A"),
                BorderBrush = UiBrushHelper.FromHex("#273942"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Child = content
            };
        }

        static StackPanel LabeledEditor(string label, FrameworkElement editor)
        {
            var content = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            content.Children.Add(FieldLabel(label));
            content.Children.Add(editor);
            return content;
        }

        static TextBlock FieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 0, 0, 5),
                Foreground = UiBrushHelper.FromHex("#C8D8E2"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            };
        }

        static TextBox EditorTextBox(double minHeight, bool acceptsReturn)
        {
            return new TextBox
            {
                MinHeight = minHeight,
                Padding = new Thickness(9, 7, 9, 7),
                AcceptsReturn = acceptsReturn,
                TextWrapping = acceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap,
                VerticalScrollBarVisibility = acceptsReturn ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
                Background = UiBrushHelper.FromHex("#151F25"),
                BorderBrush = UiBrushHelper.FromHex("#34454F"),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White
            };
        }

        static Button ActionButton(string text, double width)
        {
            var button = new Button
            {
                Content = text,
                Width = width,
                Height = 40,
                Foreground = Brushes.White,
                Background = UiBrushHelper.FromHex("#20343A"),
                BorderBrush = UiBrushHelper.FromHex("#C0CCD6"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold
            };
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            return button;
        }

        void AchievementSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection) return;
            var selected = (_achievementList.SelectedItem as FrameworkElement)?.Tag as GuideListItem;
            if (selected == null || ReferenceEquals(selected, _current)) return;

            if (!ResolveUnsavedChanges())
            {
                _suppressSelection = true;
                _achievementList.SelectedItem = _current?.Visual;
                _suppressSelection = false;
                return;
            }

            _current = selected;
            LoadCurrent();
        }

        void LoadCurrent()
        {
            var entry = _current?.Entry;
            if (entry == null)
            {
                SetEditorEnabled(false);
                return;
            }

            _officialTitle.Text = entry.Title ?? string.Empty;
            _officialDescription.Text = string.IsNullOrWhiteSpace(entry.Description)
                ? "No official description was returned."
                : entry.Description;
            UpdateSelectedAchievementHeader(entry, _current.LiveRow);
            _guideText.Text = entry.GuideText ?? string.Empty;
            _sourceUrl.Text = entry.SourceUrl ?? string.Empty;
            _sourceTitle.Text = entry.SourceTitle ?? string.Empty;
            _tags.Text = entry.Tags ?? string.Empty;
            _groupName.Text = entry.GroupName ?? string.Empty;
            _groupSortOrder.Text = entry.GroupSortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _missable.IsChecked = entry.IsMissable;
            SetEditorEnabled(true);
            _openSource.IsEnabled = IsHttpUrl(entry.SourceUrl);
            RenderReadView(entry);
            UpdatePinAction();
            SetEditing(false);
        }

        void UpdatePinAction()
        {
            _pinActionHost.Children.Clear();
            var entry = _current?.Entry;
            if (entry == null) return;
            try
            {
                var row = _current.LiveRow ?? AchievementPinUi.StoredRow(entry);
                var pinned = _service.LoadPinnedAchievements(entry.Provider, entry.ProviderGameId)
                    .Any(pin => AchievementPinUi.Matches(pin, row));
                if (!pinned && (!row.ProgressKnown || row.Unlocked)) return;
                var button = AchievementPinUi.Button(row, pinned, () =>
                {
                    if (AchievementPinUi.Change(this, _service, row, !pinned))
                    {
                        UpdatePinAction();
                        if (_pinActionHost.Children.Count > 0) (_pinActionHost.Children[0] as Control)?.Focus();
                    }
                });
                _pinActionHost.Children.Add(button);
            }
            catch (Exception ex)
            {
                _pinActionHost.Children.Add(new TextBlock
                {
                    Text = "Next Up unavailable", Foreground = UiBrushHelper.FromHex("#E8A598"), ToolTip = ex.Message
                });
            }
        }

        void RenderReadView(AchievementGuideEntry entry)
        {
            var hasGuide = !string.IsNullOrWhiteSpace(entry.GuideText);
            _readGuide.Text = hasGuide ? entry.GuideText : "No guide added yet.";
            _edit.Content = hasGuide ? "Edit Guide" : "Add Guide";
            AutomationProperties.SetName(_edit, hasGuide ? "Edit achievement guide" : "Add achievement guide");
            _readTags.Children.Clear();
            foreach (var tag in GuideReadTags(entry.Tags, entry.IsMissable))
            {
                _readTags.Children.Add(AchievementCollectionUi.TagChip(tag));
            }
            _readTags.Visibility = _readTags.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            var hasSource = !string.IsNullOrWhiteSpace(entry.SourceTitle) || !string.IsNullOrWhiteSpace(entry.SourceUrl);
            _readSource.Content = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.SourceTitle) ? entry.SourceUrl : entry.SourceTitle,
                TextWrapping = TextWrapping.Wrap
            };
            _readSource.ToolTip = entry.SourceUrl;
            _readSource.IsEnabled = IsHttpUrl(entry.SourceUrl);
            _readSource.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(_readSource, "Open guide source: " + (entry.SourceTitle ?? entry.SourceUrl));
            AchievementCollectionUi.ApplyHighContrast(this);
        }

        internal static List<string> GuideReadTags(string tags, bool missable)
        {
            var values = (tags ?? string.Empty).Split(',').Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missable && !values.Contains("missable", StringComparer.OrdinalIgnoreCase)) values.Add("Missable");
            return values;
        }

        void SetEditing(bool editing)
        {
            _isEditing = editing && _current != null;
            _reader.Visibility = !_isEditing && _current != null ? Visibility.Visible : Visibility.Collapsed;
            _editorFields.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
            _edit.Visibility = _isEditing ? Visibility.Collapsed : Visibility.Visible;
            _save.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
            _save.IsEnabled = _isEditing;
            _cancelEdit.Visibility = _isEditing ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateSelectedAchievementHeader(
            AchievementGuideEntry entry,
            GameAchievementsFetchService.AchievementRow liveRow)
        {
            _officialBadge.Source = null;
            if (liveRow != null)
            {
                _officialBadge.Visibility = Visibility.Visible;
                AchievementsInfoWindow.StartAchievementBadgeDownload(_officialBadge, liveRow, "PixelVault");
            }
            else
            {
                _officialBadge.Visibility = Visibility.Collapsed;
            }

            var progress = GuideAchievementProgressLabel(liveRow);
            _progressPillText.Text = progress == "unlocked"
                ? "OBTAINED"
                : progress == "locked"
                    ? "NOT OBTAINED"
                    : "PROGRESS UNKNOWN";
            _progressPill.Background = UiBrushHelper.FromHex(progress == "unlocked" ? "#1B3529" : "#20343A");
            _progressPill.BorderBrush = UiBrushHelper.FromHex(progress == "unlocked" ? "#47765C" : "#3B5965");
            _progressPill.Visibility = Visibility.Visible;

            _groupPillText.Text = EffectiveGuideGroupName(entry).ToUpperInvariant();
            _groupPill.Visibility = Visibility.Visible;
        }

        void SetEditorEnabled(bool enabled)
        {
            _guideText.IsEnabled = enabled;
            _sourceUrl.IsEnabled = enabled;
            _sourceTitle.IsEnabled = enabled;
            _tags.IsEnabled = enabled;
            _groupName.IsEnabled = enabled;
            _groupSortOrder.IsEnabled = enabled;
            _missable.IsEnabled = enabled;
            _save.IsEnabled = enabled;
            _edit.IsEnabled = enabled;
            if (!enabled) _openSource.IsEnabled = false;
            if (!enabled) _pinActionHost.Children.Clear();
            if (!enabled) SetEditing(false);
        }

        bool IsDirty()
        {
            var entry = _current?.Entry;
            if (entry == null || !_isEditing) return false;
            return !string.Equals((_guideText.Text ?? string.Empty).Trim(), entry.GuideText ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals((_sourceUrl.Text ?? string.Empty).Trim(), entry.SourceUrl ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals((_sourceTitle.Text ?? string.Empty).Trim(), entry.SourceTitle ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(AchievementGuideService.NormalizeTags(_tags.Text), entry.Tags ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals((_groupName.Text ?? string.Empty).Trim(), entry.GroupName ?? string.Empty, StringComparison.Ordinal)
                || !int.TryParse((_groupSortOrder.Text ?? string.Empty).Trim(), out var groupOrder)
                || groupOrder != entry.GroupSortOrder
                || (_missable.IsChecked == true) != entry.IsMissable;
        }

        bool ResolveUnsavedChanges()
        {
            if (!IsDirty()) return true;
            var choice = _confirmUnsavedChanges();
            if (choice == MessageBoxResult.Cancel) return false;
            if (choice == MessageBoxResult.Yes) return SaveCurrent(false);
            return true;
        }

        bool SaveCurrent(bool showConfirmation)
        {
            var entry = _current?.Entry;
            if (entry == null) return false;
            var url = (_sourceUrl.Text ?? string.Empty).Trim();
            if (url.Length > 0 && !IsHttpUrl(url))
            {
                MessageBox.Show(this, "Source URL must be a complete http:// or https:// address.", "Guide source", MessageBoxButton.OK, MessageBoxImage.Warning);
                _sourceUrl.Focus();
                return false;
            }
            if (!int.TryParse((_groupSortOrder.Text ?? string.Empty).Trim(), out var groupSortOrder)
                || groupSortOrder < 0
                || groupSortOrder > 100000)
            {
                MessageBox.Show(this, "Group display order must be a number from 0 to 100000.", "Achievement group", MessageBoxButton.OK, MessageBoxImage.Warning);
                _groupSortOrder.Focus();
                _groupSortOrder.SelectAll();
                return false;
            }

            try
            {
                _current.Entry = _service.SaveGuide(new AchievementGuideEdit
                {
                    AchievementId = entry.AchievementId,
                    GuideText = _guideText.Text,
                    SourceUrl = url,
                    SourceTitle = _sourceTitle.Text,
                    Tags = _tags.Text,
                    IsMissable = _missable.IsChecked == true,
                    GroupName = _groupName.Text,
                    GroupSortOrder = groupSortOrder
                });
                _openSource.IsEnabled = IsHttpUrl(_current.Entry.SourceUrl);
                if (_storedEntry != null) _storedEntry = _current.Entry;
                UpdateGuideListItemState(_current);
                LoadCurrent();
                ApplyListFilter(_current.Entry.AchievementId);
                if (showConfirmation)
                    MessageBox.Show(this, "Achievement details saved.", "Achievement Guide", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the guide.\n\n" + ex.Message, "Achievement Guide", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        void OpenCurrentSource()
        {
            var url = (_sourceUrl.Text ?? string.Empty).Trim();
            if (!IsHttpUrl(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the guide source.\n\n" + ex.Message, "Achievement Guide", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void ImportGuideJsonFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Achievement Guide JSON",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                ImportGuideJson(File.ReadAllText(dialog.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read the guide bundle.\n\n" + ex.Message, "Import Guide JSON", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ImportGuideJsonFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    MessageBox.Show(this, "The clipboard does not contain text.", "Import Guide JSON", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ImportGuideJson(Clipboard.GetText());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read guide JSON from the clipboard.\n\n" + ex.Message, "Import Guide JSON", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ImportGuideJson(string json)
        {
            if (!ResolveUnsavedChanges()) return;
            try
            {
                var preview = _service.PreviewGuideImport(json, _provider, _providerGameId);
                if (!preview.IsValid)
                {
                    MessageBox.Show(
                        this,
                        "The guide bundle cannot be imported:\n\n• " + string.Join("\n• ", preview.ValidationErrors),
                        "Import Guide JSON",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var summary = "Entries in bundle: " + preview.RequestedCount
                    + "\nMatched to this game: " + preview.MatchedCount
                    + "\nChanges: " + preview.ChangedCount
                    + "\nAlready up to date: " + preview.UnchangedCount
                    + "\nUnmatched: " + preview.UnmatchedAchievementIds.Count;
                if (preview.UnmatchedAchievementIds.Count > 0)
                {
                    var shown = preview.UnmatchedAchievementIds.Take(10).ToList();
                    summary += "\n\nUnmatched IDs:\n" + string.Join("\n", shown);
                    if (preview.UnmatchedAchievementIds.Count > shown.Count)
                        summary += "\n...and " + (preview.UnmatchedAchievementIds.Count - shown.Count) + " more";
                }

                if (!preview.CanImport)
                {
                    MessageBox.Show(this, summary + "\n\nNo entries can be imported.", "Import Guide JSON", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (preview.ChangedCount == 0)
                {
                    MessageBox.Show(this, summary + "\n\nNo changes are needed.", "Import Guide JSON", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(
                    this,
                    summary + "\n\nImport the matched changes?",
                    "Import Guide JSON",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                var selectedId = _current?.Entry?.AchievementId ?? 0;
                var imported = _service.ImportGuideBundle(json, _provider, _providerGameId);
                ReloadEntries(selectedId);
                MessageBox.Show(
                    this,
                    "Imported " + imported.ImportedCount + (imported.ImportedCount == 1 ? " entry." : " entries.")
                        + (imported.UnmatchedAchievementIds.Count > 0 ? "\n" + imported.UnmatchedAchievementIds.Count + " entries were unmatched." : string.Empty),
                    "Import Guide JSON",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not import the guide bundle.\n\n" + ex.Message, "Import Guide JSON", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (_allowClose) return;
            if (!ResolveUnsavedChanges())
            {
                e.Cancel = true;
                return;
            }
            _allowClose = true;
        }

        static bool IsHttpUrl(string value)
        {
            return Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        static string ProviderKey(GameAchievementsFetchService.AchievementRow row)
        {
            return (row.Provider ?? string.Empty).Trim().ToLowerInvariant() + "\u001f"
                + (row.ProviderGameId ?? string.Empty).Trim() + "\u001f"
                + (row.ProviderAchievementId ?? string.Empty).Trim();
        }

        static string ProviderKey(AchievementGuideEntry entry)
        {
            return (entry.Provider ?? string.Empty).Trim().ToLowerInvariant() + "\u001f"
                + (entry.ProviderGameId ?? string.Empty).Trim() + "\u001f"
                + (entry.ProviderAchievementId ?? string.Empty).Trim();
        }
    }
}
