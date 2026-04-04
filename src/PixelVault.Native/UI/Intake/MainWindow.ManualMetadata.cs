using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        bool ShowManualMetadataWindow(List<ManualMetadataItem> items, bool libraryMode, string contextName, bool importAndEditMode = false)
        {
            if (items == null || items.Count == 0) return true;

            var useFlexiblePreview = libraryMode || importAndEditMode;
            var contextLabel = string.IsNullOrWhiteSpace(contextName) ? "selected folder" : contextName;
            var windowLabel = importAndEditMode ? "Import and Edit" : (libraryMode ? "Edit Library Metadata" : "Missing Data");
            var headerTitleText = importAndEditMode ? "Import and edit captures" : (libraryMode ? "Edit library metadata" : "Add missing metadata");
            var headerDescriptionText = importAndEditMode
                ? items.Count + " file(s) in the upload folder (rule-matched and manual intake). Select files to include in this import, adjust metadata, then continue. Files not selected stay in the upload folder."
                : libraryMode
                    ? items.Count + " capture(s) from " + contextLabel + " are ready for metadata edits. Select one or more files, update the game title prefix, tags, one console tag, an optional capture date/time, and an optional comment. Files can also be reorganized into the proper game folder when the title changes."
                    : items.Count + " capture(s) were left in intake because they did not match a known format. Select one or more files, add a game title prefix, tags, one console tag, an optional capture date/time, and an optional comment before sending them to the destination.";
            var leaveButtonText = libraryMode || importAndEditMode ? "Close" : "Leave Unchanged";
            var finishButtonText = importAndEditMode ? "Continue Import" : (libraryMode ? "Apply Changes" : "Apply and Send");
            var emptySelectionText = libraryMode ? "Choose one or more library images to edit." : importAndEditMode ? "Choose one or more upload files to include in this import." : "Choose unmatched intake images to add metadata.";
            var defaultStatusText = libraryMode ? "Update the game title prefix, tags, one console tag, optional date/time, and an optional comment." : importAndEditMode ? "Adjust metadata for selected files, then continue import." : "Add a game title prefix, tags, one console tag, optional date/time, and an optional comment.";
            var singleSelectionMetaPrefix = libraryMode ? "Library capture time | " : "Filesystem time | ";
            var confirmCaption = libraryMode ? "Library Metadata" : importAndEditMode ? "Import and Edit" : "Manual Intake";
            var confirmMessage = libraryMode
                ? items.Count + " image(s) will be renamed if needed, updated with metadata, and reorganized in the library if their title changes.\n\nApply changes now?"
                : importAndEditMode
                    ? string.Empty
                    : items.Count + " image(s) will be renamed if needed, tagged, updated with metadata, and moved to the destination.\n\nApply changes and send them now?";

            var manualWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " " + windowLabel,
                Width = 1220,
                Height = 1040,
                MinWidth = 1040,
                MinHeight = 920,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var banner = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(18), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 16) };
            var bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bannerStack = new StackPanel();
            bannerStack.Children.Add(new TextBlock { Text = headerTitleText, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            bannerStack.Children.Add(new TextBlock { Text = headerDescriptionText, Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            bannerGrid.Children.Add(bannerStack);
            var leaveButton = Btn(leaveButtonText, null, "#334249", Brushes.White);
            leaveButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(leaveButton, 1);
            bannerGrid.Children.Add(leaveButton);
            var finishButton = Btn(finishButtonText, null, "#275D47", Brushes.White);
            finishButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(finishButton, 2);
            bannerGrid.Children.Add(finishButton);
            banner.Child = bannerGrid;
            root.Children.Add(banner);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);

            var listCard = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(8), Margin = new Thickness(0, 0, 16, 0) };
            var fileList = new ListBox { Background = Brush("#12191E"), BorderThickness = new Thickness(0), Foreground = Brushes.White, Padding = new Thickness(10), HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Extended };
            listCard.Child = fileList;
            main.Children.Add(listCard);

            var detailCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            Grid.SetColumn(detailCard, 1);
            main.Children.Add(detailCard);

            var detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var detailStack = new StackPanel();
            detailScroll.Content = detailStack;
            detailCard.Child = detailScroll;

            var selectedTitle = new TextBlock { Text = "Select one or more captures", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), TextWrapping = TextWrapping.Wrap };
            var selectedMeta = new TextBlock { Text = emptySelectionText, Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
            var previewBorder = new Border { Background = Brush("#EAF0F5"), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            var previewImage = new Image { Stretch = Stretch.Uniform };
            if (useFlexiblePreview)
            {
                previewBorder.MinHeight = 200;
                previewImage.MinHeight = 200;
                previewImage.MaxHeight = 420;
                manualWindow.SizeChanged += delegate
                {
                    if (!manualWindow.IsLoaded) return;
                    var h = Math.Max(220, (manualWindow.ActualHeight - 460) * 0.45);
                    previewImage.MaxHeight = h;
                };
            }
            else previewImage.Height = 320;
            var guessCallout = new Border { Background = Brush("#F4F7F9"), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 16) };
            var guessText = new TextBlock { Text = "Best Guess | No confident match", FontSize = 13, Foreground = Brush("#8B98A3"), TextWrapping = TextWrapping.Wrap };
            guessCallout.Child = guessText;
            var steamLookupLabel = new TextBlock { Text = "Steam lookup", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") };
            var steamLookupGrid = new Grid { Margin = new Thickness(0, 8, 0, 14) };
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            var steamSearchBox = new TextBox { Margin = new Thickness(0, 0, 12, 0), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), FontSize = 14 };
            var steamSearchButton = Btn("Search Steam", null, "#174A73", Brushes.White);
            steamSearchButton.Width = 150;
            steamSearchButton.Height = 40;
            steamSearchButton.Margin = new Thickness(0, 0, 12, 0);
            var steamAppIdBox = new TextBox { Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), FontSize = 14 };
            steamLookupGrid.Children.Add(steamSearchBox);
            Grid.SetColumn(steamSearchButton, 1);
            steamLookupGrid.Children.Add(steamSearchButton);
            Grid.SetColumn(steamAppIdBox, 2);
            steamLookupGrid.Children.Add(steamAppIdBox);
            var steamLookupStatus = new TextBlock { Text = "Search by game title or numeric Steam AppID (Search looks up the store name), or paste an AppID in the box on the right.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap };
            var knownGameChoices = new List<string>();
            var knownGameChoiceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownGameChoiceNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var gameNameBox = new ComboBox
            {
                Margin = new Thickness(0, 8, 0, 14),
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 14,
                FontFamily = new FontFamily("Cascadia Mono"),
                IsEditable = true,
                IsTextSearchEnabled = true,
                StaysOpenOnEdit = true
            };
            var tagsBox = new TextBox { Margin = new Thickness(0, 8, 0, 14), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 14 };
            var photographyBox = new CheckBox { Content = "Add Game Photography tag", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 14, 10), IsThreeState = true };
            var tagSeparator = new Border { Width = 1, Height = 20, Background = Brush("#D7E1E8"), Margin = new Thickness(2, 2, 16, 10), VerticalAlignment = VerticalAlignment.Center };
            var steamBox = new CheckBox { Content = "Steam", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var pcBox = new CheckBox { Content = "PC", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var ps5Box = new CheckBox { Content = "PS5", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var xboxBox = new CheckBox { Content = "Xbox", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            var otherBox = new CheckBox { Content = "Other", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 12, 10), IsThreeState = true };
            var otherPlatformBox = new TextBox { Width = 190, Margin = new Thickness(0, 0, 0, 10), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), FontSize = 13, IsEnabled = false };
            var tagToggleRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            tagToggleRow.Children.Add(photographyBox);
            tagToggleRow.Children.Add(tagSeparator);
            tagToggleRow.Children.Add(steamBox);
            tagToggleRow.Children.Add(pcBox);
            tagToggleRow.Children.Add(ps5Box);
            tagToggleRow.Children.Add(xboxBox);
            tagToggleRow.Children.Add(otherBox);
            tagToggleRow.Children.Add(otherPlatformBox);
            var useCustomTimeBox = new CheckBox { Content = "Use custom date/time", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 8), IsThreeState = true };
            var dateRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var captureDatePicker = new DatePicker { Width = 170 };
            var hourBox = new ComboBox { Width = 68, Margin = new Thickness(12, 0, 0, 0) };
            for (int hour = 1; hour <= 12; hour++) hourBox.Items.Add(hour.ToString());
            var minuteBox = new ComboBox { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            for (int minute = 0; minute < 60; minute++) minuteBox.Items.Add(minute.ToString("00"));
            var ampmBox = new ComboBox { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            ampmBox.Items.Add("AM");
            ampmBox.Items.Add("PM");
            dateRow.Children.Add(captureDatePicker);
            dateRow.Children.Add(hourBox);
            dateRow.Children.Add(minuteBox);
            dateRow.Children.Add(ampmBox);
            var commentBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120, Margin = new Thickness(0, 8, 0, 0), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 14 };
            var statusText = new TextBlock { Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

            detailStack.Children.Add(selectedTitle);
            detailStack.Children.Add(selectedMeta);
            detailStack.Children.Add(previewBorder);
            detailStack.Children.Add(guessCallout);
            detailStack.Children.Add(steamLookupLabel);
            detailStack.Children.Add(steamLookupGrid);
            detailStack.Children.Add(steamLookupStatus);
            detailStack.Children.Add(new TextBlock { Text = "Game title to prepend", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(gameNameBox);
            detailStack.Children.Add(new TextBlock { Text = "Additional tags", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(tagsBox);
            detailStack.Children.Add(tagToggleRow);
            detailStack.Children.Add(new TextBlock { Text = "Capture date and time", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(useCustomTimeBox);
            detailStack.Children.Add(new TextBlock { Text = "If left off, PixelVault uses the existing filesystem timestamp.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            detailStack.Children.Add(dateRow);
            var deleteBeforeBox = new CheckBox
            {
                Content = "Delete selected file(s) before import (skipped for metadata and move)",
                Foreground = Brush("#9B2C2C"),
                Margin = new Thickness(0, 0, 0, 12),
                Visibility = importAndEditMode ? Visibility.Visible : Visibility.Collapsed
            };
            detailStack.Children.Add(deleteBeforeBox);
            detailStack.Children.Add(new TextBlock { Text = "Comment for Immich description", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(commentBox);
            detailStack.Children.Add(statusText);

            bool suppressSync = false;
            bool dialogReady = false;
            CancellationTokenSource steamSearchCancellation = null;
            int steamSearchRequestVersion = 0;
            int gameTitleChoicesRefreshVersion = 0;
            var badgeBlocks = new Dictionary<ManualMetadataItem, TextBlock>();
            var tileBorders = new Dictionary<ManualMetadataItem, Border>();
            var selectedItems = new List<ManualMetadataItem>();
            Action refreshSelectionStatus = null;
            Action syncSelectedSteamAppIds = null;
            Action refreshGameTitleChoices = delegate
            {
                var version = ++gameTitleChoicesRefreshVersion;
                var root = libraryRoot;
                System.Threading.Tasks.Task.Factory.StartNew(delegate
                {
                    var rows = LoadSavedGameIndexRows(root);
                    if (rows == null || rows.Count == 0) rows = LoadGameIndexEditorRowsCore(root, null);
                    return rows ?? new List<GameIndexEditorRow>();
                }).ContinueWith(delegate(System.Threading.Tasks.Task<List<GameIndexEditorRow>> loadTask)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (version != gameTitleChoicesRefreshVersion) return;
                        if (loadTask.IsFaulted)
                        {
                            var flattened = loadTask.Exception == null ? null : loadTask.Exception.Flatten();
                            var err = flattened == null ? null : flattened.InnerExceptions.FirstOrDefault();
                            if (err == null) Log("Game title list load failed.");
                            else LogException("Game title list load", err);
                            return;
                        }
                        var rows = loadTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && loadTask.Result != null
                            ? loadTask.Result
                            : new List<GameIndexEditorRow>();
                        var loadedChoices = rows
                            .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                            .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(row => row.PlatformLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                            .Select(row => BuildGameTitleChoiceLabel(row.Name, row.PlatformLabel))
                            .Where(label => !string.IsNullOrWhiteSpace(label))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        foreach (var extraChoice in knownGameChoices.Where(label => !string.IsNullOrWhiteSpace(label)))
                        {
                            if (!loadedChoices.Contains(extraChoice, StringComparer.OrdinalIgnoreCase)) loadedChoices.Add(extraChoice);
                        }
                        knownGameChoices = loadedChoices;
                        knownGameChoiceSet = new HashSet<string>(knownGameChoices, StringComparer.OrdinalIgnoreCase);
                        knownGameChoiceNameMap = rows
                            .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                            .Select(row => new { Label = BuildGameTitleChoiceLabel(row.Name, row.PlatformLabel), Name = NormalizeGameIndexName(row.Name, row.FolderPath) })
                            .Where(entry => !string.IsNullOrWhiteSpace(entry.Label) && !string.IsNullOrWhiteSpace(entry.Name))
                            .GroupBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(group => CleanTag(group.Key), group => group.First().Name, StringComparer.OrdinalIgnoreCase);
                        foreach (var extraChoice in knownGameChoices)
                        {
                            var normalizedChoice = CleanTag(extraChoice);
                            if (knownGameChoiceNameMap.ContainsKey(normalizedChoice)) continue;
                            var extraName = ExtractGameNameFromChoiceLabel(extraChoice);
                            if (!string.IsNullOrWhiteSpace(extraName)) knownGameChoiceNameMap[normalizedChoice] = extraName;
                        }
                        var restoreText = gameNameBox.Text;
                        gameNameBox.ItemsSource = null;
                        gameNameBox.ItemsSource = knownGameChoices;
                        gameNameBox.Text = restoreText;
                    }));
                }, System.Threading.Tasks.TaskScheduler.Default);
            };
            Action syncSelectedGameNames = delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                var selectedTitleText = CleanTag(gameNameBox.Text);
                string mappedName;
                if (knownGameChoiceNameMap.TryGetValue(selectedTitleText, out mappedName)) selectedTitleText = mappedName;
                else selectedTitleText = ExtractGameNameFromChoiceLabel(selectedTitleText);
                foreach (var item in selectedItems) item.GameName = selectedTitleText;
                refreshSelectionStatus();
            };
            syncSelectedSteamAppIds = delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                var cleanedAppId = Regex.Replace((steamAppIdBox.Text ?? string.Empty).Trim(), @"[^\d]", string.Empty);
                if (!string.Equals(steamAppIdBox.Text ?? string.Empty, cleanedAppId, StringComparison.Ordinal))
                {
                    suppressSync = true;
                    steamAppIdBox.Text = cleanedAppId;
                    steamAppIdBox.SelectionStart = steamAppIdBox.Text.Length;
                    suppressSync = false;
                }
                foreach (var item in selectedItems) item.SteamAppId = cleanedAppId;
                if (!string.IsNullOrWhiteSpace(cleanedAppId))
                {
                    foreach (var item in selectedItems)
                    {
                        item.TagSteam = true;
                        item.TagPc = false;
                        item.TagPs5 = false;
                        item.TagXbox = false;
                        item.TagOther = false;
                        item.CustomPlatformTag = string.Empty;
                        item.ForceTagMetadataWrite = true;
                    }
                }
                refreshSelectionStatus();
            };

            Func<ManualMetadataItem, string> manualBadgeLabel = delegate(ManualMetadataItem item)
            {
                if (item != null && item.IntakeRuleMatched) return "Auto";
                if (item.TagSteam) return "Steam";
                if (item.TagPc) return "PC";
                if (item.TagPs5) return "PS5";
                if (item.TagXbox) return "Xbox";
                if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) return CleanTag(item.CustomPlatformTag);
                return "Manual";
            };

            Func<string, Brush> manualBadgeBrush = delegate(string label)
            {
                if (string.Equals(label, "Auto", StringComparison.OrdinalIgnoreCase)) return Brush("#5CB88A");
                if (string.Equals(label, "Steam", StringComparison.OrdinalIgnoreCase)) return Brush("#69A7FF");
                if (string.Equals(label, "PC", StringComparison.OrdinalIgnoreCase)) return Brush("#7F8EA3");
                if (string.Equals(label, "PS5", StringComparison.OrdinalIgnoreCase)) return Brush("#4F83FF");
                if (string.Equals(label, "Xbox", StringComparison.OrdinalIgnoreCase)) return Brush("#66C47A");
                return Brush("#D0A15F");
            };

            Func<IEnumerable<ManualMetadataItem>, Func<ManualMetadataItem, string>, string> sharedText = delegate(IEnumerable<ManualMetadataItem> selection, Func<ManualMetadataItem, string> getter)
            {
                var values = selection.Select(getter).Select(v => (v ?? string.Empty).Trim()).Distinct(StringComparer.Ordinal).ToArray();
                return values.Length == 1 ? values[0] : string.Empty;
            };

            Func<IEnumerable<ManualMetadataItem>, Func<ManualMetadataItem, bool>, bool?> sharedBool = delegate(IEnumerable<ManualMetadataItem> selection, Func<ManualMetadataItem, bool> getter)
            {
                var values = selection.Select(getter).Distinct().ToArray();
                return values.Length == 1 ? (bool?)values[0] : null;
            };

            Func<IEnumerable<ManualMetadataItem>, string> sharedFilenameGuess = delegate(IEnumerable<ManualMetadataItem> selection)
            {
                var guesses = selection.Select(item => FilenameGuessLabel(item == null ? string.Empty : item.FileName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (guesses.Length == 0) return "Best Guess | No confident match";
                if (guesses.Length == 1) return "Best Guess | " + guesses[0];
                return "Best Guess | Mixed guesses";
            };

            Func<int, UIElement> buildMultiPreview = delegate(int count)
            {
                var multiH = useFlexiblePreview ? Math.Min(400, Math.Max(240, previewImage.MaxHeight)) : 320;
                var grid = new Grid { Height = multiH };
                var art = new Grid { Width = 260, Height = 190, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var back = new Border { Width = 136, Height = 104, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(64, -44, 0, 0) };
                var mid = new Border { Width = 148, Height = 112, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(30, -20, 0, 0) };
                var front = new Border { Width = 160, Height = 120, Background = Brushes.White, BorderBrush = Brush("#2E2A2A"), BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
                var frontGrid = new Grid();                frontGrid.Children.Add(new Border { Width = 78, Height = 78, Background = Brush("#161C20"), CornerRadius = new CornerRadius(39), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } });
                front.Child = frontGrid;
                art.Children.Add(back);
                art.Children.Add(mid);
                art.Children.Add(front);
                grid.Children.Add(art);
                return grid;
            };

            Action refreshDateControls = delegate
            {
                var enabled = useCustomTimeBox.IsChecked == true;
                captureDatePicker.IsEnabled = enabled;
                hourBox.IsEnabled = enabled;
                minuteBox.IsEnabled = enabled;
                ampmBox.IsEnabled = enabled;
                otherPlatformBox.IsEnabled = otherBox.IsChecked == true;
            };

            Action<IEnumerable<ManualMetadataItem>, string> applyConsoleSelection = delegate(IEnumerable<ManualMetadataItem> selection, string platform)
            {
                foreach (var item in selection)
                {
                    item.TagSteam = string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase);
                    item.TagPc = string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase);
                    item.TagPs5 = string.Equals(platform, "PS5", StringComparison.OrdinalIgnoreCase);
                    item.TagXbox = string.Equals(platform, "Xbox", StringComparison.OrdinalIgnoreCase);
                    item.TagOther = string.Equals(platform, "Other", StringComparison.OrdinalIgnoreCase);
                    if (!item.TagOther) item.CustomPlatformTag = string.Empty;
                    item.ForceTagMetadataWrite = true;
                }
            };

            Action refreshTileBadges = delegate
            {
                foreach (var pair in badgeBlocks)
                {
                    var label = manualBadgeLabel(pair.Key);
                    pair.Value.Text = "[" + label + "]";
                    pair.Value.Foreground = manualBadgeBrush(label);
                }
            };

            Action refreshTileSelectionState = delegate
            {
                foreach (var pair in tileBorders)
                {
                    var isSelected = selectedItems.Contains(pair.Key);
                    pair.Value.Background = isSelected ? Brush("#24323C") : Brush("#1A2329");
                    pair.Value.BorderBrush = isSelected ? Brush("#69A7FF") : Brush("#1A2329");
                    pair.Value.BorderThickness = new Thickness(isSelected ? 2 : 1);
                }
            };

            Action saveSelectedDateTime = delegate
            {
                if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return;
                var fallback = selectedItems[0].CaptureTime;
                var date = captureDatePicker.SelectedDate ?? fallback.Date;
                int hour12 = ParseInt(Convert.ToString(hourBox.SelectedItem));
                if (hour12 < 1 || hour12 > 12)
                {
                    var fallbackHour = fallback.Hour % 12;
                    hour12 = fallbackHour == 0 ? 12 : fallbackHour;
                }
                int minute = ParseInt(Convert.ToString(minuteBox.SelectedItem));
                var ampm = Convert.ToString(ampmBox.SelectedItem);
                if (string.IsNullOrWhiteSpace(ampm)) ampm = fallback.Hour >= 12 ? "PM" : "AM";
                int hour24 = hour12 % 12;
                if (string.Equals(ampm, "PM", StringComparison.OrdinalIgnoreCase)) hour24 += 12;
                var newTime = new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0);
                foreach (var item in selectedItems) item.CaptureTime = newTime;
            };
            refreshSelectionStatus = delegate
            {
                var notes = new List<string>();
                if (selectedItems.Count > 1) notes.Add(selectedItems.Count + " files selected");
                if (!string.IsNullOrWhiteSpace(gameNameBox.Text)) notes.Add("rename prefix ready");
                if (!string.IsNullOrWhiteSpace(steamAppIdBox.Text)) notes.Add("Steam AppID " + steamAppIdBox.Text);
                if (!string.IsNullOrWhiteSpace(commentBox.Text)) notes.Add("comment saved");
                var tagCount = ParseTagText(tagsBox.Text).Length;
                if (tagCount > 0) notes.Add(tagCount + " extra tag(s)");
                if (useCustomTimeBox.IsChecked == true) notes.Add("custom capture time");
                if (photographyBox.IsChecked == true) notes.Add(GamePhotographyTag + " tag enabled");
                if (steamBox.IsChecked == true) notes.Add("platform tag: Steam");
                else if (pcBox.IsChecked == true) notes.Add("platform tag: PC");
                else if (ps5Box.IsChecked == true) notes.Add("platform tag: PS5");
                else if (xboxBox.IsChecked == true) notes.Add("platform tag: Xbox");
                else                 if (otherBox.IsChecked == true) notes.Add("platform tag: " + CleanTag(otherPlatformBox.Text));
                if (importAndEditMode && selectedItems.Count > 0)
                {
                    var del = selectedItems.Count(i => i.DeleteBeforeProcessing);
                    if (del > 0) notes.Add(del + " marked for delete before import");
                }
                statusText.Text = notes.Count == 0 ? defaultStatusText : string.Join(" | ", notes.ToArray()) + ".";
            };
            refreshGameTitleChoices();


            Action refreshSelectionUi = delegate
            {
                if (selectedItems.Count == 0)
                {
                    selectedTitle.Text = "Select one or more captures";
                    selectedMeta.Text = emptySelectionText;
                    guessText.Text = "Best Guess | No confident match";
                    previewBorder.Child = buildMultiPreview(0);
                    suppressSync = true;
                    steamSearchBox.Text = string.Empty;
                    steamAppIdBox.Text = string.Empty;
                    steamLookupStatus.Text = "Search by game title or numeric Steam AppID, or paste an AppID in the box on the right.";
                    gameNameBox.Text = string.Empty;
                    tagsBox.Text = string.Empty;
                    commentBox.Text = string.Empty;
                    photographyBox.IsChecked = false;
                    steamBox.IsChecked = false;
                    ps5Box.IsChecked = false;
                    xboxBox.IsChecked = false;
                    pcBox.IsChecked = false;
                    otherBox.IsChecked = false;
                    otherPlatformBox.Text = string.Empty;
                    useCustomTimeBox.IsChecked = false;
                    captureDatePicker.SelectedDate = null;
                    hourBox.SelectedIndex = -1;
                    minuteBox.SelectedIndex = -1;
                    ampmBox.SelectedIndex = -1;
                    if (importAndEditMode) deleteBeforeBox.IsChecked = false;
                    suppressSync = false;
                    refreshDateControls();
                    statusText.Text = defaultStatusText;
                    refreshTileSelectionState();
                    return;
                }

                suppressSync = true;
                if (selectedItems.Count == 1)
                {
                    var item = selectedItems[0];
                    selectedTitle.Text = item.FileName;
                    selectedMeta.Text = singleSelectionMetaPrefix + FormatFriendlyTimestamp(GetLibraryDate(item.FilePath));
                    guessText.Text = sharedFilenameGuess(selectedItems);
                    steamLookupStatus.Text = string.IsNullOrWhiteSpace(item.SteamAppId)
                        ? (IsSteamManualExportWithoutAppId(item.FileName) ? "Steam-style export detected. Search the game name to attach its AppID before import." : "Search by game name to fetch a Steam AppID, or paste one directly.")
                        : "Steam AppID " + item.SteamAppId + " will be saved with this import.";
                    previewBorder.Child = previewImage;
                    previewImage.Source = null;
                    QueueImageLoad(
                        previewImage,
                        item.FilePath,
                        1600,
                        delegate(BitmapImage loaded)
                        {
                            previewImage.Source = loaded;
                        },
                        true,
                        delegate
                        {
                            return previewBorder.Child == previewImage
                                && selectedItems.Count == 1
                                && ReferenceEquals(selectedItems[0], item);
                        });
                }
                else
                {
                    selectedTitle.Text = selectedItems.Count + " captures selected";
                    selectedMeta.Text = "Edits here apply to all selected files. Mixed values show as blank or indeterminate.";
                    guessText.Text = sharedFilenameGuess(selectedItems);
                    steamLookupStatus.Text = "Search by title or Steam AppID to apply one AppID to the selected captures, or paste it directly.";
                    previewBorder.Child = buildMultiPreview(selectedItems.Count);
                    previewImage.Source = null;
                }

                steamSearchBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item)
                {
                    return item.GameName;
                });
                steamAppIdBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.SteamAppId; });
                gameNameBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item)
                {
                    var displayLabel = BuildGameTitleChoiceLabel(item.GameName, DetermineManualMetadataPlatformLabel(item));
                    return knownGameChoiceSet.Contains(displayLabel) ? displayLabel : item.GameName;
                });
                tagsBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.TagText; });
                commentBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.Comment; });
                photographyBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.AddPhotographyTag; });
                useCustomTimeBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.UseCustomCaptureTime; });
                steamBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagSteam; });
                pcBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagPc; });
                ps5Box.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagPs5; });
                xboxBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagXbox; });
                otherBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.TagOther; });
                otherPlatformBox.Text = sharedText(selectedItems, delegate(ManualMetadataItem item) { return item.CustomPlatformTag; });
                if (importAndEditMode) deleteBeforeBox.IsChecked = sharedBool(selectedItems, delegate(ManualMetadataItem item) { return item.DeleteBeforeProcessing; });

                var first = selectedItems[0];
                captureDatePicker.SelectedDate = first.CaptureTime.Date;
                var hour12 = first.CaptureTime.Hour % 12;
                if (hour12 == 0) hour12 = 12;
                hourBox.SelectedItem = hour12.ToString();
                minuteBox.SelectedItem = first.CaptureTime.Minute.ToString("00");
                ampmBox.SelectedItem = first.CaptureTime.Hour >= 12 ? "PM" : "AM";
                suppressSync = false;
                refreshDateControls();

                refreshSelectionStatus();
                refreshTileSelectionState();
            };

            foreach (var item in items)
            {
                var tile = new ListBoxItem { Tag = item, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                var tileBorder = new Border { Background = Brush("#1A2329"), BorderBrush = Brush("#1A2329"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10) };
                tileBorders[item] = tileBorder;
                var tileStack = new StackPanel();
                var badge = new TextBlock { Text = "[Manual]", Foreground = Brush("#D0A15F"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
                badgeBlocks[item] = badge;
                tileStack.Children.Add(badge);
                tileStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
                tileStack.Children.Add(new TextBlock { Text = FormatFriendlyTimestamp(item.CaptureTime), Foreground = Brush("#B7C6C0"), Margin = new Thickness(0, 6, 0, 0) });
                tileBorder.Child = tileStack;
                tile.Content = tileBorder;
                fileList.Items.Add(tile);
            }
            refreshTileBadges();

            fileList.SelectionChanged += delegate
            {
                selectedItems = fileList.SelectedItems.Cast<ListBoxItem>().Where(i => i.Tag is ManualMetadataItem).Select(i => (ManualMetadataItem)i.Tag).ToList();
                refreshSelectionUi();
            };

            gameNameBox.SelectionChanged += delegate { syncSelectedGameNames(); };
            gameNameBox.LostKeyboardFocus += delegate { syncSelectedGameNames(); };
            gameNameBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(delegate(object sender, TextChangedEventArgs e)
            {
                syncSelectedGameNames();
            }));
            steamAppIdBox.TextChanged += delegate
            {
                syncSelectedSteamAppIds();
            };
            steamAppIdBox.LostKeyboardFocus += delegate
            {
                syncSelectedSteamAppIds();
            };
            steamSearchButton.Click += delegate
            {
                if (steamSearchCancellation != null)
                {
                    steamLookupStatus.Text = "Canceling Steam search...";
                    steamSearchCancellation.Cancel();
                    return;
                }
                if (selectedItems.Count == 0)
                {
                    MessageBox.Show("Select one or more captures before searching Steam.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var query = CleanTag(steamSearchBox.Text);
                string mappedName;
                if (knownGameChoiceNameMap.TryGetValue(query, out mappedName)) query = mappedName;
                else query = ExtractGameNameFromChoiceLabel(query);
                if (string.IsNullOrWhiteSpace(query))
                {
                    query = CleanTag(gameNameBox.Text);
                    if (knownGameChoiceNameMap.TryGetValue(query, out mappedName)) query = mappedName;
                    else query = ExtractGameNameFromChoiceLabel(query);
                }
                if (string.IsNullOrWhiteSpace(query))
                {
                    var firstItem = selectedItems[0];
                    if (firstItem != null && !string.IsNullOrWhiteSpace(firstItem.GameName)) query = CleanTag(firstItem.GameName);
                }
                if (string.IsNullOrWhiteSpace(query))
                {
                    MessageBox.Show("Enter a game title or a numeric Steam AppID in the search box, then click Search Steam.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                steamLookupStatus.Text = "Searching Steam for \"" + query + "\"...";
                var searchQuery = query;
                var searchCancellation = new CancellationTokenSource();
                steamSearchCancellation = searchCancellation;
                var searchVersion = ++steamSearchRequestVersion;
                steamSearchButton.Content = "Cancel Search";
                Task.Run(async () =>
                {
                    searchCancellation.Token.ThrowIfCancellationRequested();
                    var matches = await coverService.SearchSteamAppMatchesAsync(searchQuery, searchCancellation.Token).ConfigureAwait(false);
                    return Tuple.Create(searchQuery, matches);
                }, searchCancellation.Token).ContinueWith(delegate(Task<Tuple<string, List<Tuple<string, string>>>> searchTask)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (ReferenceEquals(steamSearchCancellation, searchCancellation))
                        {
                            steamSearchCancellation.Dispose();
                            steamSearchCancellation = null;
                        }
                        steamSearchButton.Content = "Search Steam";
                        if (!manualWindow.IsLoaded || searchVersion != steamSearchRequestVersion) return;
                        if (searchTask.IsCanceled || searchCancellation.IsCancellationRequested)
                        {
                            steamLookupStatus.Text = "Steam search canceled. Pick a match or paste the AppID directly.";
                            return;
                        }
                        if (searchTask.IsFaulted)
                        {
                            steamLookupStatus.Text = "Steam lookup failed. Try again or paste the AppID directly.";
                            return;
                        }
                        var result = searchTask.Result;
                        var matches = result == null || result.Item2 == null ? new List<Tuple<string, string>>() : result.Item2;
                        if (matches.Count == 0)
                        {
                            steamLookupStatus.Text = "No Steam AppID match found for \"" + searchQuery + "\".";
                            return;
                        }

                        var chosenMatch = matches.Count == 1 ? matches[0] : ShowSteamAppMatchWindow(searchQuery, matches);
                        if (chosenMatch == null)
                        {
                            steamLookupStatus.Text = "Steam search canceled. Pick a match or paste the AppID directly.";
                            return;
                        }

                        var resolvedAppId = chosenMatch.Item1 ?? string.Empty;
                        var resolvedTitle = chosenMatch.Item2 ?? string.Empty;

                        suppressSync = true;
                        steamSearchBox.Text = string.IsNullOrWhiteSpace(resolvedTitle) ? searchQuery : resolvedTitle;
                        steamAppIdBox.Text = resolvedAppId;
                        suppressSync = false;
                        foreach (var item in selectedItems)
                        {
                            item.SteamAppId = resolvedAppId;
                            if (!string.IsNullOrWhiteSpace(resolvedTitle)) item.GameName = resolvedTitle;
                        }
                        applyConsoleSelection(selectedItems, "Steam");
                        refreshTileBadges();
                        if (!string.IsNullOrWhiteSpace(resolvedTitle))
                        {
                            if (knownGameChoiceSet.Add(BuildGameTitleChoiceLabel(resolvedTitle, "Steam"))) knownGameChoices.Add(BuildGameTitleChoiceLabel(resolvedTitle, "Steam"));
                            refreshGameTitleChoices();
                            suppressSync = true;
                            gameNameBox.Text = resolvedTitle;
                            suppressSync = false;
                            syncSelectedGameNames();
                        }
                        else
                        {
                            steamLookupStatus.Text = "Selected Steam AppID " + resolvedAppId + ".";
                            refreshSelectionStatus();
                        }
                        refreshSelectionUi();
                    }));
                }, TaskScheduler.Default);
            };
            tagsBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.TagText = tagsBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionStatus();
            };
            commentBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.Comment = commentBox.Text;
                refreshSelectionStatus();
            };
            photographyBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.AddPhotographyTag = true;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionUi();
            };
            photographyBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.AddPhotographyTag = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionUi();
            };
            steamBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "Steam");
                refreshTileBadges();
                refreshSelectionUi();
            };
            steamBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (steamBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagSteam = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            pcBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "PC");
                refreshTileBadges();
                refreshSelectionUi();
            };
            pcBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (pcBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagPc = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            ps5Box.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "PS5");
                refreshTileBadges();
                refreshSelectionUi();
            };
            ps5Box.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (ps5Box.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagPs5 = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            xboxBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "Xbox");
                refreshTileBadges();
                refreshSelectionUi();
            };
            xboxBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                if (xboxBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagXbox = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherBox.Checked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                applyConsoleSelection(selectedItems, "Other");
                foreach (var item in selectedItems)
                {
                    item.CustomPlatformTag = otherPlatformBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherBox.Unchecked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                if (otherBox.IsChecked != false) return;
                foreach (var item in selectedItems)
                {
                    item.TagOther = false;
                    item.CustomPlatformTag = string.Empty;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            otherPlatformBox.TextChanged += delegate
            {
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems)
                {
                    item.CustomPlatformTag = otherPlatformBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionStatus();
            };
            useCustomTimeBox.Checked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.UseCustomCaptureTime = true;
                saveSelectedDateTime();
                refreshSelectionUi();
            };
            useCustomTimeBox.Unchecked += delegate
            {
                refreshDateControls();
                if (suppressSync || selectedItems.Count == 0) return;
                foreach (var item in selectedItems) item.UseCustomCaptureTime = false;
                refreshSelectionUi();
            };
            captureDatePicker.SelectedDateChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            hourBox.SelectionChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            minuteBox.SelectionChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            ampmBox.SelectionChanged += delegate { if (suppressSync || selectedItems.Count == 0 || useCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };

            deleteBeforeBox.Checked += delegate
            {
                if (suppressSync || selectedItems.Count == 0 || !importAndEditMode) return;
                foreach (var item in selectedItems) item.DeleteBeforeProcessing = true;
                refreshSelectionStatus();
            };
            deleteBeforeBox.Unchecked += delegate
            {
                if (suppressSync || selectedItems.Count == 0 || !importAndEditMode) return;
                foreach (var item in selectedItems) item.DeleteBeforeProcessing = false;
                refreshSelectionStatus();
            };

            finishButton.Click += async delegate
            {
                if (!dialogReady || !manualWindow.IsLoaded) return;
                var pendingItems = selectedItems.Distinct().ToList();
                if (pendingItems.Count == 0)
                {
                    MessageBox.Show(importService.GetManualMetadataFinishEmptySelectionMessage(libraryMode, importAndEditMode), "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (useCustomTimeBox.IsChecked == true) saveSelectedDateTime();
                importService.ApplyManualMetadataTagTextToPlatformFlags(pendingItems);
                if (importService.ManualMetadataItemsMissingOtherPlatformName(pendingItems))
                {
                    MessageBox.Show("Enter a platform name in the Other box before applying changes.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (importAndEditMode)
                {
                    await importService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync(pendingItems.Where(i => i != null && !i.DeleteBeforeProcessing), CancellationToken.None).ConfigureAwait(true);
                }
                var gameRows = LoadSavedGameIndexRows(libraryRoot);
                var unresolvedMasterRecords = importService.BuildUnresolvedManualMetadataMasterRecordLabels(gameRows, pendingItems);
                if (unresolvedMasterRecords.Count > 0)
                {
                    var addChoice = MessageBox.Show(
                        importService.BuildManualMetadataAddNewGamePrompt(unresolvedMasterRecords, 8),
                        "Add New Game",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question);
                    if (addChoice != MessageBoxResult.OK) return;
                    foreach (var title in unresolvedMasterRecords.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (knownGameChoiceSet.Add(title)) knownGameChoices.Add(title);
                    }
                    refreshGameTitleChoices();
                    importService.EnsureNewManualMetadataMasterRecordsInGameIndex(gameRows, pendingItems);
                }
                var confirm = MessageBox.Show(
                    importService.GetManualMetadataFinishConfirmBody(pendingItems.Count, libraryMode, importAndEditMode),
                    confirmCaption,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.OK) return;
                importService.FinalizeManualMetadataItemsAgainstGameIndex(libraryRoot, gameRows, pendingItems);
                items.Clear();
                items.AddRange(pendingItems);
                manualWindow.DialogResult = true;
                manualWindow.Close();
            };
            leaveButton.Click += delegate
            {
                if (!dialogReady || !manualWindow.IsLoaded) return;
                manualWindow.DialogResult = false;
                manualWindow.Close();
            };

            manualWindow.Content = root;
            manualWindow.Closing += delegate
            {
                if (steamSearchCancellation != null && !steamSearchCancellation.IsCancellationRequested)
                {
                    steamSearchCancellation.Cancel();
                }
            };
            manualWindow.Loaded += delegate
            {
                dialogReady = true;
                if (items.Count > 0)
                {
                    if (libraryMode && !importAndEditMode)
                    {
                        var firstEntry = fileList.Items.Cast<ListBoxItem>().FirstOrDefault();
                        if (firstEntry != null) firstEntry.IsSelected = true;
                    }
                    else
                    {
                        foreach (ListBoxItem entry in fileList.Items) entry.IsSelected = true;
                    }
                }
                else
                {
                    refreshSelectionUi();
                }
            };
            var result = manualWindow.ShowDialog();
            return result == true;
        }
    }
}
