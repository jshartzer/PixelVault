using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string TrimFilenameConventionSeparatorText(string value)
        {
            return (value ?? string.Empty).Trim(' ', '_', '-', '.', '(', ')', '[', ']');
        }

        bool TryFindFilenameTimestampToken(string value, out string tokenPattern, out string tokenFormat, out int tokenIndex, out int tokenLength)
        {
            tokenPattern = string.Empty;
            tokenFormat = string.Empty;
            tokenIndex = -1;
            tokenLength = 0;

            var candidates = new[]
            {
                new { Pattern = @"\d{14}", Format = "yyyyMMddHHmmss" },
                new { Pattern = @"\d{4}[-_]\d{2}[-_]\d{2}[ T_-]\d{2}[-_:]\d{2}[-_:]\d{2}\s?[AP]M", Format = "yyyy-MM-dd hh-mm-ss tt" },
                new { Pattern = @"\d{4}[-_]\d{2}[-_]\d{2}[ T_-]\d{2}[-_:]\d{2}[-_:]\d{2}", Format = "yyyy-MM-dd HH-mm-ss" },
                new { Pattern = @"\d{4}[-_]\d{2}[-_]\d{2}", Format = "yyyy-MM-dd" },
                new { Pattern = @"\d{8}", Format = "yyyyMMdd" }
            };

            foreach (var candidate in candidates)
            {
                var match = Regex.Match(value ?? string.Empty, candidate.Pattern, RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                tokenPattern = candidate.Pattern;
                tokenFormat = candidate.Format;
                tokenIndex = match.Index;
                tokenLength = match.Length;
                return true;
            }
            return false;
        }

        string BuildFilenameConventionSuffixPattern(string suffix)
        {
            var cleaned = TrimFilenameConventionSeparatorText(suffix);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            if (Regex.IsMatch(cleaned, @"^\d+$")) return @"(?:[_-]\d+)?";
            return @"(?:[_\-\s]+" + Regex.Escape(cleaned).Replace(@"\ ", @"\s+") + ")?";
        }

        string DefaultPlatformTagsTextForLabel(string platformLabel)
        {
            var normalized = NormalizeConsoleLabel(platformLabel);
            if (string.Equals(normalized, "Steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
            if (string.Equals(normalized, "Xbox", StringComparison.OrdinalIgnoreCase)) return "Xbox";
            if (string.Equals(normalized, "PS5", StringComparison.OrdinalIgnoreCase)) return "PS5;PlayStation";
            if (string.Equals(normalized, "PlayStation", StringComparison.OrdinalIgnoreCase)) return "PlayStation";
            if (string.Equals(normalized, "PC", StringComparison.OrdinalIgnoreCase)) return "PC";
            return string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "Other", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized;
        }

        FilenameConventionRule BuildCustomFilenameConventionFromSample(FilenameConventionSample sample)
        {
            var fileName = Path.GetFileName(sample == null ? string.Empty : sample.FileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            var parsed = ParseFilename(fileName);
            var platformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(parsed.PlatformLabel) ? (sample == null ? string.Empty : sample.SuggestedPlatformLabel) : parsed.PlatformLabel);
            if (string.IsNullOrWhiteSpace(platformLabel)) platformLabel = "Other";

            var rule = new FilenameConventionRule
            {
                ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = "Custom: " + (string.IsNullOrWhiteSpace(fileName) ? "New Rule" : Path.GetFileNameWithoutExtension(fileName)),
                Enabled = true,
                Priority = 1200,
                PlatformLabel = platformLabel,
                PlatformTagsText = DefaultPlatformTagsTextForLabel(platformLabel),
                ConfidenceLabel = "CustomRule",
                IsBuiltIn = false
            };

            if (string.IsNullOrWhiteSpace(fileName))
            {
                rule.Pattern = @"^.+\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^\d{3,}_\d{14}(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Steam Screenshot";
                rule.PlatformLabel = "Steam";
                rule.PlatformTagsText = "Steam";
                rule.Pattern = @"^(?<appid>\d{3,})_(?<stamp>\d{14})(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";
                rule.SteamAppIdGroup = "appid";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^\d{14}(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Steam Manual Export";
                rule.PlatformLabel = "Steam";
                rule.PlatformTagsText = "Steam";
                rule.Pattern = @"^(?<stamp>\d{14})(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
                rule.RoutesToManualWhenMissingSteamAppId = true;
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^clip_[\d,]{13,17}\.(mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Steam Clip";
                rule.PlatformLabel = "Steam";
                rule.PlatformTagsText = "Steam";
                rule.Pattern = @"^clip_(?<stamp>[\d,]{13,17})\.(mp4|mkv|avi|mov|wmv|webm)$";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "unix-ms";
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^.+?\s*-\s*\d{4}-\d{2}-\d{2}\s+\d{2}-\d{2}-\d{2}\s+[AP]M\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Xbox Capture";
                rule.PlatformLabel = "Xbox";
                rule.PlatformTagsText = "Xbox";
                rule.Pattern = @"^(?<title>.+?)\s*-\s*(?<stamp>\d{4}-\d{2}-\d{2}\s+\d{2}-\d{2}-\d{2}\s+[AP]M)\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";
                rule.TitleGroup = "title";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyy-MM-dd hh-mm-ss tt";
                rule.PreserveFileTimes = true;
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^.+?_\d{14}\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Title And Timestamp";
                rule.Pattern = @"^(?<title>.+?)_(?<stamp>\d{14})\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";
                rule.TitleGroup = "title";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
                return rule;
            }

            string timestampPattern;
            string timestampFormat;
            int timestampIndex;
            int timestampLength;
            if (TryFindFilenameTimestampToken(baseName, out timestampPattern, out timestampFormat, out timestampIndex, out timestampLength))
            {
                var prefix = timestampIndex > 0 ? baseName.Substring(0, timestampIndex) : string.Empty;
                var suffix = timestampIndex + timestampLength < baseName.Length ? baseName.Substring(timestampIndex + timestampLength) : string.Empty;
                var prefixClean = TrimFilenameConventionSeparatorText(prefix);
                var suffixPattern = BuildFilenameConventionSuffixPattern(suffix);
                var extPattern = @"\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";

                var appIdAndTitlePrefix = Regex.Match(prefixClean, @"^(?<appid>\d{3,})[_\-\s]+(?<title>.+)$", RegexOptions.IgnoreCase);
                if (appIdAndTitlePrefix.Success)
                {
                    rule.Name = "Custom: AppID, Title, And Timestamp";
                    rule.Pattern = @"^(?<appid>\d{3,})[_\-\s]+(?<title>.+?)[_\-\s]+(?<stamp>" + timestampPattern + @")" + suffixPattern + extPattern;
                    rule.SteamAppIdGroup = "appid";
                    rule.TitleGroup = "title";
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = timestampFormat;
                    return rule;
                }

                if (Regex.IsMatch(prefixClean, @"^\d{3,}$", RegexOptions.IgnoreCase) && string.Equals(platformLabel, "Steam", StringComparison.OrdinalIgnoreCase))
                {
                    rule.Name = "Custom: AppID And Timestamp";
                    rule.PlatformLabel = "Steam";
                    rule.PlatformTagsText = "Steam";
                    rule.Pattern = @"^(?<appid>\d{3,})[_\-\s]+(?<stamp>" + timestampPattern + @")" + suffixPattern + extPattern;
                    rule.SteamAppIdGroup = "appid";
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = timestampFormat;
                    return rule;
                }

                if (!string.IsNullOrWhiteSpace(prefixClean))
                {
                    rule.Name = "Custom: Title And Timestamp";
                    rule.Pattern = @"^(?<title>.+?)[_\-\s]+(?<stamp>" + timestampPattern + @")" + suffixPattern + extPattern;
                    rule.TitleGroup = "title";
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = timestampFormat;
                    return rule;
                }
            }

            var escapedPattern = Regex.Escape(fileName);
            var timestampMatch = Regex.Match(fileName, @"\d{14}");
            if (timestampMatch.Success)
            {
                escapedPattern = escapedPattern.Replace(Regex.Escape(timestampMatch.Value), @"(?<stamp>\d{14})");
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
            }
            else
            {
                timestampMatch = Regex.Match(fileName, @"\d{8}");
                if (timestampMatch.Success)
                {
                    escapedPattern = escapedPattern.Replace(Regex.Escape(timestampMatch.Value), @"(?<stamp>\d{8})");
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = "yyyyMMdd";
                }
            }
            rule.Pattern = "^" + escapedPattern + "$";
            return rule;
        }

        void OpenFilenameConventionEditor()
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                MessageBox.Show("Library folder not found. Check Settings before opening filename rules.", "PixelVault");
                return;
            }
            if (filenameConventionEditorWindow != null)
            {
                if (filenameConventionEditorWindow.IsVisible)
                {
                    filenameConventionEditorWindow.Activate();
                    return;
                }
                filenameConventionEditorWindow = null;
            }

            try
            {
                status.Text = "Loading filename rules";

                List<FilenameConventionRule> customRules = new List<FilenameConventionRule>();
                List<FilenameConventionRule> builtInRules = new List<FilenameConventionRule>();
                List<FilenameConventionSample> samples = new List<FilenameConventionSample>();

                var editorWindow = new Window
                {
                    Title = "PixelVault " + AppVersion + " Filename Rules",
                    Width = 1560,
                    Height = 980,
                    MinWidth = 1260,
                    MinHeight = 780,
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#F3EEE4")
                };

                var root = new Grid { Margin = new Thickness(24) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(20), Padding = new Thickness(24), Margin = new Thickness(0, 0, 0, 18) };
                var headerStack = new StackPanel();
                headerStack.Children.Add(new TextBlock { Text = "Filename Rules", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                headerStack.Children.Add(new TextBlock { Text = "Centralize filename parsing here. Built-in rules stay as shipped defaults, while custom rules in the index database can extend or override them as conventions drift over time.", Margin = new Thickness(0, 8, 0, 0), FontSize = 14, Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap });
                headerStack.Children.Add(new TextBlock { Text = "Recent unmatched filenames are captured below so we can turn repeat misses into explicit rules instead of scattering regex logic across import paths.", Margin = new Thickness(0, 10, 0, 0), FontSize = 13, Foreground = Brush("#D8C7A4"), TextWrapping = TextWrapping.Wrap });
                header.Child = headerStack;
                root.Children.Add(header);

                var body = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
                Grid.SetRow(body, 1);
                root.Children.Add(body);

                var bodyGrid = new Grid();
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                body.Child = bodyGrid;

                var controlGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                controlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var addRuleButton = Btn("Add Rule", null, "#8A5A17", Brushes.White);
                addRuleButton.Width = 138;
                addRuleButton.Height = 42;
                addRuleButton.Margin = new Thickness(0, 0, 10, 0);
                controlGrid.Children.Add(addRuleButton);

                var sampleRuleButton = Btn("Rule From Sample", null, "#275D47", Brushes.White);
                sampleRuleButton.Width = 180;
                sampleRuleButton.Height = 42;
                sampleRuleButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(sampleRuleButton, 1);
                controlGrid.Children.Add(sampleRuleButton);

                var disableBuiltInButton = Btn("Disable Built-In", null, "#A3473E", Brushes.White);
                disableBuiltInButton.Width = 180;
                disableBuiltInButton.Height = 42;
                disableBuiltInButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(disableBuiltInButton, 2);
                controlGrid.Children.Add(disableBuiltInButton);

                var promoteFrequentButton = Btn("Promote Frequent", null, "#355F93", Brushes.White);
                promoteFrequentButton.Width = 170;
                promoteFrequentButton.Height = 42;
                promoteFrequentButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(promoteFrequentButton, 3);
                controlGrid.Children.Add(promoteFrequentButton);

                var reloadButton = Btn("Reload", null, "#EEF2F5", Brush("#33424D"));
                reloadButton.Width = 132;
                reloadButton.Height = 42;
                reloadButton.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(reloadButton, 4);
                controlGrid.Children.Add(reloadButton);

                var helperText = new TextBlock
                {
                    Text = "Custom rules save into the library index DB. Use a built-in override to disable a shipped rule without deleting its definition.",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brush("#5F6970"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 0, 14, 0)
                };
                Grid.SetColumn(helperText, 5);
                controlGrid.Children.Add(helperText);

                var saveTopButton = Btn("Save Rules", null, "#275D47", Brushes.White);
                saveTopButton.Width = 150;
                saveTopButton.Height = 42;
                saveTopButton.Margin = new Thickness(0);
                Grid.SetColumn(saveTopButton, 6);
                controlGrid.Children.Add(saveTopButton);
                bodyGrid.Children.Add(controlGrid);

                var mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
                Grid.SetRow(mainGrid, 1);
                bodyGrid.Children.Add(mainGrid);

                var customBorder = new Border { BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Background = Brush("#FAFCFD"), Padding = new Thickness(14), Margin = new Thickness(0, 0, 10, 14) };
                var customPanel = new Grid();
                customPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                customPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                customBorder.Child = customPanel;
                mainGrid.Children.Add(customBorder);

                customPanel.Children.Add(new TextBlock { Text = "Custom Rules", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 10) });

                var customGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    CanUserResizeRows = false,
                    SelectionMode = DataGridSelectionMode.Single,
                    SelectionUnit = DataGridSelectionUnit.FullRow,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brush("#D7E1E8"),
                    Background = Brushes.White,
                    AlternatingRowBackground = Brush("#F7FAFC"),
                    RowHeaderWidth = 0
                };
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 200 });
                customGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = new System.Windows.Data.Binding("Enabled"), Width = 56 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new System.Windows.Data.Binding("Priority") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 72 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Pattern", Binding = new System.Windows.Data.Binding("Pattern") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Platform", Binding = new System.Windows.Data.Binding("PlatformLabel") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 110 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Tags", Binding = new System.Windows.Data.Binding("PlatformTagsText") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 150 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "AppID Group", Binding = new System.Windows.Data.Binding("SteamAppIdGroup") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 90 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Title Group", Binding = new System.Windows.Data.Binding("TitleGroup") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 90 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Time Group", Binding = new System.Windows.Data.Binding("TimestampGroup") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 90 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Time Format", Binding = new System.Windows.Data.Binding("TimestampFormat") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 120 });
                customGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Keep File Time", Binding = new System.Windows.Data.Binding("PreserveFileTimes"), Width = 84 });
                customGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Manual If No AppID", Binding = new System.Windows.Data.Binding("RoutesToManualWhenMissingSteamAppId"), Width = 96 });
                customGrid.Columns.Add(new DataGridTextColumn { Header = "Confidence", Binding = new System.Windows.Data.Binding("ConfidenceLabel") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus }, Width = 110 });
                Grid.SetRow(customGrid, 1);
                customPanel.Children.Add(customGrid);

                var builtInBorder = new Border { BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Background = Brush("#FAFCFD"), Padding = new Thickness(14), Margin = new Thickness(10, 0, 0, 14) };
                Grid.SetColumn(builtInBorder, 1);
                var builtInPanel = new Grid();
                builtInPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                builtInPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                builtInBorder.Child = builtInPanel;
                mainGrid.Children.Add(builtInBorder);

                builtInPanel.Children.Add(new TextBlock { Text = "Built-In Rules", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 10) });

                var builtInGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    CanUserResizeRows = false,
                    IsReadOnly = true,
                    SelectionMode = DataGridSelectionMode.Single,
                    SelectionUnit = DataGridSelectionUnit.FullRow,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brush("#D7E1E8"),
                    Background = Brushes.White,
                    AlternatingRowBackground = Brush("#F7FAFC"),
                    RowHeaderWidth = 0
                };
                builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name"), Width = 170 });
                builtInGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = new System.Windows.Data.Binding("Enabled"), Width = 56 });
                builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new System.Windows.Data.Binding("Priority"), Width = 72 });
                builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Platform", Binding = new System.Windows.Data.Binding("PlatformLabel"), Width = 105 });
                builtInGrid.Columns.Add(new DataGridTextColumn { Header = "Pattern", Binding = new System.Windows.Data.Binding("Pattern"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
                Grid.SetRow(builtInGrid, 1);
                builtInPanel.Children.Add(builtInGrid);

                var samplesBorder = new Border { BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Background = Brush("#FAFCFD"), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14) };
                Grid.SetRow(samplesBorder, 2);
                bodyGrid.Children.Add(samplesBorder);

                var samplePanel = new Grid();
                samplePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                samplePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                samplePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                samplesBorder.Child = samplePanel;
                samplePanel.Children.Add(new TextBlock { Text = "Recent Unmatched Samples", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
                var sampleHelpText = new TextBlock { Text = "Double-click a sample to turn it into a starter rule, or use Promote Frequent to seed rules from repeated misses.", FontSize = 12, Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 10) };
                Grid.SetRow(sampleHelpText, 1);
                samplePanel.Children.Add(sampleHelpText);

                var sampleGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    CanUserResizeRows = false,
                    IsReadOnly = true,
                    SelectionMode = DataGridSelectionMode.Single,
                    SelectionUnit = DataGridSelectionUnit.FullRow,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brush("#D7E1E8"),
                    Background = Brushes.White,
                    AlternatingRowBackground = Brush("#F7FAFC"),
                    RowHeaderWidth = 0
                };
                sampleGrid.Columns.Add(new DataGridTextColumn { Header = "File Name", Binding = new System.Windows.Data.Binding("FileName"), Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
                sampleGrid.Columns.Add(new DataGridTextColumn { Header = "Suggested Platform", Binding = new System.Windows.Data.Binding("SuggestedPlatformLabel"), Width = 130 });
                sampleGrid.Columns.Add(new DataGridTextColumn { Header = "Suggested Rule", Binding = new System.Windows.Data.Binding("SuggestedConventionId"), Width = 160 });
                sampleGrid.Columns.Add(new DataGridTextColumn { Header = "Count", Binding = new System.Windows.Data.Binding("OccurrenceCount"), Width = 68 });
                sampleGrid.Columns.Add(new DataGridTextColumn { Header = "Last Seen (UTC)", Binding = new System.Windows.Data.Binding("LastSeenUtcTicks"), Width = 160 });
                Grid.SetRow(sampleGrid, 2);
                samplePanel.Children.Add(sampleGrid);

                var footerGrid = new Grid();
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(footerGrid, 3);
                bodyGrid.Children.Add(footerGrid);

                var statusText = new TextBlock { Foreground = Brush("#5F6970"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                footerGrid.Children.Add(statusText);

                var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var closeButton = Btn("Close", null, "#EEF2F5", Brush("#33424D"));
                closeButton.Width = 128;
                closeButton.Height = 44;
                closeButton.Margin = new Thickness(0, 0, 10, 0);
                var saveButton = Btn("Save Rules", null, "#275D47", Brushes.White);
                saveButton.Width = 148;
                saveButton.Height = 44;
                saveButton.Margin = new Thickness(0);
                actionRow.Children.Add(closeButton);
                actionRow.Children.Add(saveButton);
                Grid.SetColumn(actionRow, 1);
                footerGrid.Children.Add(actionRow);

                filenameConventionEditorWindow = editorWindow;
                editorWindow.Content = root;

                bool dirty = false;
                Action loadState = null;
                Action refreshUi = null;
                Action saveRules = null;
                Action<IEnumerable<FilenameConventionSample>, string> promoteSamples = null;

                loadState = delegate
                {
                    var allRules = filenameParserService.GetConventionRules(libraryRoot);
                    customRules = allRules
                        .Where(rule => rule != null && !rule.IsBuiltIn)
                        .Select(rule => new FilenameConventionRule
                        {
                            ConventionId = rule.ConventionId,
                            Name = rule.Name,
                            Enabled = rule.Enabled,
                            Priority = rule.Priority,
                            Pattern = rule.Pattern,
                            PlatformLabel = rule.PlatformLabel,
                            PlatformTagsText = rule.PlatformTagsText,
                            SteamAppIdGroup = rule.SteamAppIdGroup,
                            TitleGroup = rule.TitleGroup,
                            TimestampGroup = rule.TimestampGroup,
                            TimestampFormat = rule.TimestampFormat,
                            PreserveFileTimes = rule.PreserveFileTimes,
                            RoutesToManualWhenMissingSteamAppId = rule.RoutesToManualWhenMissingSteamAppId,
                            ConfidenceLabel = rule.ConfidenceLabel,
                            IsBuiltIn = false
                        })
                        .OrderByDescending(rule => rule.Priority)
                        .ThenBy(rule => rule.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    builtInRules = allRules
                        .Where(rule => rule != null && rule.IsBuiltIn)
                        .Select(rule => new FilenameConventionRule
                        {
                            ConventionId = rule.ConventionId,
                            Name = rule.Name,
                            Enabled = rule.Enabled,
                            Priority = rule.Priority,
                            Pattern = rule.Pattern,
                            PlatformLabel = rule.PlatformLabel,
                            PlatformTagsText = rule.PlatformTagsText,
                            SteamAppIdGroup = rule.SteamAppIdGroup,
                            TitleGroup = rule.TitleGroup,
                            TimestampGroup = rule.TimestampGroup,
                            TimestampFormat = rule.TimestampFormat,
                            PreserveFileTimes = rule.PreserveFileTimes,
                            RoutesToManualWhenMissingSteamAppId = rule.RoutesToManualWhenMissingSteamAppId,
                            ConfidenceLabel = rule.ConfidenceLabel,
                            IsBuiltIn = true
                        })
                        .OrderByDescending(rule => rule.Priority)
                        .ThenBy(rule => rule.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    samples = indexPersistenceService.LoadFilenameConventionSamples(libraryRoot, 80)
                        .OrderByDescending(sample => sample.LastSeenUtcTicks)
                        .ThenByDescending(sample => sample.OccurrenceCount)
                        .ToList();
                };

                refreshUi = delegate
                {
                    customGrid.ItemsSource = null;
                    customGrid.ItemsSource = customRules;
                    builtInGrid.ItemsSource = null;
                    builtInGrid.ItemsSource = builtInRules;
                    sampleGrid.ItemsSource = null;
                    sampleGrid.ItemsSource = samples.Select(sample => new
                    {
                        sample.SampleId,
                        sample.FileName,
                        sample.SuggestedPlatformLabel,
                        sample.SuggestedConventionId,
                        sample.OccurrenceCount,
                        LastSeenUtcTicks = sample.LastSeenUtcTicks > 0 ? new DateTime(sample.LastSeenUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss") : string.Empty
                    }).ToList();

                    var summary = customRules.Count + " custom | " + builtInRules.Count + " built-in | " + samples.Count + " sample(s) | " + (dirty ? "Unsaved changes" : "Saved");
                    var selectedCustom = customGrid.SelectedItem as FilenameConventionRule;
                    var selectedBuiltIn = builtInGrid.SelectedItem as FilenameConventionRule;
                    var selectedSample = sampleGrid.SelectedIndex >= 0 && sampleGrid.SelectedIndex < samples.Count ? samples[sampleGrid.SelectedIndex] : null;
                    promoteFrequentButton.IsEnabled = selectedSample != null || samples.Any(sample => sample.OccurrenceCount >= 2);
                    if (selectedCustom != null) summary += " | Editing " + (selectedCustom.Name ?? selectedCustom.ConventionId ?? "custom rule");
                    else if (selectedBuiltIn != null) summary += " | Built-in " + (selectedBuiltIn.Name ?? selectedBuiltIn.ConventionId ?? "rule");
                    else if (selectedSample != null) summary += " | Sample " + (selectedSample.FileName ?? string.Empty);
                    statusText.Text = summary;
                };

                saveRules = delegate
                {
                    try
                    {
                        customGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                        customGrid.CommitEdit(DataGridEditingUnit.Row, true);

                        foreach (var rule in customRules)
                        {
                            rule.ConventionId = CleanTag(rule.ConventionId);
                            if (string.IsNullOrWhiteSpace(rule.ConventionId)) rule.ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                            rule.Name = CleanTag(rule.Name);
                            if (string.IsNullOrWhiteSpace(rule.Name)) rule.Name = "Custom Rule";
                            rule.Priority = Math.Max(-100000, Math.Min(100000, rule.Priority));
                            rule.Pattern = (rule.Pattern ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(rule.Pattern)) throw new InvalidOperationException("Each custom filename rule needs a regex pattern before it can be saved.");
                            _ = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
                            rule.PlatformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(rule.PlatformLabel) ? "Other" : rule.PlatformLabel);
                            var defaultTags = string.IsNullOrWhiteSpace(rule.PlatformTagsText) ? DefaultPlatformTagsTextForLabel(rule.PlatformLabel) : rule.PlatformTagsText;
                            rule.PlatformTagsText = string.Join("; ", ParseTagText(defaultTags));
                            rule.SteamAppIdGroup = CleanTag(rule.SteamAppIdGroup);
                            rule.TitleGroup = CleanTag(rule.TitleGroup);
                            rule.TimestampGroup = CleanTag(rule.TimestampGroup);
                            rule.TimestampFormat = CleanTag(rule.TimestampFormat);
                            rule.ConfidenceLabel = CleanTag(string.IsNullOrWhiteSpace(rule.ConfidenceLabel) ? "CustomRule" : rule.ConfidenceLabel);
                            rule.IsBuiltIn = false;
                        }

                        customRules = customRules
                            .GroupBy(rule => rule.ConventionId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                            .Select(group => group.First())
                            .OrderByDescending(rule => rule.Priority)
                            .ThenBy(rule => rule.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        indexPersistenceService.SaveFilenameConventions(libraryRoot, customRules);
                        filenameParserService.InvalidateRules(libraryRoot);
                        loadState();
                        dirty = false;
                        refreshUi();
                        if (previewBox != null) RefreshPreview();
                        status.Text = "Filename rules saved";
                        Log("Saved " + customRules.Count + " custom filename rule(s) to the index database.");
                    }
                    catch (Exception saveEx)
                    {
                        status.Text = "Filename rule save failed";
                        Log("Failed to save filename rules. " + saveEx.Message);
                        MessageBox.Show("Could not save the filename rules." + Environment.NewLine + Environment.NewLine + saveEx.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                promoteSamples = delegate(IEnumerable<FilenameConventionSample> sourceSamples, string successMessage)
                {
                    var added = 0;
                    foreach (var sample in (sourceSamples ?? Enumerable.Empty<FilenameConventionSample>()).Where(item => item != null))
                    {
                        var candidate = BuildCustomFilenameConventionFromSample(sample);
                        if (candidate == null || string.IsNullOrWhiteSpace(candidate.Pattern)) continue;
                        if (customRules.Any(rule => rule != null && string.Equals(rule.Pattern, candidate.Pattern, StringComparison.OrdinalIgnoreCase))) continue;
                        customRules.Insert(0, candidate);
                        added++;
                    }

                    if (added <= 0)
                    {
                        MessageBox.Show("No new starter rules were added. The matching pattern may already exist in your custom rules.", "PixelVault");
                        return;
                    }

                    dirty = true;
                    refreshUi();
                    customGrid.SelectedIndex = 0;
                    customGrid.ScrollIntoView(customGrid.SelectedItem);
                    status.Text = successMessage + " (" + added + " added)";
                };

                addRuleButton.Click += delegate
                {
                    var newRule = new FilenameConventionRule
                    {
                        ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                        Name = "Custom Rule",
                        Enabled = true,
                        Priority = 1200,
                        Pattern = @"^.+\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$",
                        PlatformLabel = "Other",
                        PlatformTagsText = string.Empty,
                        ConfidenceLabel = "CustomRule",
                        IsBuiltIn = false
                    };
                    customRules.Insert(0, newRule);
                    dirty = true;
                    refreshUi();
                    customGrid.SelectedItem = newRule;
                    customGrid.ScrollIntoView(newRule);
                    status.Text = "New filename rule added";
                };

                sampleRuleButton.Click += delegate
                {
                    var selectedSample = sampleGrid.SelectedIndex >= 0 && sampleGrid.SelectedIndex < samples.Count ? samples[sampleGrid.SelectedIndex] : null;
                    if (selectedSample == null)
                    {
                        MessageBox.Show("Select a recent unmatched sample first.", "PixelVault");
                        return;
                    }
                    promoteSamples(new[] { selectedSample }, "Created a starter rule from the selected sample");
                };

                disableBuiltInButton.Click += delegate
                {
                    var selected = builtInGrid.SelectedItem as FilenameConventionRule;
                    if (selected == null)
                    {
                        MessageBox.Show("Select a built-in rule first.", "PixelVault");
                        return;
                    }
                    var existingOverride = customRules.FirstOrDefault(rule => string.Equals(rule.ConventionId, selected.ConventionId, StringComparison.OrdinalIgnoreCase));
                    if (existingOverride == null)
                    {
                        existingOverride = new FilenameConventionRule
                        {
                            ConventionId = selected.ConventionId,
                            Name = selected.Name,
                            Enabled = false,
                            Priority = selected.Priority,
                            Pattern = selected.Pattern,
                            PlatformLabel = selected.PlatformLabel,
                            PlatformTagsText = selected.PlatformTagsText,
                            SteamAppIdGroup = selected.SteamAppIdGroup,
                            TitleGroup = selected.TitleGroup,
                            TimestampGroup = selected.TimestampGroup,
                            TimestampFormat = selected.TimestampFormat,
                            PreserveFileTimes = selected.PreserveFileTimes,
                            RoutesToManualWhenMissingSteamAppId = selected.RoutesToManualWhenMissingSteamAppId,
                            ConfidenceLabel = "CustomOverride",
                            IsBuiltIn = false
                        };
                        customRules.Insert(0, existingOverride);
                    }
                    else
                    {
                        existingOverride.Enabled = false;
                        existingOverride.ConfidenceLabel = "CustomOverride";
                    }
                    dirty = true;
                    refreshUi();
                    customGrid.SelectedItem = existingOverride;
                    customGrid.ScrollIntoView(existingOverride);
                    status.Text = "Built-in rule disabled through a custom override";
                };

                reloadButton.Click += delegate
                {
                    if (dirty)
                    {
                        var choice = MessageBox.Show("Discard unsaved filename-rule edits and reload from disk?", "Reload Filename Rules", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                        if (choice != MessageBoxResult.OK) return;
                    }
                    filenameParserService.InvalidateRules(libraryRoot);
                    loadState();
                    dirty = false;
                    refreshUi();
                    status.Text = "Filename rules reloaded";
                    Log("Reloaded filename rules and recent samples from the index database.");
                };

                promoteFrequentButton.Click += delegate
                {
                    var selectedSample = sampleGrid.SelectedIndex >= 0 && sampleGrid.SelectedIndex < samples.Count ? samples[sampleGrid.SelectedIndex] : null;
                    var frequentSamples = samples
                        .Where(sample => sample != null && sample.OccurrenceCount >= 2)
                        .OrderByDescending(sample => sample.OccurrenceCount)
                        .ThenByDescending(sample => sample.LastSeenUtcTicks)
                        .Take(5)
                        .ToList();
                    if (selectedSample != null && !frequentSamples.Any(sample => sample.SampleId == selectedSample.SampleId))
                    {
                        frequentSamples.Insert(0, selectedSample);
                    }
                    if (frequentSamples.Count == 0)
                    {
                        MessageBox.Show("There are no repeated unmatched samples yet. Select a sample and use Rule From Sample instead.", "PixelVault");
                        return;
                    }
                    promoteSamples(frequentSamples, "Promoted frequent unmatched samples into starter rules");
                };

                customGrid.SelectionChanged += delegate { refreshUi(); };
                builtInGrid.SelectionChanged += delegate { refreshUi(); };
                sampleGrid.SelectionChanged += delegate { refreshUi(); };
                sampleGrid.MouseDoubleClick += delegate
                {
                    var selectedSample = sampleGrid.SelectedIndex >= 0 && sampleGrid.SelectedIndex < samples.Count ? samples[sampleGrid.SelectedIndex] : null;
                    if (selectedSample != null) promoteSamples(new[] { selectedSample }, "Created a starter rule from the selected sample");
                };
                customGrid.CellEditEnding += delegate { dirty = true; };
                saveTopButton.Click += delegate { saveRules(); };
                saveButton.Click += delegate { saveRules(); };

                closeButton.Click += delegate
                {
                    if (dirty)
                    {
                        var choice = MessageBox.Show("You have unsaved filename-rule changes." + Environment.NewLine + Environment.NewLine + "Close without saving?", "Close Filename Rules", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        if (choice != MessageBoxResult.OK) return;
                    }
                    editorWindow.Close();
                };

                editorWindow.Closed += delegate
                {
                    if (ReferenceEquals(filenameConventionEditorWindow, editorWindow)) filenameConventionEditorWindow = null;
                    status.Text = "Ready";
                };

                loadState();
                refreshUi();
                status.Text = "Filename rules ready";
                Log("Opened filename rule editor.");
                editorWindow.Show();
                editorWindow.Activate();
            }
            catch (Exception ex)
            {
                status.Text = "Filename rules unavailable";
                Log("Failed to open filename rules. " + ex.Message);
                MessageBox.Show("Could not open the filename rules." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault");
            }
        }
    }
}
