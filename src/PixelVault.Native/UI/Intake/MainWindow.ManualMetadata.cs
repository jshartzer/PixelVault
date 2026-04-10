using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        ListBoxItem BuildManualMetadataListTile(ManualMetadataDialogHost h, ManualMetadataItem item)
        {
            var tile = new ListBoxItem { Tag = item, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            var tileBorder = new Border { Background = Brush("#1A2329"), BorderBrush = Brush("#1A2329"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10) };
            h.TileBorders[item] = tileBorder;
            var tileStack = new StackPanel();
            var badge = new TextBlock { Text = "[Manual]", Foreground = Brush("#D0A15F"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            h.BadgeBlocks[item] = badge;
            tileStack.Children.Add(badge);
            tileStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
            tileStack.Children.Add(new TextBlock { Text = FormatFriendlyTimestamp(item.CaptureTime), Foreground = Brush("#B7C6C0"), Margin = new Thickness(0, 6, 0, 0) });
            tileBorder.Child = tileStack;
            tile.Content = tileBorder;
            return tile;
        }

        void RebuildManualMetadataFileList(ManualMetadataDialogHost h)
        {
            h.FileList.Items.Clear();
            h.BadgeBlocks.Clear();
            h.TileBorders.Clear();
            foreach (var item in h.Items)
                h.FileList.Items.Add(BuildManualMetadataListTile(h, item));
        }

        /// <summary>After applying library metadata to a batch: remove those files from the dialog list and keep editing. Returns false if the list is empty (caller should close).</summary>
        bool ContinueManualMetadataAfterLibraryApply(ManualMetadataDialogHost h, List<ManualMetadataItem> pendingItems, Action refreshGameTitleChoices, Action refreshSelectionUi, Action refreshTileBadges)
        {
            var paths = new HashSet<string>(pendingItems.Select(p => p.FilePath), StringComparer.OrdinalIgnoreCase);
            for (int i = h.Items.Count - 1; i >= 0; i--)
            {
                if (paths.Contains(h.Items[i].FilePath)) h.Items.RemoveAt(i);
            }
            var distinctLabels = pendingItems
                .Select(it => NormalizeGameIndexName(it.GameName, null))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctLabels.Count > 0) PushManualMetadataRecentTitleLabels(distinctLabels);
            RebuildManualMetadataFileList(h);
            refreshGameTitleChoices();
            refreshTileBadges();
            if (h.Items.Count == 0) return false;
            foreach (ListBoxItem entry in h.FileList.Items) entry.IsSelected = false;
            var first = h.FileList.Items.Cast<ListBoxItem>().FirstOrDefault();
            if (first != null) first.IsSelected = true;
            refreshSelectionUi();
            return true;
        }

        bool ShowManualMetadataWindow(List<ManualMetadataItem> items, bool libraryMode, string contextName, bool importAndEditMode = false)
        {
            if (items == null || items.Count == 0) return true;

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

            var h = new ManualMetadataDialogHost
            {
                Items = items,
                LibraryMode = libraryMode,
                ImportAndEditMode = importAndEditMode,
                UseFlexiblePreview = libraryMode || importAndEditMode,
                EmptySelectionText = emptySelectionText,
                DefaultStatusText = defaultStatusText,
                SingleSelectionMetaPrefix = singleSelectionMetaPrefix,
                ConfirmCaption = confirmCaption
            };

            BuildManualMetadataDialogLayout(h, windowLabel, headerTitleText, headerDescriptionText, leaveButtonText, finishButtonText);

            Action refreshSelectionStatus = null;
            Action syncSelectedSteamAppIds = null;
            Action refreshGameTitleChoices = delegate
            {
                var version = ++h.GameTitleChoicesRefreshVersion;
                var libRoot = libraryRoot;
                importService.LoadManualMetadataGameTitleRowsAsync(libRoot, System.Threading.CancellationToken.None).ContinueWith(delegate(System.Threading.Tasks.Task<List<GameIndexEditorRow>> loadTask)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (version != h.GameTitleChoicesRefreshVersion) return;
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
                        h.GameTitleIndexRows = rows;
                        var loadedChoices = rows
                            .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                            .Select(row => NormalizeGameIndexName(row.Name, row.FolderPath))
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        foreach (var recent in GetManualMetadataRecentTitleLabelsList())
                        {
                            var recentName = NormalizeGameIndexName(ExtractGameNameFromChoiceLabel(recent), null);
                            if (string.IsNullOrWhiteSpace(recentName)) continue;
                            if (loadedChoices.Contains(recentName, StringComparer.OrdinalIgnoreCase)) continue;
                            loadedChoices.Insert(0, recentName);
                        }
                        foreach (var extraChoice in h.KnownGameChoices.Where(label => !string.IsNullOrWhiteSpace(label)))
                        {
                            if (!loadedChoices.Contains(extraChoice, StringComparer.OrdinalIgnoreCase)) loadedChoices.Add(extraChoice);
                        }
                        h.KnownGameChoices = loadedChoices;
                        h.KnownGameChoiceSet = new HashSet<string>(h.KnownGameChoices, StringComparer.OrdinalIgnoreCase);
                        h.KnownGameChoiceNameMap = rows
                            .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                            .Select(row => NormalizeGameIndexName(row.Name, row.FolderPath))
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(name => CleanTag(name), name => name, StringComparer.OrdinalIgnoreCase);
                        foreach (var extraChoice in h.KnownGameChoices)
                        {
                            var normalizedChoice = CleanTag(extraChoice);
                            if (h.KnownGameChoiceNameMap.ContainsKey(normalizedChoice)) continue;
                            var extraName = ExtractGameNameFromChoiceLabel(extraChoice);
                            if (!string.IsNullOrWhiteSpace(extraName)) h.KnownGameChoiceNameMap[normalizedChoice] = extraName;
                        }
                        var restoreText = h.GameNameBox.Text;
                        h.GameNameBox.ItemsSource = null;
                        h.GameNameBox.ItemsSource = h.KnownGameChoices;
                        h.GameNameBox.Text = restoreText;
                        SyncManualMetadataGameIdsFromGameTitleIndexRows(h, h.SelectedItems);
                    }));
                }, System.Threading.Tasks.TaskScheduler.Default);
            };
            Action syncSelectedGameNames = delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                var selectedTitleText = CleanTag(h.GameNameBox.Text);
                string mappedName;
                if (h.KnownGameChoiceNameMap.TryGetValue(selectedTitleText, out mappedName)) selectedTitleText = mappedName;
                else selectedTitleText = ExtractGameNameFromChoiceLabel(selectedTitleText);
                foreach (var item in h.SelectedItems) item.GameName = selectedTitleText;
                refreshSelectionStatus();
            };
            syncSelectedSteamAppIds = delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                var cleanedAppId = Regex.Replace((h.SteamAppIdBox.Text ?? string.Empty).Trim(), @"[^\d]", string.Empty);
                if (!string.Equals(h.SteamAppIdBox.Text ?? string.Empty, cleanedAppId, StringComparison.Ordinal))
                {
                    h.SuppressSync = true;
                    h.SteamAppIdBox.Text = cleanedAppId;
                    h.SteamAppIdBox.SelectionStart = h.SteamAppIdBox.Text.Length;
                    h.SuppressSync = false;
                }
                foreach (var item in h.SelectedItems) item.SteamAppId = cleanedAppId;
                if (!string.IsNullOrWhiteSpace(cleanedAppId))
                {
                    foreach (var item in h.SelectedItems)
                    {
                        item.TagSteam = true;
                        item.TagPc = false;
                        item.TagEmulation = false;
                        item.TagPs5 = false;
                        item.TagXbox = false;
                        item.TagOther = false;
                        item.CustomPlatformTag = string.Empty;
                        item.ForceTagMetadataWrite = true;
                    }
                }
                refreshSelectionStatus();
            };

            Action refreshDateControls = delegate
            {
                var enabled = h.UseCustomTimeBox.IsChecked == true;
                h.CaptureDatePicker.IsEnabled = enabled;
                h.HourBox.IsEnabled = enabled;
                h.MinuteBox.IsEnabled = enabled;
                h.AmpmBox.IsEnabled = enabled;
                h.OtherPlatformBox.IsEnabled = h.OtherBox.IsChecked == true;
            };

            Action refreshTileBadges = delegate
            {
                foreach (var pair in h.BadgeBlocks)
                {
                    var label = GetManualMetadataBadgeLabel(pair.Key);
                    pair.Value.Text = "[" + label + "]";
                    pair.Value.Foreground = GetManualMetadataBadgeBrush(label);
                }
            };

            Action refreshTileSelectionState = delegate
            {
                foreach (var pair in h.TileBorders)
                {
                    var isSelected = h.SelectedItems.Contains(pair.Key);
                    pair.Value.Background = isSelected ? Brush("#24323C") : Brush("#1A2329");
                    pair.Value.BorderBrush = isSelected ? Brush("#69A7FF") : Brush("#1A2329");
                    pair.Value.BorderThickness = new Thickness(isSelected ? 2 : 1);
                }
            };

            Action saveSelectedDateTime = delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0 || h.UseCustomTimeBox.IsChecked != true) return;
                var fallback = h.SelectedItems[0].CaptureTime;
                var date = h.CaptureDatePicker.SelectedDate ?? fallback.Date;
                int hour12 = ParseInt(Convert.ToString(h.HourBox.SelectedItem));
                if (hour12 < 1 || hour12 > 12)
                {
                    var fallbackHour = fallback.Hour % 12;
                    hour12 = fallbackHour == 0 ? 12 : fallbackHour;
                }
                int minute = ParseInt(Convert.ToString(h.MinuteBox.SelectedItem));
                var ampm = Convert.ToString(h.AmpmBox.SelectedItem);
                if (string.IsNullOrWhiteSpace(ampm)) ampm = fallback.Hour >= 12 ? "PM" : "AM";
                int hour24 = hour12 % 12;
                if (string.Equals(ampm, "PM", StringComparison.OrdinalIgnoreCase)) hour24 += 12;
                var newTime = new DateTime(date.Year, date.Month, date.Day, hour24, minute, 0);
                foreach (var item in h.SelectedItems) item.CaptureTime = newTime;
            };
            refreshSelectionStatus = delegate
            {
                var notes = new List<string>();
                if (h.SelectedItems.Count > 1) notes.Add(h.SelectedItems.Count + " files selected");
                if (!string.IsNullOrWhiteSpace(h.GameNameBox.Text)) notes.Add("rename prefix ready");
                if (!string.IsNullOrWhiteSpace(h.SteamAppIdBox.Text)) notes.Add("Steam AppID " + h.SteamAppIdBox.Text);
                if (!string.IsNullOrWhiteSpace(h.CommentBox.Text)) notes.Add("comment saved");
                var tagCount = ParseTagText(h.TagsBox.Text).Length;
                if (tagCount > 0) notes.Add(tagCount + " extra tag(s)");
                if (h.UseCustomTimeBox.IsChecked == true) notes.Add("custom capture time");
                if (h.PhotographyBox.IsChecked == true) notes.Add(GamePhotographyTag + " tag enabled");
                if (h.SteamBox.IsChecked == true) notes.Add("platform tag: Steam");
                else if (h.PcBox.IsChecked == true) notes.Add("platform tag: PC");
                else if (h.EmulationBox.IsChecked == true) notes.Add("platform tag: Emulation");
                else if (h.Ps5Box.IsChecked == true) notes.Add("platform tag: PS5");
                else if (h.XboxBox.IsChecked == true) notes.Add("platform tag: Xbox");
                else if (h.OtherBox.IsChecked == true) notes.Add("platform tag: " + CleanTag(h.OtherPlatformBox.Text));
                if (h.ImportAndEditMode && h.SelectedItems.Count > 0)
                {
                    var del = h.SelectedItems.Count(i => i.DeleteBeforeProcessing);
                    if (del > 0) notes.Add(del + " marked for delete before import");
                }
                h.StatusText.Text = notes.Count == 0 ? h.DefaultStatusText : string.Join(" | ", notes.ToArray()) + ".";
                if (!h.SuppressSync && h.SelectedItems != null && h.SelectedItems.Count > 0)
                    SyncManualMetadataGameIdsFromGameTitleIndexRows(h, h.SelectedItems);
            };
            refreshGameTitleChoices();

            Action refreshSelectionUi = delegate
            {
                if (h.SelectedItems.Count == 0)
                {
                    h.SelectedTitle.Text = "Select one or more captures";
                    h.SelectedMeta.Text = h.EmptySelectionText;
                    h.GuessText.Text = "Best Guess | No confident match";
                    h.PreviewBorder.Child = BuildManualMetadataMultiPreviewStack(0, h.UseFlexiblePreview, h.PreviewImage.MaxHeight);
                    h.SuppressSync = true;
                    h.SteamSearchBox.Text = string.Empty;
                    h.SteamAppIdBox.Text = string.Empty;
                    h.SteamLookupStatus.Text = "Search by game title or numeric Steam AppID, or paste an AppID in the box on the right.";
                    h.GameNameBox.Text = string.Empty;
                    h.TagsBox.Text = string.Empty;
                    h.CommentBox.Text = string.Empty;
                    h.PhotographyBox.IsChecked = false;
                    h.SteamBox.IsChecked = false;
                    h.Ps5Box.IsChecked = false;
                    h.XboxBox.IsChecked = false;
                    h.PcBox.IsChecked = false;
                    h.EmulationBox.IsChecked = false;
                    h.OtherBox.IsChecked = false;
                    h.OtherPlatformBox.Text = string.Empty;
                    h.UseCustomTimeBox.IsChecked = false;
                    h.CaptureDatePicker.SelectedDate = null;
                    h.HourBox.SelectedIndex = -1;
                    h.MinuteBox.SelectedIndex = -1;
                    h.AmpmBox.SelectedIndex = -1;
                    if (h.ImportAndEditMode) h.DeleteBeforeBox.IsChecked = false;
                    h.SuppressSync = false;
                    refreshDateControls();
                    h.StatusText.Text = h.DefaultStatusText;
                    refreshTileSelectionState();
                    return;
                }

                h.SuppressSync = true;
                if (h.SelectedItems.Count == 1)
                {
                    var item = h.SelectedItems[0];
                    h.SelectedTitle.Text = item.FileName;
                    h.SelectedMeta.Text = h.SingleSelectionMetaPrefix + FormatFriendlyTimestamp(GetLibraryDate(item.FilePath));
                    h.GuessText.Text = GetManualMetadataFilenameGuessSummary(h.SelectedItems);
                    h.SteamLookupStatus.Text = !string.IsNullOrWhiteSpace(item.NonSteamId)
                        ? "Non-Steam ID " + item.NonSteamId + " will be saved with this game record."
                        : string.IsNullOrWhiteSpace(item.SteamAppId)
                            ? (IsSteamManualExportWithoutAppId(item.FileName) ? "Steam-style export detected. Search the game name to attach its AppID before import." : "Search by game name to fetch a Steam AppID, or paste one directly.")
                            : "Steam AppID " + item.SteamAppId + " will be saved with this import.";
                    h.PreviewBorder.Child = h.PreviewImage;
                    h.PreviewImage.Source = null;
                    QueueImageLoad(
                        h.PreviewImage,
                        item.FilePath,
                        1600,
                        delegate(BitmapImage loaded)
                        {
                            h.PreviewImage.Source = loaded;
                        },
                        true,
                        delegate
                        {
                            return h.PreviewBorder.Child == h.PreviewImage
                                && h.SelectedItems.Count == 1
                                && ReferenceEquals(h.SelectedItems[0], item);
                        });
                }
                else
                {
                    h.SelectedTitle.Text = h.SelectedItems.Count + " captures selected";
                    h.SelectedMeta.Text = "Edits here apply to all selected files. Mixed values show as blank or indeterminate.";
                    h.GuessText.Text = GetManualMetadataFilenameGuessSummary(h.SelectedItems);
                    h.SteamLookupStatus.Text = "Search by title or Steam AppID to apply one AppID to the selected captures, or paste it directly.";
                    h.PreviewBorder.Child = BuildManualMetadataMultiPreviewStack(h.SelectedItems.Count, h.UseFlexiblePreview, h.PreviewImage.MaxHeight);
                    h.PreviewImage.Source = null;
                }

                h.SteamSearchBox.Text = GetSharedManualMetadataFieldText(h.SelectedItems, delegate(ManualMetadataItem item) { return item.GameName; });
                h.SteamAppIdBox.Text = GetSharedManualMetadataFieldText(h.SelectedItems, delegate(ManualMetadataItem item) { return item.SteamAppId; });
                h.GameNameBox.Text = GetSharedManualMetadataFieldText(h.SelectedItems, delegate(ManualMetadataItem item)
                {
                    var n = NormalizeGameIndexName(item.GameName, null);
                    return !string.IsNullOrWhiteSpace(n) && h.KnownGameChoiceSet.Contains(n) ? n : (item.GameName ?? string.Empty);
                });
                h.TagsBox.Text = GetSharedManualMetadataFieldText(h.SelectedItems, delegate(ManualMetadataItem item) { return item.TagText; });
                h.CommentBox.Text = GetSharedManualMetadataFieldText(h.SelectedItems, delegate(ManualMetadataItem item) { return item.Comment; });
                h.PhotographyBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.AddPhotographyTag; });
                h.UseCustomTimeBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.UseCustomCaptureTime; });
                h.SteamBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.TagSteam; });
                h.PcBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.TagPc; });
                h.EmulationBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.TagEmulation; });
                h.Ps5Box.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.TagPs5; });
                h.XboxBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.TagXbox; });
                h.OtherBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.TagOther; });
                h.OtherPlatformBox.Text = GetSharedManualMetadataFieldText(h.SelectedItems, delegate(ManualMetadataItem item) { return item.CustomPlatformTag; });
                if (h.ImportAndEditMode) h.DeleteBeforeBox.IsChecked = GetSharedManualMetadataFieldBool(h.SelectedItems, delegate(ManualMetadataItem item) { return item.DeleteBeforeProcessing; });

                var first = h.SelectedItems[0];
                h.CaptureDatePicker.SelectedDate = first.CaptureTime.Date;
                var hour12Val = first.CaptureTime.Hour % 12;
                if (hour12Val == 0) hour12Val = 12;
                h.HourBox.SelectedItem = hour12Val.ToString();
                h.MinuteBox.SelectedItem = first.CaptureTime.Minute.ToString("00");
                h.AmpmBox.SelectedItem = first.CaptureTime.Hour >= 12 ? "PM" : "AM";
                h.SuppressSync = false;
                refreshDateControls();

                refreshSelectionStatus();
                refreshTileSelectionState();
            };

            foreach (var item in items)
                h.FileList.Items.Add(BuildManualMetadataListTile(h, item));
            refreshTileBadges();

            var fileListMenu = new ContextMenu();
            var copyPathItem = new MenuItem { Header = "Copy file path" };
            copyPathItem.Click += delegate
            {
                try
                {
                    var paths = h.FileList.SelectedItems.Cast<ListBoxItem>().Select(i => i.Tag as ManualMetadataItem).Where(m => m != null).Select(m => m.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (paths.Count == 0) return;
                    Clipboard.SetText(paths.Count == 1 ? paths[0] : string.Join(Environment.NewLine, paths));
                }
                catch (Exception ex)
                {
                    Log("Copy file path failed. " + ex.Message);
                }
            };
            var openFolderItem = new MenuItem { Header = "Open containing folder" };
            openFolderItem.Click += delegate
            {
                foreach (var path in h.FileList.SelectedItems.Cast<ListBoxItem>().Select(i => i.Tag as ManualMetadataItem).Where(m => m != null).Select(m => m.FilePath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir)) OpenFolder(dir);
                }
            };
            fileListMenu.Items.Add(copyPathItem);
            fileListMenu.Items.Add(openFolderItem);
            h.FileList.ContextMenu = fileListMenu;

            h.FileList.SelectionChanged += delegate
            {
                h.SelectedItems = h.FileList.SelectedItems.Cast<ListBoxItem>().Where(i => i.Tag is ManualMetadataItem).Select(i => (ManualMetadataItem)i.Tag).ToList();
                refreshSelectionUi();
            };

            h.GameNameBox.SelectionChanged += delegate { syncSelectedGameNames(); };
            h.GameNameBox.LostKeyboardFocus += delegate { syncSelectedGameNames(); };
            h.GameNameBox.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(delegate { syncSelectedGameNames(); }));
            h.SteamAppIdBox.TextChanged += delegate { syncSelectedSteamAppIds(); };
            h.SteamAppIdBox.LostKeyboardFocus += delegate { syncSelectedSteamAppIds(); };

            h.SameAsPreviousButton.Click += delegate
            {
                if (h.SelectedItems == null || h.SelectedItems.Count == 0 || h.Items == null || h.Items.Count < 2)
                {
                    TryLibraryToast("Select one or more files. There must be a previous file in the list to copy from.");
                    return;
                }
                var ordered = h.SelectedItems
                    .Select(it => new { Item = it, Index = h.Items.IndexOf(it) })
                    .Where(x => x.Index >= 0)
                    .OrderBy(x => x.Index)
                    .ToList();
                if (ordered.Count == 0) return;
                if (ordered[0].Index < 1)
                {
                    TryLibraryToast("There is no previous file above the current selection in the list.");
                    return;
                }
                foreach (var x in ordered)
                    CopyManualMetadataItemFromAnother(h.Items[x.Index - 1], x.Item);
                refreshTileBadges();
                refreshSelectionUi();
            };

            AttachManualMetadataSteamSearchHandler(h, refreshGameTitleChoices, syncSelectedGameNames, refreshTileBadges, refreshSelectionStatus, refreshSelectionUi);

            h.TagsBox.TextChanged += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                foreach (var item in h.SelectedItems)
                {
                    item.TagText = h.TagsBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionStatus();
            };
            h.CommentBox.TextChanged += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                foreach (var item in h.SelectedItems) item.Comment = h.CommentBox.Text;
                refreshSelectionStatus();
            };
            h.PhotographyBox.Checked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                foreach (var item in h.SelectedItems)
                {
                    item.AddPhotographyTag = true;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionUi();
            };
            h.PhotographyBox.Unchecked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                foreach (var item in h.SelectedItems)
                {
                    item.AddPhotographyTag = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshSelectionUi();
            };
            h.SteamBox.Checked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                ApplyConsolePlatformToManualMetadataItems(h.SelectedItems, "Steam");
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.SteamBox.Unchecked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                if (h.SteamBox.IsChecked != false) return;
                foreach (var item in h.SelectedItems)
                {
                    item.TagSteam = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.PcBox.Checked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                ApplyConsolePlatformToManualMetadataItems(h.SelectedItems, "PC");
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.PcBox.Unchecked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                if (h.PcBox.IsChecked != false) return;
                foreach (var item in h.SelectedItems)
                {
                    item.TagPc = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.EmulationBox.Checked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                ApplyConsolePlatformToManualMetadataItems(h.SelectedItems, "Emulation");
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.EmulationBox.Unchecked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                if (h.EmulationBox.IsChecked != false) return;
                foreach (var item in h.SelectedItems)
                {
                    item.TagEmulation = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.Ps5Box.Checked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                ApplyConsolePlatformToManualMetadataItems(h.SelectedItems, "PS5");
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.Ps5Box.Unchecked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                if (h.Ps5Box.IsChecked != false) return;
                foreach (var item in h.SelectedItems)
                {
                    item.TagPs5 = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.XboxBox.Checked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                ApplyConsolePlatformToManualMetadataItems(h.SelectedItems, "Xbox");
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.XboxBox.Unchecked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                if (h.XboxBox.IsChecked != false) return;
                foreach (var item in h.SelectedItems)
                {
                    item.TagXbox = false;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.OtherBox.Checked += delegate
            {
                refreshDateControls();
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                ApplyConsolePlatformToManualMetadataItems(h.SelectedItems, "Other");
                foreach (var item in h.SelectedItems)
                {
                    item.CustomPlatformTag = h.OtherPlatformBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.OtherBox.Unchecked += delegate
            {
                refreshDateControls();
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                if (h.OtherBox.IsChecked != false) return;
                foreach (var item in h.SelectedItems)
                {
                    item.TagOther = false;
                    item.CustomPlatformTag = string.Empty;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionUi();
            };
            h.OtherPlatformBox.TextChanged += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                foreach (var item in h.SelectedItems)
                {
                    item.CustomPlatformTag = h.OtherPlatformBox.Text;
                    item.ForceTagMetadataWrite = true;
                }
                refreshTileBadges();
                refreshSelectionStatus();
            };
            h.UseCustomTimeBox.Checked += delegate
            {
                refreshDateControls();
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                foreach (var item in h.SelectedItems) item.UseCustomCaptureTime = true;
                saveSelectedDateTime();
                refreshSelectionUi();
            };
            h.UseCustomTimeBox.Unchecked += delegate
            {
                refreshDateControls();
                if (h.SuppressSync || h.SelectedItems.Count == 0) return;
                foreach (var item in h.SelectedItems) item.UseCustomCaptureTime = false;
                refreshSelectionUi();
            };
            h.CaptureDatePicker.SelectedDateChanged += delegate { if (h.SuppressSync || h.SelectedItems.Count == 0 || h.UseCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            h.HourBox.SelectionChanged += delegate { if (h.SuppressSync || h.SelectedItems.Count == 0 || h.UseCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            h.MinuteBox.SelectionChanged += delegate { if (h.SuppressSync || h.SelectedItems.Count == 0 || h.UseCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };
            h.AmpmBox.SelectionChanged += delegate { if (h.SuppressSync || h.SelectedItems.Count == 0 || h.UseCustomTimeBox.IsChecked != true) return; saveSelectedDateTime(); refreshSelectionUi(); };

            h.DeleteBeforeBox.Checked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0 || !h.ImportAndEditMode) return;
                foreach (var item in h.SelectedItems) item.DeleteBeforeProcessing = true;
                refreshSelectionStatus();
            };
            h.DeleteBeforeBox.Unchecked += delegate
            {
                if (h.SuppressSync || h.SelectedItems.Count == 0 || !h.ImportAndEditMode) return;
                foreach (var item in h.SelectedItems) item.DeleteBeforeProcessing = false;
                refreshSelectionStatus();
            };

            Action flushPendingFieldEditsToSelectedItems = delegate
            {
                if (h.SelectedItems == null || h.SelectedItems.Count == 0) return;
                var wasSuppress = h.SuppressSync;
                h.SuppressSync = false;
                try
                {
                    syncSelectedGameNames();
                    syncSelectedSteamAppIds();
                    foreach (var item in h.SelectedItems)
                    {
                        item.TagText = h.TagsBox.Text ?? string.Empty;
                        item.Comment = h.CommentBox.Text ?? string.Empty;
                        if (ManualMetadataTouchesTags(item)) item.ForceTagMetadataWrite = true;
                    }
                }
                finally
                {
                    h.SuppressSync = wasSuppress;
                }
            };

            AttachManualMetadataFinishHandler(h, flushPendingFieldEditsToSelectedItems, saveSelectedDateTime, refreshGameTitleChoices, refreshSelectionUi, refreshTileBadges);

            h.LeaveButton.Click += delegate
            {
                if (!h.DialogReady || !h.ManualWindow.IsLoaded) return;
                h.ManualWindow.DialogResult = false;
                h.ManualWindow.Close();
            };

            h.ManualWindow.Closing += delegate
            {
                if (h.SteamSearchCancellation != null && !h.SteamSearchCancellation.IsCancellationRequested)
                {
                    h.SteamSearchCancellation.Cancel();
                }
            };
            h.ManualWindow.Loaded += delegate
            {
                h.DialogReady = true;
                if (items.Count > 0)
                {
                    if (libraryMode && !importAndEditMode)
                    {
                        var firstEntry = h.FileList.Items.Cast<ListBoxItem>().FirstOrDefault();
                        if (firstEntry != null) firstEntry.IsSelected = true;
                    }
                    else
                    {
                        foreach (ListBoxItem entry in h.FileList.Items) entry.IsSelected = true;
                    }
                }
                else
                {
                    refreshSelectionUi();
                }
            };

            var result = h.ManualWindow.ShowDialog();
            return result == true;
        }
    }
}
