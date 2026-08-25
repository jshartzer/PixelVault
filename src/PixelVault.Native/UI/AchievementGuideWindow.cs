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

            public override string ToString()
            {
                var status = LiveRow != null && LiveRow.ProgressKnown
                    ? (LiveRow.Unlocked ? "✓ " : "○ ")
                    : string.Empty;
                var guided = Entry != null && !string.IsNullOrWhiteSpace(Entry.GuideText) ? "  • Guide" : string.Empty;
                return status + (Entry?.Title ?? "Achievement") + guided;
            }
        }

        sealed class GuideFilterOption
        {
            public string Key;
            public string Label;
            public override string ToString() => Label ?? Key ?? "All achievements";
        }

        readonly IAchievementGuideService _service;
        readonly string _pixelVaultGameId;
        readonly List<GameAchievementsFetchService.AchievementRow> _liveRows;
        readonly ListBox _achievementList;
        readonly TextBox _search;
        readonly ComboBox _filter;
        readonly List<GuideListItem> _allItems = new List<GuideListItem>();
        readonly TextBlock _officialTitle;
        readonly TextBlock _officialDescription;
        readonly TextBox _guideText;
        readonly TextBox _sourceUrl;
        readonly TextBox _sourceTitle;
        readonly TextBox _tags;
        readonly CheckBox _missable;
        readonly Button _save;
        readonly Button _openSource;
        readonly Button _importFile;
        readonly Button _pasteJson;
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
            IReadOnlyList<GameAchievementsFetchService.AchievementRow> rows)
        {
            Owner = owner;
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _pixelVaultGameId = (pixelVaultGameId ?? string.Empty).Trim();
            _liveRows = (rows ?? Array.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null && row.HasStableProviderIdentity)
                .ToList();
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = "Achievement Guide — " + (string.IsNullOrWhiteSpace(title) ? "Game" : title.Trim());
            Width = 1040;
            Height = 760;
            MinWidth = 820;
            MinHeight = 560;
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
            heading.Children.Add(new TextBlock
            {
                Text = "Select an achievement, add your completion notes, and keep the original source attached.",
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 12.5,
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
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
            listColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.Children.Add(listColumn);

            _search = EditorTextBox(34, false);
            _search.Margin = new Thickness(0, 0, 0, 8);
            _search.ToolTip = "Search achievement names, IDs, guide text, descriptions, and tags (Ctrl+F).";
            AutomationProperties.SetName(_search, "Search achievement guides");
            _search.TextChanged += delegate { ApplyListFilter(); };
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
            Grid.SetRow(_filter, 1);
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
            Grid.SetRow(_achievementList, 2);
            listColumn.Children.Add(_achievementList);

            var editorBorder = new Border
            {
                Background = UiBrushHelper.FromHex("#0D1216"),
                BorderBrush = UiBrushHelper.FromHex("#27313A"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18)
            };
            Grid.SetColumn(editorBorder, 2);
            body.Children.Add(editorBorder);

            var editorScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            editorBorder.Child = editorScroll;
            var editor = new StackPanel();
            editorScroll.Content = editor;

            _officialTitle = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            editor.Children.Add(_officialTitle);
            _officialDescription = new TextBlock
            {
                Margin = new Thickness(0, 5, 0, 16),
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            };
            editor.Children.Add(_officialDescription);

            editor.Children.Add(FieldLabel("Guide"));
            _guideText = EditorTextBox(150, true);
            AutomationProperties.SetName(_guideText, "Guide text");
            editor.Children.Add(_guideText);

            editor.Children.Add(FieldLabel("Source URL"));
            _sourceUrl = EditorTextBox(34, false);
            AutomationProperties.SetName(_sourceUrl, "Guide source URL");
            editor.Children.Add(_sourceUrl);

            editor.Children.Add(FieldLabel("Source title"));
            _sourceTitle = EditorTextBox(34, false);
            AutomationProperties.SetName(_sourceTitle, "Guide source title");
            editor.Children.Add(_sourceTitle);

            editor.Children.Add(FieldLabel("Tags"));
            _tags = EditorTextBox(34, false);
            _tags.ToolTip = "Comma-separated, for example: collectible, multiplayer, difficulty";
            AutomationProperties.SetName(_tags, "Guide tags");
            editor.Children.Add(_tags);

            _missable = new CheckBox
            {
                Content = "Missable",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            AutomationProperties.SetName(_missable, "Missable achievement");
            editor.Children.Add(_missable);

            var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftButtons = new StackPanel { Orientation = Orientation.Horizontal };
            _importFile = ActionButton("Import JSON...", 126);
            _importFile.ToolTip = "Import a versioned achievement guide bundle (Ctrl+I).";
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

            _save = ActionButton("Save Guide", 126);
            _save.ToolTip = "Save this achievement guide (Ctrl+S).";
            _save.IsEnabled = false;
            _save.Click += delegate { SaveCurrent(true); };
            Grid.SetColumn(_save, 2);
            buttons.Children.Add(_save);

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

            ReloadEntries();
        }

        void ReloadEntries(long preferredAchievementId = 0)
        {
            var entries = _service.SyncDefinitionsAndLoadGuides(_pixelVaultGameId, _liveRows);
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
                _allItems.Add(item);
                if (entry.AchievementId == preferredAchievementId) preferred = item;
            }

            _current = preferred ?? _allItems.FirstOrDefault();
            _suppressSelection = false;
            var firstEntry = entries.FirstOrDefault();
            _provider = firstEntry?.Provider ?? string.Empty;
            _providerGameId = firstEntry?.ProviderGameId ?? string.Empty;
            var canImport = _provider.Length > 0 && _providerGameId.Length > 0;
            _importFile.IsEnabled = canImport;
            _pasteJson.IsEnabled = canImport;

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
            var preferred = visible.FirstOrDefault(item => item?.Entry?.AchievementId == preferredAchievementId)
                ?? visible.FirstOrDefault(item => ReferenceEquals(item, _current))
                ?? visible.FirstOrDefault();

            _suppressSelection = true;
            _achievementList.Items.Clear();
            foreach (var item in visible) _achievementList.Items.Add(item);
            _current = preferred;
            _achievementList.SelectedItem = _current;
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
                SetEditorEnabled(false);
            }
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
                || ContainsIgnoreCase(entry.Tags, query);
        }

        static bool ContainsIgnoreCase(string value, string query)
        {
            return (value ?? string.Empty).IndexOf(query ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void ShowModal(
            Window owner,
            IAchievementGuideService service,
            string title,
            string pixelVaultGameId,
            IReadOnlyList<GameAchievementsFetchService.AchievementRow> rows)
        {
            new AchievementGuideWindow(owner, service, title, pixelVaultGameId, rows).ShowDialog();
        }

        static TextBlock FieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Margin = new Thickness(0, 10, 0, 5),
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
            var selected = _achievementList.SelectedItem as GuideListItem;
            if (selected == null || ReferenceEquals(selected, _current)) return;

            if (!ResolveUnsavedChanges())
            {
                _suppressSelection = true;
                _achievementList.SelectedItem = _current;
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
            _guideText.Text = entry.GuideText ?? string.Empty;
            _sourceUrl.Text = entry.SourceUrl ?? string.Empty;
            _sourceTitle.Text = entry.SourceTitle ?? string.Empty;
            _tags.Text = entry.Tags ?? string.Empty;
            _missable.IsChecked = entry.IsMissable;
            SetEditorEnabled(true);
            _openSource.IsEnabled = IsHttpUrl(entry.SourceUrl);
        }

        void SetEditorEnabled(bool enabled)
        {
            _guideText.IsEnabled = enabled;
            _sourceUrl.IsEnabled = enabled;
            _sourceTitle.IsEnabled = enabled;
            _tags.IsEnabled = enabled;
            _missable.IsEnabled = enabled;
            _save.IsEnabled = enabled;
            if (!enabled) _openSource.IsEnabled = false;
        }

        bool IsDirty()
        {
            var entry = _current?.Entry;
            if (entry == null) return false;
            return !string.Equals((_guideText.Text ?? string.Empty).Trim(), entry.GuideText ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals((_sourceUrl.Text ?? string.Empty).Trim(), entry.SourceUrl ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals((_sourceTitle.Text ?? string.Empty).Trim(), entry.SourceTitle ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(AchievementGuideService.NormalizeTags(_tags.Text), entry.Tags ?? string.Empty, StringComparison.Ordinal)
                || (_missable.IsChecked == true) != entry.IsMissable;
        }

        bool ResolveUnsavedChanges()
        {
            if (!IsDirty()) return true;
            var choice = MessageBox.Show(
                this,
                "Save changes to this achievement guide?",
                "Unsaved guide changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
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

            try
            {
                _current.Entry = _service.SaveGuide(new AchievementGuideEdit
                {
                    AchievementId = entry.AchievementId,
                    GuideText = _guideText.Text,
                    SourceUrl = url,
                    SourceTitle = _sourceTitle.Text,
                    Tags = _tags.Text,
                    IsMissable = _missable.IsChecked == true
                });
                _openSource.IsEnabled = IsHttpUrl(_current.Entry.SourceUrl);
                ApplyListFilter(_current.Entry.AchievementId);
                if (showConfirmation)
                    MessageBox.Show(this, "Guide saved.", "Achievement Guide", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    "Imported " + imported.ImportedCount + (imported.ImportedCount == 1 ? " guide." : " guides.")
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
