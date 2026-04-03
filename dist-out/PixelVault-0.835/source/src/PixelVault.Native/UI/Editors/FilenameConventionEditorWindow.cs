using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    internal sealed class FilenameConventionEditorServices
    {
        public IFilenameRulesService RulesService { get; set; }
        public IFilenameParserService ParserService { get; set; }
        public Action<string> SetStatus { get; set; }
        public Action<string> Log { get; set; }
        public Action RefreshPreviewIfNeeded { get; set; }
        public Func<string, RoutedEventHandler, string, Brush, Button> CreateButton { get; set; }
        public Func<string, string> NormalizeConsoleLabel { get; set; }
        public Func<string, string> CleanTag { get; set; }
    }

    internal static class FilenameConventionEditorWindow
    {
        static SolidColorBrush B(string hex) => UiBrushHelper.FromHex(hex);

        public static void Show(Window owner, string appVersion, string libraryRoot, Action<Window> assignEditorWindow, Action<Window> clearEditorWindowIfCurrent, FilenameConventionEditorServices services)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (services.RulesService == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.RulesService));
            if (services.ParserService == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.ParserService));
            if (services.SetStatus == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.SetStatus));
            if (services.Log == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.Log));
            if (services.CreateButton == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.CreateButton));
            if (services.NormalizeConsoleLabel == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.NormalizeConsoleLabel));
            if (services.CleanTag == null) throw new ArgumentNullException(nameof(services) + "." + nameof(services.CleanTag));
            services.SetStatus("Loading filename rules");

            List<FilenameConventionRule> customRules = new List<FilenameConventionRule>();
            List<FilenameConventionRule> builtInRules = new List<FilenameConventionRule>();
            List<FilenameConventionSample> samples = new List<FilenameConventionSample>();
            FilenameConventionRule editingRule = null;
            bool editingBuiltIn = false;
            bool dirty = false;
            bool syncingEditor = false;

            var editorWindow = new Window
            {
                Title = "PixelVault " + appVersion + " Filename Rules",
                Width = 1480,
                Height = 980,
                MinWidth = 1220,
                MinHeight = 820,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = B("#F3EEE4")
            };

            Button MakeButton(string text, string background, Brush foreground, double width)
            {
                var button = services.CreateButton(text, null, background, foreground);
                button.Width = width;
                button.Height = 42;
                button.Margin = new Thickness(0, 0, 10, 0);
                return button;
            }

            void SetButtonToolTip(Button button, string text)
            {
                if (button == null) return;
                button.ToolTip = text ?? string.Empty;
            }

            TextBox MakeTextBox(bool readOnly = false, bool multiLine = false)
            {
                return new TextBox
                {
                    MinHeight = multiLine ? 74 : 34,
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderBrush = B("#C6D2DB"),
                    BorderThickness = new Thickness(1),
                    Background = readOnly ? B("#EEF2F5") : Brushes.White,
                    Foreground = B("#1B232B"),
                    TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    AcceptsReturn = multiLine,
                    VerticalScrollBarVisibility = multiLine ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                    IsReadOnly = readOnly
                };
            }

            ComboBox MakeCombo()
            {
                return new ComboBox
                {
                    MinHeight = 34,
                    Padding = new Thickness(6, 4, 6, 4),
                    BorderBrush = B("#C6D2DB"),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Foreground = B("#1B232B")
                };
            }

            FrameworkElement Labeled(string label, UIElement element, string help = null)
            {
                var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
                stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = B("#21303A"), Margin = new Thickness(0, 0, 0, 6) });
                stack.Children.Add(element);
                if (!string.IsNullOrWhiteSpace(help))
                {
                    stack.Children.Add(new TextBlock { Text = help, FontSize = 12, Foreground = B("#6A737B"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                }
                return stack;
            }

            Border Card(string title, string subtitle, Thickness margin)
            {
                var border = new Border
                {
                    Background = B("#F7FAFC"),
                    CornerRadius = new CornerRadius(16),
                    BorderBrush = B("#D7E1E8"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(18),
                    Margin = margin
                };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = B("#182126") });
                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    stack.Children.Add(new TextBlock { Text = subtitle, Margin = new Thickness(0, 6, 0, 12), Foreground = B("#6A737B"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
                }
                border.Child = stack;
                return border;
            }

            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border { Background = B("#161C20"), CornerRadius = new CornerRadius(20), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 18) };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock { Text = "Filename Rules", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            headerStack.Children.Add(new TextBlock { Text = "Use recent unmatched samples to create readable rules, inspect the shipped built-ins, and save library-specific overrides with confidence.", Margin = new Thickness(0, 10, 0, 0), FontSize = 14, Foreground = B("#B7C6C0"), TextWrapping = TextWrapping.Wrap });
            headerStack.Children.Add(new TextBlock { Text = "Readable rule examples: [appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media], [title]_[yyyy][MM][dd].[ext:image], clip_[unixms].[ext:video], or [contains:PS5].", Margin = new Thickness(0, 10, 0, 0), FontSize = 13, Foreground = B("#D8C7A4"), TextWrapping = TextWrapping.Wrap });
            header.Child = headerStack;
            root.Children.Add(header);

            var shell = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1) };
            Grid.SetRow(shell, 1);
            root.Children.Add(shell);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = body
            };

            var controlsRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            for (var i = 0; i < 5; i++) controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controlsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.Children.Add(controlsRow);

            var newRuleButton = MakeButton("New Rule", "#8A5A17", Brushes.White, 140);
            SetButtonToolTip(newRuleButton, "Create a blank custom filename rule draft.");
            controlsRow.Children.Add(newRuleButton);

            var createRuleFromSampleButton = MakeButton("Create Rule From Sample", "#275D47", Brushes.White, 220);
            SetButtonToolTip(createRuleFromSampleButton, "Turn the selected unmatched filename sample into an editable starter rule.");
            Grid.SetColumn(createRuleFromSampleButton, 1);
            controlsRow.Children.Add(createRuleFromSampleButton);

            var promoteFrequentButton = MakeButton("Promote Frequent", "#355F93", Brushes.White, 180);
            SetButtonToolTip(promoteFrequentButton, "Choose repeated unmatched samples and turn them into starter rule drafts.");
            Grid.SetColumn(promoteFrequentButton, 2);
            controlsRow.Children.Add(promoteFrequentButton);

            var disableBuiltInButton = MakeButton("Disable Built-In", "#A3473E", Brushes.White, 180);
            SetButtonToolTip(disableBuiltInButton, "Create a library-specific override that disables the selected built-in rule.");
            Grid.SetColumn(disableBuiltInButton, 3);
            controlsRow.Children.Add(disableBuiltInButton);

            var reloadButton = MakeButton("Reload", "#EEF2F5", B("#33424D"), 140);
            SetButtonToolTip(reloadButton, "Discard unsaved changes and reload filename rules from disk.");
            Grid.SetColumn(reloadButton, 4);
            controlsRow.Children.Add(reloadButton);

            var helperText = new TextBlock
            {
                Text = "Flow: samples at the top, then the rule editor, then known rules below. Select a sample to draft, or pick a rule to edit or inspect.",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = B("#5F6970"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 18, 0)
            };
            Grid.SetColumn(helperText, 5);
            controlsRow.Children.Add(helperText);

            var saveTopButton = MakeButton("Save Rules", "#275D47", Brushes.White, 170);
            SetButtonToolTip(saveTopButton, "Validate and save custom filename rules for this library.");
            Grid.SetColumn(saveTopButton, 6);
            controlsRow.Children.Add(saveTopButton);

            var closeTopButton = MakeButton("Close", "#EEF2F5", B("#33424D"), 140);
            SetButtonToolTip(closeTopButton, "Close the filename rules window.");
            closeTopButton.Margin = new Thickness(0);
            Grid.SetColumn(closeTopButton, 7);
            controlsRow.Children.Add(closeTopButton);

            var sampleCard = Card("Recent Unmatched Samples", "This is the fastest path to a good rule. Select a sample, review what PixelVault noticed, then create a draft from it.", new Thickness(0, 0, 0, 14));
            Grid.SetRow(sampleCard, 1);
            body.Children.Add(sampleCard);
            var sampleStack = (StackPanel)sampleCard.Child;

            var sampleGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                BorderThickness = new Thickness(1),
                BorderBrush = B("#D7E1E8"),
                Background = Brushes.White,
                Foreground = B("#1B232B"),
                RowHeaderWidth = 0,
                Margin = new Thickness(0, 0, 0, 12),
                MinHeight = 38,
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            sampleGrid.Columns.Add(new DataGridTextColumn { Header = "File Name", Binding = new Binding("FileName"), Width = new DataGridLength(2.6, DataGridLengthUnitType.Star) });
            sampleGrid.Columns.Add(new DataGridTextColumn { Header = "Suggested Platform", Binding = new Binding("SuggestedPlatformLabel"), Width = 150 });
            sampleGrid.Columns.Add(new DataGridTextColumn { Header = "Count", Binding = new Binding("OccurrenceCount"), Width = 80 });
            sampleGrid.Columns.Add(new DataGridTextColumn { Header = "Last Seen (UTC)", Binding = new Binding("LastSeenUtcText"), Width = 170 });
            sampleStack.Children.Add(sampleGrid);

            var sampleSummary = new TextBlock
            {
                Text = "Select a recent unmatched sample to see more detail here.",
                Foreground = B("#33424D"),
                TextWrapping = TextWrapping.Wrap
            };
            sampleStack.Children.Add(new Border { Background = B("#EEF3F7"), BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Child = sampleSummary });

            var editorCard = Card("Rule Editor", "Edit one draft at a time. Built-ins load here in read-only mode so you can understand them before overriding them.", new Thickness(0, 0, 0, 14));
            Grid.SetRow(editorCard, 2);
            body.Children.Add(editorCard);
            var editorStack = (StackPanel)editorCard.Child;
            editorCard.MinHeight = 360;
            var editorHost = new Grid();
            editorHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            editorHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            editorStack.Children.Add(editorHost);

            var editorInfo = new TextBlock { Foreground = B("#5F6970"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            editorHost.Children.Add(editorInfo);

            var editorScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MinHeight = 280
            };
            Grid.SetRow(editorScroll, 1);
            editorHost.Children.Add(editorScroll);
            var editorFields = new StackPanel();
            editorScroll.Content = editorFields;

            var nameBox = MakeTextBox();
            editorFields.Children.Add(Labeled("Rule Name", nameBox));

            var topFields = new Grid();
            topFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            topFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            var enabledBox = new CheckBox { Content = "Enabled", Foreground = B("#1B232B"), Margin = new Thickness(0, 8, 0, 0) };
            topFields.Children.Add(Labeled("Status", enabledBox));
            var priorityBox = MakeTextBox();
            var priorityField = Labeled("Priority", priorityBox);
            Grid.SetColumn(priorityField, 1);
            topFields.Children.Add(priorityField);
            var platformCombo = MakeCombo();
            foreach (var item in new[] { "Other", "Steam", "Xbox", "PS5", "PlayStation", "PC" }) platformCombo.Items.Add(item);
            var platformField = Labeled("Platform", platformCombo);
            Grid.SetColumn(platformField, 2);
            topFields.Children.Add(platformField);
            editorFields.Children.Add(topFields);

            var patternBox = MakeTextBox(multiLine: true);
            editorFields.Children.Add(Labeled("How The Filename Is Shaped", patternBox, "Use readable token rules by default. Raw regex still works, but token rules are easier to recognize and maintain."));

            var midFields = new Grid();
            midFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            midFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tagsBox = MakeTextBox();
            midFields.Children.Add(Labeled("Tags", tagsBox, "Semicolon-separated tags such as Steam;PlayStation;Nintendo."));
            var missingAppIdCombo = MakeCombo();
            missingAppIdCombo.Items.Add("Do nothing");
            missingAppIdCombo.Items.Add("Send to manual intake");
            var missingField = Labeled("When AppID Is Missing", missingAppIdCombo);
            Grid.SetColumn(missingField, 1);
            midFields.Children.Add(missingField);
            editorFields.Children.Add(midFields);

            var preserveFileTimesBox = new CheckBox { Content = "Preserve file time", Foreground = B("#1B232B"), Margin = new Thickness(0, 8, 0, 0) };
            editorFields.Children.Add(Labeled("File Time Behavior", preserveFileTimesBox));

            var advanced = new Expander { Header = "Advanced Parser Fields", IsExpanded = false, Margin = new Thickness(0, 4, 0, 0) };
            var advancedStack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            advanced.Content = advancedStack;
            var conventionIdBox = MakeTextBox();
            advancedStack.Children.Add(Labeled("Convention Id", conventionIdBox));
            var advancedGroups = new Grid();
            advancedGroups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            advancedGroups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            advancedGroups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var appIdGroupBox = MakeTextBox();
            advancedGroups.Children.Add(Labeled("AppID Group", appIdGroupBox));
            var titleGroupBox = MakeTextBox();
            var titleGroupField = Labeled("Title Group", titleGroupBox);
            Grid.SetColumn(titleGroupField, 1);
            advancedGroups.Children.Add(titleGroupField);
            var timestampGroupBox = MakeTextBox();
            var timestampGroupField = Labeled("Timestamp Group", timestampGroupBox);
            Grid.SetColumn(timestampGroupField, 2);
            advancedGroups.Children.Add(timestampGroupField);
            advancedStack.Children.Add(advancedGroups);
            var timestampFormatBox = MakeTextBox();
            advancedStack.Children.Add(Labeled("Timestamp Format", timestampFormatBox, "Examples: yyyyMMddHHmmss, yyyy-MM-dd, unix-ms"));
            var regexPreviewBox = MakeTextBox(readOnly: true, multiLine: true);
            advancedStack.Children.Add(Labeled("Raw Regex Preview", regexPreviewBox, "This is generated from the readable rule pattern. It is read-only."));
            editorFields.Children.Add(advanced);

            var knownRulesGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            knownRulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            knownRulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(knownRulesGrid, 3);
            body.Children.Add(knownRulesGrid);

            var customCard = Card("Custom Rules", "Saved for this library. Click one to load it into the editor.", new Thickness(0, 0, 8, 0));
            knownRulesGrid.Children.Add(customCard);
            var customStack = (StackPanel)customCard.Child;
            customCard.MinHeight = 240;
            var customGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                BorderThickness = new Thickness(1),
                BorderBrush = B("#D7E1E8"),
                Background = Brushes.White,
                Foreground = B("#1B232B"),
                RowHeaderWidth = 0,
                Height = 220,
                MinHeight = 220,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            customGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            customGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = new Binding("Enabled"), Width = 56 });
            customGrid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Binding("Priority"), Width = 80 });
            customGrid.Columns.Add(new DataGridTextColumn { Header = "Platform", Binding = new Binding("PlatformLabel"), Width = 110 });
            customGrid.Columns.Add(new DataGridTextColumn { Header = "Pattern Summary", Binding = new Binding("PatternText"), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
            customStack.Children.Add(customGrid);

            var builtInCard = Card("Built-In Rules", "Shipped defaults. Select one to inspect it, then disable it with a custom override if needed.", new Thickness(8, 0, 0, 0));
            Grid.SetColumn(builtInCard, 1);
            knownRulesGrid.Children.Add(builtInCard);
            var builtInStack = (StackPanel)builtInCard.Child;
            builtInCard.MinHeight = 240;
            var builtInGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                BorderThickness = new Thickness(1),
                BorderBrush = B("#D7E1E8"),
                Background = Brushes.White,
                Foreground = B("#1B232B"),
                RowHeaderWidth = 0,
                Height = 220,
                MinHeight = 220,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
            builtInGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = new Binding("Enabled"), Width = 56 });
            builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Binding("Priority"), Width = 80 });
            builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Platform", Binding = new Binding("PlatformLabel"), Width = 110 });
            builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Pattern Summary", Binding = new Binding("PatternText"), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
            builtInStack.Children.Add(builtInGrid);

            var footerGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(footerGrid, 4);
            body.Children.Add(footerGrid);

            var statusText = new TextBlock { Foreground = B("#5F6970"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            footerGrid.Children.Add(statusText);

            editorWindow.Content = root;
            assignEditorWindow(editorWindow);

            void ApplyLoadedState(FilenameRulesEditorState state)
            {
                var snapshot = state ?? new FilenameRulesEditorState();
                customRules = snapshot.CustomRules ?? new List<FilenameConventionRule>();
                builtInRules = snapshot.BuiltInRules ?? new List<FilenameConventionRule>();
                samples = snapshot.Samples ?? new List<FilenameConventionSample>();
            }

            void SetEditorEnabled(bool editable)
            {
                nameBox.IsReadOnly = !editable;
                priorityBox.IsReadOnly = !editable;
                patternBox.IsReadOnly = !editable;
                tagsBox.IsReadOnly = !editable;
                conventionIdBox.IsReadOnly = !editable;
                appIdGroupBox.IsReadOnly = !editable;
                titleGroupBox.IsReadOnly = !editable;
                timestampGroupBox.IsReadOnly = !editable;
                timestampFormatBox.IsReadOnly = !editable;
                platformCombo.IsEnabled = editable;
                missingAppIdCombo.IsEnabled = editable;
                enabledBox.IsEnabled = editable;
                preserveFileTimesBox.IsEnabled = editable;

                var background = editable ? Brushes.White : B("#EEF2F5");
                nameBox.Background = background;
                priorityBox.Background = background;
                patternBox.Background = background;
                tagsBox.Background = background;
                conventionIdBox.Background = background;
                appIdGroupBox.Background = background;
                titleGroupBox.Background = background;
                timestampGroupBox.Background = background;
                timestampFormatBox.Background = background;
                platformCombo.Background = background;
                missingAppIdCombo.Background = background;
            }

            void UpdateRegexPreview()
            {
                if (syncingEditor) return;
                try
                {
                    var patternText = FilenameParserService.NormalizePatternTextForStorage(patternBox.Text);
                    regexPreviewBox.Text = string.IsNullOrWhiteSpace(patternText)
                        ? string.Empty
                        : FilenameParserService.BuildRegexPattern(patternText, services.CleanTag(timestampGroupBox.Text));
                }
                catch (Exception ex)
                {
                    regexPreviewBox.Text = "Invalid pattern: " + ex.Message;
                }
            }

            void LoadRuleIntoEditor(FilenameConventionRule rule, bool readOnly)
            {
                syncingEditor = true;
                try
                {
                    editingRule = rule;
                    editingBuiltIn = readOnly;
                    if (rule == null)
                    {
                        editorInfo.Text = "Select an unmatched sample to create a draft, or choose a known rule to inspect it here.";
                        nameBox.Text = string.Empty;
                        priorityBox.Text = string.Empty;
                        patternBox.Text = string.Empty;
                        tagsBox.Text = string.Empty;
                        conventionIdBox.Text = string.Empty;
                        appIdGroupBox.Text = string.Empty;
                        titleGroupBox.Text = string.Empty;
                        timestampGroupBox.Text = string.Empty;
                        timestampFormatBox.Text = string.Empty;
                        regexPreviewBox.Text = string.Empty;
                        platformCombo.SelectedItem = "Other";
                        missingAppIdCombo.SelectedIndex = 0;
                        enabledBox.IsChecked = true;
                        preserveFileTimesBox.IsChecked = false;
                        SetEditorEnabled(false);
                        return;
                    }

                    editorInfo.Text = readOnly
                        ? "You are viewing a built-in rule. Use Disable Built-In if you want a library-specific override."
                        : "Editing a custom library rule. Changes stay local until you save them.";
                    nameBox.Text = rule.Name ?? string.Empty;
                    priorityBox.Text = rule.Priority.ToString();
                    patternBox.Text = FilenameParserService.GetPatternEditorText(rule.PatternText ?? rule.Pattern);
                    tagsBox.Text = rule.PlatformTagsText ?? string.Empty;
                    conventionIdBox.Text = rule.ConventionId ?? string.Empty;
                    appIdGroupBox.Text = rule.SteamAppIdGroup ?? string.Empty;
                    titleGroupBox.Text = rule.TitleGroup ?? string.Empty;
                    timestampGroupBox.Text = rule.TimestampGroup ?? string.Empty;
                    timestampFormatBox.Text = rule.TimestampFormat ?? string.Empty;
                    platformCombo.SelectedItem = services.NormalizeConsoleLabel(string.IsNullOrWhiteSpace(rule.PlatformLabel) ? "Other" : rule.PlatformLabel);
                    missingAppIdCombo.SelectedIndex = rule.RoutesToManualWhenMissingSteamAppId ? 1 : 0;
                    enabledBox.IsChecked = rule.Enabled;
                    preserveFileTimesBox.IsChecked = rule.PreserveFileTimes;
                    SetEditorEnabled(!readOnly);
                }
                finally
                {
                    syncingEditor = false;
                }
                UpdateRegexPreview();
            }

            void UpdateSampleSummary()
            {
                var sample = sampleGrid.SelectedItem as FilenameConventionSample;
                if (sample == null)
                {
                    sampleSummary.Text = "Select a recent unmatched sample to see more detail here.";
                    return;
                }
                sampleSummary.Text = "Selected sample: " + (sample.FileName ?? string.Empty)
                    + Environment.NewLine + "Suggested platform: " + (string.IsNullOrWhiteSpace(sample.SuggestedPlatformLabel) ? "Other" : sample.SuggestedPlatformLabel)
                    + " | Count: " + sample.OccurrenceCount
                    + " | Last seen: " + (sample.LastSeenUtcText ?? string.Empty) + " UTC"
                    + Environment.NewLine + "Next step: Create Rule From Sample to open a draft in the editor.";
            }

            string RuleSelectionKey(FilenameConventionRule rule)
            {
                return rule == null ? string.Empty : ((rule.ConventionId ?? string.Empty) + "|" + (rule.Name ?? string.Empty));
            }

            void UpdateActionState()
            {
                createRuleFromSampleButton.IsEnabled = sampleGrid.SelectedItem is FilenameConventionSample;
                disableBuiltInButton.IsEnabled = builtInGrid.SelectedItem is FilenameConventionRule;
                promoteFrequentButton.IsEnabled = samples.Any(sample => sample != null && sample.OccurrenceCount >= 2);
                saveTopButton.IsEnabled = !editingBuiltIn && editingRule != null;
            }

            void DismissSamples(IEnumerable<FilenameConventionSample> handledSamples)
            {
                var ids = (handledSamples ?? Enumerable.Empty<FilenameConventionSample>())
                    .Where(sample => sample != null && sample.SampleId > 0)
                    .Select(sample => sample.SampleId)
                    .Distinct()
                    .ToList();

                if (ids.Count == 0) return;
                services.RulesService.DismissSamples(libraryRoot, ids);
                samples = samples.Where(sample => sample == null || !ids.Contains(sample.SampleId)).ToList();
            }

            void RefreshLists(bool reloadSources = true)
            {
                var selectedSampleId = (sampleGrid.SelectedItem as FilenameConventionSample)?.SampleId ?? -1L;
                var selectedCustomKey = RuleSelectionKey(customGrid.SelectedItem as FilenameConventionRule);
                var selectedBuiltInKey = RuleSelectionKey(builtInGrid.SelectedItem as FilenameConventionRule);

                if (reloadSources)
                {
                    customGrid.ItemsSource = null;
                    customGrid.ItemsSource = customRules;
                    builtInGrid.ItemsSource = null;
                    builtInGrid.ItemsSource = builtInRules;
                    sampleGrid.ItemsSource = null;
                    sampleGrid.ItemsSource = samples;

                    if (selectedSampleId > 0)
                    {
                        var selectedSample = samples.FirstOrDefault(sample => sample != null && sample.SampleId == selectedSampleId);
                        if (selectedSample != null) sampleGrid.SelectedItem = selectedSample;
                    }

                    if (!string.IsNullOrWhiteSpace(selectedCustomKey))
                    {
                        var selectedCustom = customRules.FirstOrDefault(rule => string.Equals(RuleSelectionKey(rule), selectedCustomKey, StringComparison.OrdinalIgnoreCase));
                        if (selectedCustom != null) customGrid.SelectedItem = selectedCustom;
                    }

                    if (!string.IsNullOrWhiteSpace(selectedBuiltInKey))
                    {
                        var selectedBuiltIn = builtInRules.FirstOrDefault(rule => string.Equals(RuleSelectionKey(rule), selectedBuiltInKey, StringComparison.OrdinalIgnoreCase));
                        if (selectedBuiltIn != null) builtInGrid.SelectedItem = selectedBuiltIn;
                    }
                }

                var current = editingRule == null ? "No rule selected" : ((editingBuiltIn ? "Viewing built-in: " : "Editing custom: ") + (editingRule.Name ?? editingRule.ConventionId ?? "rule"));
                statusText.Text = customRules.Count + " custom | " + builtInRules.Count + " built-in | " + samples.Count + " sample(s) | " + (dirty ? "Unsaved changes" : "Saved") + " | " + current;
                UpdateActionState();
                UpdateSampleSummary();
            }

            void MarkDirty()
            {
                if (syncingEditor || editingBuiltIn || editingRule == null) return;
                dirty = true;
                RefreshLists(false);
            }

            void PushEditorToRule()
            {
                if (syncingEditor || editingBuiltIn || editingRule == null) return;
                editingRule.Name = services.CleanTag(nameBox.Text);
                editingRule.Enabled = enabledBox.IsChecked != false;
                int parsedPriority;
                editingRule.Priority = int.TryParse(priorityBox.Text, out parsedPriority) ? parsedPriority : 0;
                editingRule.PatternText = FilenameParserService.NormalizePatternTextForStorage(patternBox.Text);
                editingRule.Pattern = editingRule.PatternText;
                editingRule.PlatformLabel = services.NormalizeConsoleLabel(platformCombo.SelectedItem as string);
                editingRule.PlatformTagsText = services.CleanTag(tagsBox.Text);
                editingRule.ConventionId = services.CleanTag(conventionIdBox.Text);
                editingRule.SteamAppIdGroup = services.CleanTag(appIdGroupBox.Text);
                editingRule.TitleGroup = services.CleanTag(titleGroupBox.Text);
                editingRule.TimestampGroup = services.CleanTag(timestampGroupBox.Text);
                editingRule.TimestampFormat = services.CleanTag(timestampFormatBox.Text);
                editingRule.PreserveFileTimes = preserveFileTimesBox.IsChecked == true;
                editingRule.RoutesToManualWhenMissingSteamAppId = missingAppIdCombo.SelectedIndex == 1;
                editingRule.ConfidenceLabel = services.CleanTag(string.IsNullOrWhiteSpace(editingRule.ConfidenceLabel) ? "CustomRule" : editingRule.ConfidenceLabel);
                editingRule.IsBuiltIn = false;
                UpdateRegexPreview();
                MarkDirty();
            }

            void SelectCustomRule(FilenameConventionRule rule, bool focusPattern)
            {
                if (rule == null) return;
                builtInGrid.SelectedItem = null;
                customGrid.SelectedItem = rule;
                customGrid.ScrollIntoView(rule);
                LoadRuleIntoEditor(rule, false);
                RefreshLists();
                editorWindow.Dispatcher.BeginInvoke(new Action(delegate { if (focusPattern) patternBox.Focus(); else nameBox.Focus(); }), DispatcherPriority.Background);
            }

            void CreateRuleFromSample(FilenameConventionSample sample)
            {
                if (sample == null)
                {
                    MessageBox.Show("Select a recent unmatched sample first.", "PixelVault");
                    return;
                }
                var candidate = services.RulesService.CreateRuleFromSample(sample);
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.PatternText ?? candidate.Pattern))
                {
                    MessageBox.Show("Could not turn the selected sample into a starter rule.", "PixelVault");
                    return;
                }

                customRules.Insert(0, candidate);
                DismissSamples(new[] { sample });
                dirty = true;
                RefreshLists(true);
                SelectCustomRule(candidate, true);
                services.SetStatus("Created starter rule from " + (sample.FileName ?? "sample"));
            }

            FilenameConventionSample[] ShowPromoteChooser()
            {
                var frequent = samples.Where(sample => sample != null && sample.OccurrenceCount >= 2).OrderByDescending(sample => sample.OccurrenceCount).ThenByDescending(sample => sample.LastSeenUtcTicks).Take(12).ToList();
                if (frequent.Count == 0) return new FilenameConventionSample[0];

                var chooser = new Window
                {
                    Title = "Promote Frequent Samples",
                    Width = 720,
                    Height = 520,
                    MinWidth = 620,
                    MinHeight = 400,
                    Owner = editorWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = B("#F3EEE4")
                };

                var chooserRoot = new Grid { Margin = new Thickness(20) };
                chooserRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                chooserRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                chooserRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                chooser.Content = chooserRoot;

                chooserRoot.Children.Add(new TextBlock { Text = "Choose the repeated unmatched samples you want to turn into starter drafts.", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("#182126"), Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
                var chooserScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Grid.SetRow(chooserScroll, 1);
                chooserRoot.Children.Add(chooserScroll);
                var chooserStack = new StackPanel();
                chooserScroll.Content = chooserStack;

                var selections = new List<Tuple<FilenameConventionSample, CheckBox>>();
                foreach (var sample in frequent)
                {
                    var row = new Border { Background = Brushes.White, BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var check = new CheckBox { IsChecked = true, Margin = new Thickness(0, 2, 12, 0) };
                    rowGrid.Children.Add(check);
                    var detail = new StackPanel();
                    detail.Children.Add(new TextBlock { Text = sample.FileName ?? string.Empty, FontWeight = FontWeights.SemiBold, Foreground = B("#182126"), TextWrapping = TextWrapping.Wrap });
                    detail.Children.Add(new TextBlock { Text = "Platform: " + (string.IsNullOrWhiteSpace(sample.SuggestedPlatformLabel) ? "Other" : sample.SuggestedPlatformLabel) + " | Count: " + sample.OccurrenceCount + " | Last seen: " + sample.LastSeenUtcText + " UTC", Foreground = B("#5F6970"), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
                    Grid.SetColumn(detail, 1);
                    rowGrid.Children.Add(detail);
                    row.Child = rowGrid;
                    chooserStack.Children.Add(row);
                    selections.Add(Tuple.Create(sample, check));
                }

                var chooserFooter = new Grid { Margin = new Thickness(0, 14, 0, 0) };
                chooserFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                chooserFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                chooserFooter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(chooserFooter, 2);
                chooserRoot.Children.Add(chooserFooter);
                chooserFooter.Children.Add(new TextBlock { Text = "One starter rule will be created for each selected sample.", Foreground = B("#5F6970"), VerticalAlignment = VerticalAlignment.Center });
                var chooserCancel = MakeButton("Cancel", "#EEF2F5", B("#33424D"), 140);
                Grid.SetColumn(chooserCancel, 1);
                chooserFooter.Children.Add(chooserCancel);
                var chooserPromote = MakeButton("Promote", "#275D47", Brushes.White, 160);
                chooserPromote.Margin = new Thickness(0);
                Grid.SetColumn(chooserPromote, 2);
                chooserFooter.Children.Add(chooserPromote);

                List<FilenameConventionSample> chosen = null;
                chooserCancel.Click += delegate { chooser.DialogResult = false; chooser.Close(); };
                chooserPromote.Click += delegate
                {
                    chosen = selections.Where(item => item.Item2.IsChecked == true).Select(item => item.Item1).ToList();
                    chooser.DialogResult = true;
                    chooser.Close();
                };

                return chooser.ShowDialog() == true && chosen != null ? chosen.ToArray() : new FilenameConventionSample[0];
            }

            void LoadState()
            {
                ApplyLoadedState(services.RulesService.LoadState(libraryRoot));
            }

            void SaveRules()
            {
                try
                {
                    PushEditorToRule();
                    var preserveKey = editingRule != null ? RuleSelectionKey(editingRule) : null;
                    var preserveBuiltIn = editingBuiltIn;
                    ApplyLoadedState(services.RulesService.SaveRules(libraryRoot, customRules));
                    dirty = false;
                    RefreshLists(true);
                    if (!string.IsNullOrWhiteSpace(preserveKey))
                    {
                        if (preserveBuiltIn)
                        {
                            var builtIn = builtInRules.FirstOrDefault(rule => string.Equals(RuleSelectionKey(rule), preserveKey, StringComparison.OrdinalIgnoreCase));
                            if (builtIn != null)
                            {
                                customGrid.SelectedItem = null;
                                builtInGrid.SelectedItem = builtIn;
                                builtInGrid.ScrollIntoView(builtIn);
                                LoadRuleIntoEditor(builtIn, true);
                            }
                            else
                            {
                                customGrid.SelectedItem = null;
                                builtInGrid.SelectedItem = null;
                                LoadRuleIntoEditor(null, false);
                            }
                        }
                        else
                        {
                            var custom = customRules.FirstOrDefault(rule => string.Equals(RuleSelectionKey(rule), preserveKey, StringComparison.OrdinalIgnoreCase));
                            if (custom != null)
                            {
                                builtInGrid.SelectedItem = null;
                                customGrid.SelectedItem = custom;
                                customGrid.ScrollIntoView(custom);
                                LoadRuleIntoEditor(custom, false);
                            }
                            else
                            {
                                customGrid.SelectedItem = null;
                                builtInGrid.SelectedItem = null;
                                LoadRuleIntoEditor(null, false);
                            }
                        }
                    }
                    else
                    {
                        customGrid.SelectedItem = null;
                        builtInGrid.SelectedItem = null;
                        LoadRuleIntoEditor(null, false);
                    }
                    RefreshLists(false);

                    services.RefreshPreviewIfNeeded();
                    services.SetStatus("Filename rules saved.");
                    services.Log("Saved " + customRules.Count + " custom filename rule(s) to the index database.");
                }
                catch (Exception saveEx)
                {
                    services.SetStatus("Filename rule save failed");
                    services.Log("Failed to save filename rules. " + saveEx.Message);
                    MessageBox.Show("Could not save the filename rules." + Environment.NewLine + Environment.NewLine + saveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            void ReloadState()
            {
                if (dirty)
                {
                    var choice = MessageBox.Show("Discard unsaved filename-rule edits and reload from disk?", "Reload Filename Rules", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (choice != MessageBoxResult.OK) return;
                }

                services.ParserService.InvalidateRules(libraryRoot);
                ApplyLoadedState(services.RulesService.LoadState(libraryRoot));
                dirty = false;
                LoadRuleIntoEditor(null, false);
                if (samples.Count > 0) sampleGrid.SelectedItem = samples[0];
                RefreshLists(true);
                services.SetStatus("Filename rules reloaded");
                services.Log("Reloaded filename rules and recent samples from the index database.");
            }

            foreach (var textBox in new[] { nameBox, priorityBox, patternBox, tagsBox, conventionIdBox, appIdGroupBox, titleGroupBox, timestampGroupBox, timestampFormatBox })
            {
                textBox.TextChanged += delegate { PushEditorToRule(); };
            }
            platformCombo.SelectionChanged += delegate { PushEditorToRule(); };
            missingAppIdCombo.SelectionChanged += delegate { PushEditorToRule(); };
            enabledBox.Checked += delegate { PushEditorToRule(); };
            enabledBox.Unchecked += delegate { PushEditorToRule(); };
            preserveFileTimesBox.Checked += delegate { PushEditorToRule(); };
            preserveFileTimesBox.Unchecked += delegate { PushEditorToRule(); };

            newRuleButton.Click += delegate
            {
                var rule = services.RulesService.CreateNewRule();
                customRules.Insert(0, rule);
                dirty = true;
                RefreshLists(true);
                SelectCustomRule(rule, false);
                services.SetStatus("New rule draft created");
            };
            createRuleFromSampleButton.Click += delegate { CreateRuleFromSample(sampleGrid.SelectedItem as FilenameConventionSample); };
            promoteFrequentButton.Click += delegate
            {
                var chosen = ShowPromoteChooser();
                if (chosen.Length == 0)
                {
                    if (!samples.Any(sample => sample != null && sample.OccurrenceCount >= 2)) MessageBox.Show("There are no repeated unmatched samples yet. Select a sample and use Create Rule From Sample instead.", "PixelVault");
                    return;
                }
                foreach (var sample in chosen) CreateRuleFromSample(sample);
                services.SetStatus("Created " + chosen.Length + " starter rule(s) from repeated samples");
            };
            disableBuiltInButton.Click += delegate
            {
                var builtInRule = builtInGrid.SelectedItem as FilenameConventionRule;
                if (builtInRule == null)
                {
                    MessageBox.Show("Select a built-in rule first.", "PixelVault");
                    return;
                }

                var overrideRule = services.RulesService.EnsureDisabledOverride(builtInRule, customRules);

                dirty = true;
                RefreshLists(true);
                SelectCustomRule(overrideRule, false);
                MessageBox.Show("This does not delete the built-in rule. PixelVault saved a custom override for this library and marked it disabled. Save the rules to make that override active.", "PixelVault");
            };
            reloadButton.Click += delegate { ReloadState(); };
            saveTopButton.Click += delegate { SaveRules(); };
            closeTopButton.Click += delegate { editorWindow.Close(); };
            sampleGrid.SelectionChanged += delegate
            {
                UpdateSampleSummary();
                UpdateActionState();
            };
            sampleGrid.MouseDoubleClick += delegate
            {
                var sample = sampleGrid.SelectedItem as FilenameConventionSample;
                if (sample != null) CreateRuleFromSample(sample);
            };
            customGrid.SelectionChanged += delegate
            {
                var selected = customGrid.SelectedItem as FilenameConventionRule;
                if (selected != null && !ReferenceEquals(editingRule, selected))
                {
                    if (builtInGrid.SelectedItem != null) builtInGrid.SelectedItem = null;
                    LoadRuleIntoEditor(selected, false);
                }
                RefreshLists(false);
            };
            customGrid.MouseDoubleClick += delegate
            {
                var selected = customGrid.SelectedItem as FilenameConventionRule;
                if (selected != null) SelectCustomRule(selected, true);
            };
            builtInGrid.SelectionChanged += delegate
            {
                var selected = builtInGrid.SelectedItem as FilenameConventionRule;
                if (selected != null && (!ReferenceEquals(editingRule, selected) || !editingBuiltIn))
                {
                    if (customGrid.SelectedItem != null) customGrid.SelectedItem = null;
                    LoadRuleIntoEditor(selected, true);
                }
                RefreshLists(false);
            };

            editorWindow.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs args)
            {
                if (!dirty) return;
                var choice = MessageBox.Show("You have unsaved filename-rule changes." + Environment.NewLine + Environment.NewLine + "Close without saving?", "Close Filename Rules", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (choice != MessageBoxResult.OK) args.Cancel = true;
            };

            editorWindow.Closed += delegate
            {
                clearEditorWindowIfCurrent(editorWindow);
                services.SetStatus("Ready");
            };

            LoadState();
            LoadRuleIntoEditor(null, false);
            if (samples.Count > 0) sampleGrid.SelectedItem = samples[0];
            RefreshLists(true);
            services.SetStatus("Filename rules ready");
            services.Log("Opened filename rule editor.");
            editorWindow.Show();
            editorWindow.Activate();
        }
    }
}
