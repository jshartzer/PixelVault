using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        enum LibraryAssetPickerKind
        {
            Cover,
            Banner,
            LogoIcon
        }

        async Task ChooseLibraryAssetFromSteamGridDbAsync(
            Window owner,
            LibraryBrowserFolderView view,
            LibraryFolderInfo lookupFolder,
            List<LibraryFolderInfo> targetFolders,
            LibraryAssetPickerKind pickerKind,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action refreshPhotoWorkspaceHeroBanner,
            Action<string> libraryToast)
        {
            if (lookupFolder == null || targetFolders == null || targetFolders.Count == 0) return;
            if (!HasSteamGridDbApiToken())
            {
                libraryToast?.Invoke("SteamGridDB token required");
                return;
            }

            var selection = ShowSteamGridDbAssetPickerWindow(
                owner ?? this,
                BuildLibraryAssetPickerWindowTitle(pickerKind),
                BuildLibraryAssetPickerWindowSubtitle(view, pickerKind),
                cancellationToken => LoadSteamGridDbAssetChoicesForPickerAsync(lookupFolder, pickerKind, cancellationToken),
                lookupFolder == null || (view != null && view.IsMergedAcrossPlatforms)
                    ? null
                    : (Action<Window>)(pickerOwner => OpenLibraryFolderIdEditor(
                        lookupFolder,
                        delegate
                        {
                            showFolder?.Invoke(view);
                            renderTiles?.Invoke();
                            refreshPhotoWorkspaceHeroBanner?.Invoke();
                        },
                        pickerOwner)));
            if (selection == null) return;

            string tempPath = null;
            try
            {
                tempPath = await DownloadSteamGridDbAssetChoiceToTempFileAsync(selection, CancellationToken.None).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath))
                {
                    libraryToast?.Invoke("Could not download selected asset");
                    return;
                }

                foreach (var folder in targetFolders.Where(folder => folder != null))
                {
                    switch (pickerKind)
                    {
                        case LibraryAssetPickerKind.Cover:
                            SaveCustomCover(folder, tempPath);
                            break;
                        case LibraryAssetPickerKind.Banner:
                            SaveCustomHero(folder, tempPath);
                            break;
                        default:
                            SaveCustomLogo(folder, tempPath);
                            break;
                    }
                }

                showFolder?.Invoke(view);
                if (pickerKind == LibraryAssetPickerKind.Cover)
                {
                    renderTiles?.Invoke();
                    libraryToast?.Invoke("Cover saved");
                    Log("SteamGridDB cover chosen for " + BuildLibraryBrowserActionScopeLabel(view) + ".");
                }
                else if (pickerKind == LibraryAssetPickerKind.Banner)
                {
                    refreshPhotoWorkspaceHeroBanner?.Invoke();
                    libraryToast?.Invoke("Banner saved");
                    Log("SteamGridDB banner chosen for " + BuildLibraryBrowserActionScopeLabel(view) + ".");
                }
                else
                {
                    refreshPhotoWorkspaceHeroBanner?.Invoke();
                    libraryToast?.Invoke("Logo saved");
                    Log("SteamGridDB " + (selection.AssetKind ?? "logo").ToLowerInvariant() + " chosen for " + BuildLibraryBrowserActionScopeLabel(view) + ".");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogException("Choose SteamGridDB " + BuildLibraryAssetPickerWindowTitle(pickerKind) + " | " + (view == null ? "?" : (view.Name ?? "?")), ex);
                libraryToast?.Invoke("Asset selection failed");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        async Task<List<SteamGridDbAssetChoice>> LoadSteamGridDbAssetChoicesForPickerAsync(LibraryFolderInfo lookupFolder, LibraryAssetPickerKind pickerKind, CancellationToken cancellationToken)
        {
            var choices = new List<SteamGridDbAssetChoice>();
            if (lookupFolder == null) return choices;
            var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(libraryRoot, lookupFolder, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(steamGridDbId)) return choices;

            switch (pickerKind)
            {
                case LibraryAssetPickerKind.Cover:
                    choices = await coverService.GetSteamGridDbCoverChoicesAsync(steamGridDbId, cancellationToken).ConfigureAwait(false);
                    break;
                case LibraryAssetPickerKind.Banner:
                    choices = await coverService.GetSteamGridDbHeroChoicesAsync(steamGridDbId, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    var logos = await coverService.GetSteamGridDbLogoChoicesAsync(steamGridDbId, cancellationToken).ConfigureAwait(false);
                    var icons = await coverService.GetSteamGridDbIconChoicesAsync(steamGridDbId, cancellationToken).ConfigureAwait(false);
                    choices = logos
                        .Concat(icons)
                        .GroupBy(choice => (choice == null ? string.Empty : (choice.DownloadUrl ?? string.Empty)).Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .OrderBy(choice => string.Equals(choice.AssetKind ?? string.Empty, "Logo", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenByDescending(choice => choice.Score)
                        .ThenBy(choice => choice.Style ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
            }

            return choices.Where(choice => choice != null && !string.IsNullOrWhiteSpace(choice.DownloadUrl)).ToList();
        }

        SteamGridDbAssetChoice ShowSteamGridDbAssetPickerWindow(
            Window owner,
            string title,
            string subtitle,
            Func<CancellationToken, Task<List<SteamGridDbAssetChoice>>> loadChoicesAsync,
            Action<Window> openIdEditor = null)
        {
            if (loadChoicesAsync == null) return null;

            const int pageSize = 10;
            SteamGridDbAssetChoice selected = null;
            var loadCts = new CancellationTokenSource();
            var allChoices = new List<SteamGridDbAssetChoice>();
            var filteredChoices = new List<SteamGridDbAssetChoice>();
            var currentPageIndex = 0;
            var isLoading = false;

            var window = new Window
            {
                Title = "PixelVault " + AppVersion + " " + title,
                Width = 1120,
                Height = 780,
                MinWidth = 900,
                MinHeight = 620,
                Owner = owner ?? this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Border
            {
                Background = Brush("#161C20"),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = Brush("#B7C6C0"),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            });
            header.Child = headerStack;
            root.Children.Add(header);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var listHost = new Grid();
            listHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            listHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.Children.Add(listHost);

            var filterShell = new Border
            {
                Background = Brush("#161C20"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var filterStack = new StackPanel();
            var searchBox = new TextBox
            {
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brush("#0F1519"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                FontSize = 13.5,
                ToolTip = "Search by asset type, style, language, author, size, mime, score, or asset ID."
            };
            filterStack.Children.Add(searchBox);

            ComboBox BuildFilterCombo(double minWidth, string toolTip)
            {
                return new ComboBox
                {
                    MinWidth = minWidth,
                    Height = 32,
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = toolTip
                };
            }

            var filterRow = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var kindFilter = BuildFilterCombo(102, "Filter by asset type");
            var styleFilter = BuildFilterCombo(118, "Filter by SteamGridDB style");
            var languageFilter = BuildFilterCombo(118, "Filter by language");
            var clearFiltersButton = Btn("Clear Filters", null, "#20343A", Brushes.White);
            clearFiltersButton.Height = 32;
            clearFiltersButton.Padding = new Thickness(12, 0, 12, 0);
            clearFiltersButton.Margin = new Thickness(0);
            filterRow.Children.Add(kindFilter);
            filterRow.Children.Add(styleFilter);
            filterRow.Children.Add(languageFilter);
            filterRow.Children.Add(clearFiltersButton);
            filterStack.Children.Add(filterRow);
            filterShell.Child = filterStack;
            listHost.Children.Add(filterShell);

            var listStatus = new TextBlock
            {
                Text = "Loading SteamGridDB choices…",
                Foreground = Brush("#9FB0BA"),
                FontSize = 12.5,
                Margin = new Thickness(2, 0, 0, 10)
            };
            Grid.SetRow(listStatus, 1);
            listHost.Children.Add(listStatus);

            var list = new ListBox
            {
                Background = Brush("#12191E"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Padding = new Thickness(12),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(list, 2);
            listHost.Children.Add(list);

            var pager = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(pager, 3);
            listHost.Children.Add(pager);

            var previousPageButton = Btn("← Previous", null, "#20343A", Brushes.White);
            previousPageButton.Height = 34;
            previousPageButton.Padding = new Thickness(12, 0, 12, 0);
            previousPageButton.Margin = new Thickness(0);
            pager.Children.Add(previousPageButton);

            var pageStatus = new TextBlock
            {
                Foreground = Brush("#8FA4B0"),
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetColumn(pageStatus, 1);
            pager.Children.Add(pageStatus);

            var nextPageButton = Btn("Next →", null, "#20343A", Brushes.White);
            nextPageButton.Height = 34;
            nextPageButton.Padding = new Thickness(12, 0, 12, 0);
            nextPageButton.Margin = new Thickness(0);
            Grid.SetColumn(nextPageButton, 2);
            pager.Children.Add(nextPageButton);

            var previewHost = new Grid();
            previewHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            previewHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(previewHost, 2);
            body.Children.Add(previewHost);

            var previewTitle = new TextBlock
            {
                Text = "Pick an asset",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };
            previewHost.Children.Add(previewTitle);

            var previewFrame = new Border
            {
                Background = Brush("#12191E"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 12, 0, 12)
            };
            Grid.SetRow(previewFrame, 1);
            previewHost.Children.Add(previewFrame);

            var previewStack = new Grid();
            previewStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewFrame.Child = previewStack;

            var previewImageShell = new Border
            {
                Background = Brush("#0D1216"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14),
                MinHeight = 320
            };
            var previewImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxHeight = 420
            };
            previewImageShell.Child = previewImage;
            previewStack.Children.Add(previewImageShell);

            var previewMeta = new TextBlock
            {
                Foreground = Brush("#D9E5EC"),
                FontSize = 13.5,
                Margin = new Thickness(2, 14, 2, 0),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(previewMeta, 1);
            previewStack.Children.Add(previewMeta);

            var previewDetail = new TextBlock
            {
                Foreground = Brush("#8FA4B0"),
                FontSize = 12.5,
                Margin = new Thickness(2, 10, 2, 0),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(previewDetail, 2);
            previewStack.Children.Add(previewDetail);

            var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            var footerHint = new TextBlock
            {
                Text = openIdEditor == null
                    ? "Search or filter without leaving this picker."
                    : "Search, filter, edit IDs, or reload without leaving this picker.",
                Foreground = Brush("#7E929E"),
                FontSize = 8.5,
                MaxWidth = 320,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 18, 0)
            };
            footer.Children.Add(footerHint);

            var buttons = new Grid { HorizontalAlignment = HorizontalAlignment.Right };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            var editIdsButton = Btn("Edit IDs...", null, "#20343A", Brushes.White);
            editIdsButton.Margin = new Thickness(0);
            editIdsButton.IsEnabled = openIdEditor != null;
            buttons.Children.Add(editIdsButton);

            var reloadButton = Btn("Reload Choices", null, "#20343A", Brushes.White);
            reloadButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(reloadButton, 1);
            buttons.Children.Add(reloadButton);

            var cancelButton = Btn("Cancel", null, "#334249", Brushes.White);
            cancelButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(cancelButton, 2);
            buttons.Children.Add(cancelButton);

            var chooseButton = Btn("Use Asset", null, "#275D47", Brushes.White);
            chooseButton.Margin = new Thickness(12, 0, 0, 0);
            chooseButton.IsEnabled = false;
            Grid.SetColumn(chooseButton, 3);
            buttons.Children.Add(chooseButton);

            void resetPreviewState(string heading, string meta, string detail)
            {
                previewTitle.Text = heading ?? string.Empty;
                previewMeta.Text = meta ?? string.Empty;
                previewDetail.Text = detail ?? string.Empty;
                previewImage.Source = null;
            }

            string SelectedFilterValue(ComboBox combo)
            {
                var value = combo == null ? string.Empty : (combo.SelectedItem as string ?? string.Empty);
                if (string.IsNullOrWhiteSpace(value) || value.StartsWith("All ", StringComparison.OrdinalIgnoreCase)) return string.Empty;
                return value.Trim();
            }

            bool SameChoice(SteamGridDbAssetChoice left, SteamGridDbAssetChoice right)
            {
                return string.Equals((left == null ? string.Empty : (left.DownloadUrl ?? string.Empty)).Trim(), (right == null ? string.Empty : (right.DownloadUrl ?? string.Empty)).Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(left == null ? string.Empty : (left.AssetKind ?? string.Empty), right == null ? string.Empty : (right.AssetKind ?? string.Empty), StringComparison.OrdinalIgnoreCase);
            }

            string BuildChoiceSearchText(SteamGridDbAssetChoice choice)
            {
                if (choice == null) return string.Empty;
                return string.Join("\n", new[]
                {
                    choice.AssetKind ?? string.Empty,
                    choice.AssetId ?? string.Empty,
                    choice.Style ?? string.Empty,
                    choice.Language ?? string.Empty,
                    choice.Author ?? string.Empty,
                    choice.Mime ?? string.Empty,
                    choice.Width > 0 && choice.Height > 0 ? choice.Width + "x" + choice.Height : string.Empty,
                    "score " + choice.Score.ToString()
                }.Where(bit => !string.IsNullOrWhiteSpace(bit)));
            }

            void SetComboItems(ComboBox combo, string allLabel, IEnumerable<string> values)
            {
                if (combo == null) return;
                var previous = combo.SelectedItem as string ?? allLabel;
                var items = new List<string> { allLabel };
                items.AddRange((values ?? Enumerable.Empty<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                combo.ItemsSource = items;
                combo.SelectedItem = items.FirstOrDefault(item => string.Equals(item, previous, StringComparison.OrdinalIgnoreCase)) ?? allLabel;
            }

            void PopulateFilters()
            {
                SetComboItems(kindFilter, "All types", allChoices.Select(choice => choice == null ? string.Empty : (choice.AssetKind ?? string.Empty)));
                SetComboItems(styleFilter, "All styles", allChoices.Select(choice => choice == null ? string.Empty : (choice.Style ?? string.Empty)));
                SetComboItems(languageFilter, "All languages", allChoices.Select(choice => choice == null ? string.Empty : (choice.Language ?? string.Empty)));
            }

            List<SteamGridDbAssetChoice> ApplyFilters()
            {
                IEnumerable<SteamGridDbAssetChoice> query = allChoices;
                var kind = SelectedFilterValue(kindFilter);
                var style = SelectedFilterValue(styleFilter);
                var language = SelectedFilterValue(languageFilter);
                var search = (searchBox.Text ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(kind))
                    query = query.Where(choice => string.Equals(choice == null ? string.Empty : (choice.AssetKind ?? string.Empty), kind, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(style))
                    query = query.Where(choice => string.Equals(choice == null ? string.Empty : (choice.Style ?? string.Empty), style, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(language))
                    query = query.Where(choice => string.Equals(choice == null ? string.Empty : (choice.Language ?? string.Empty), language, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(choice => BuildChoiceSearchText(choice).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

                return query.ToList();
            }

            void UpdateControlStates()
            {
                searchBox.IsEnabled = !isLoading;
                kindFilter.IsEnabled = !isLoading && kindFilter.Items.Count > 1;
                styleFilter.IsEnabled = !isLoading && styleFilter.Items.Count > 1;
                languageFilter.IsEnabled = !isLoading && languageFilter.Items.Count > 1;
                clearFiltersButton.IsEnabled = !isLoading
                    && (!string.IsNullOrWhiteSpace(searchBox.Text)
                    || !string.IsNullOrWhiteSpace(SelectedFilterValue(kindFilter))
                    || !string.IsNullOrWhiteSpace(SelectedFilterValue(styleFilter))
                    || !string.IsNullOrWhiteSpace(SelectedFilterValue(languageFilter)));
                previousPageButton.IsEnabled = !isLoading && currentPageIndex > 0;
                nextPageButton.IsEnabled = !isLoading && filteredChoices.Count > 0 && (currentPageIndex + 1) * pageSize < filteredChoices.Count;
                reloadButton.IsEnabled = !isLoading;
                editIdsButton.IsEnabled = !isLoading && openIdEditor != null;
                chooseButton.IsEnabled = !isLoading && list.SelectedItem is ListBoxItem;
            }

            void updateSelectedPreview()
            {
                var selectedItem = list.SelectedItem as ListBoxItem;
                var choice = selectedItem == null ? null : selectedItem.Tag as SteamGridDbAssetChoice;
                chooseButton.IsEnabled = choice != null;
                if (choice == null)
                {
                    resetPreviewState("Pick an asset", "Select a choice on the left to preview it.", string.Empty);
                    return;
                }

                previewTitle.Text = (choice.AssetKind ?? "Asset") + " preview";
                var detailBits = new List<string>();
                if (choice.Width > 0 && choice.Height > 0) detailBits.Add(choice.Width + " × " + choice.Height);
                if (!string.IsNullOrWhiteSpace(choice.Style)) detailBits.Add(choice.Style);
                if (!string.IsNullOrWhiteSpace(choice.Language)) detailBits.Add(choice.Language);
                if (!string.IsNullOrWhiteSpace(choice.Mime)) detailBits.Add(choice.Mime);
                previewMeta.Text = detailBits.Count == 0 ? (choice.AssetKind ?? "Asset") : string.Join(" · ", detailBits);

                var authorBits = new List<string>();
                if (!string.IsNullOrWhiteSpace(choice.Author)) authorBits.Add("By " + choice.Author);
                authorBits.Add("Score " + choice.Score);
                if (choice.Upvotes > 0 || choice.Downvotes > 0) authorBits.Add("+" + choice.Upvotes + " / -" + choice.Downvotes);
                previewDetail.Text = string.Join(" · ", authorBits.Where(bit => !string.IsNullOrWhiteSpace(bit)));
                SetRemoteImageSource(previewImage, string.IsNullOrWhiteSpace(choice.DownloadUrl) ? choice.PreviewUrl : choice.DownloadUrl, 900);
            }

            void RenderChoicePage(SteamGridDbAssetChoice preferredSelection = null)
            {
                filteredChoices = ApplyFilters();
                list.Items.Clear();
                if (filteredChoices.Count == 0)
                {
                    currentPageIndex = 0;
                    pageStatus.Text = allChoices.Count == 0 ? "No pages" : "No matching results";
                    listStatus.Text = allChoices.Count == 0 ? "No choices found" : "No matches";
                    chooseButton.IsEnabled = false;
                    if (allChoices.Count == 0)
                    {
                        resetPreviewState("No choices found", "SteamGridDB did not return any compatible assets for this selection.", openIdEditor == null ? "Try reloading the picker later." : "Try editing the game ID, then reload the picker.");
                    }
                    else
                    {
                        resetPreviewState("No matches", "Nothing matched the current search and filter settings.", openIdEditor == null ? "Clear the filters or adjust your search text." : "Clear the filters or edit IDs to try a different SteamGridDB entry.");
                    }
                    UpdateControlStates();
                    return;
                }

                if (preferredSelection != null)
                {
                    var preferredIndex = filteredChoices.FindIndex(choice => SameChoice(choice, preferredSelection));
                    if (preferredIndex >= 0) currentPageIndex = preferredIndex / pageSize;
                }

                var totalPages = Math.Max(1, (filteredChoices.Count + pageSize - 1) / pageSize);
                if (currentPageIndex >= totalPages) currentPageIndex = totalPages - 1;
                if (currentPageIndex < 0) currentPageIndex = 0;

                var pageItems = filteredChoices.Skip(currentPageIndex * pageSize).Take(pageSize).ToList();
                for (var i = 0; i < pageItems.Count; i++)
                {
                    var choice = pageItems[i];
                    var item = new ListBoxItem
                    {
                        Tag = choice,
                        Padding = new Thickness(0),
                        Margin = new Thickness(0, 0, 0, 10),
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent
                    };
                    item.Content = BuildSteamGridDbAssetChoiceRow(choice, currentPageIndex * pageSize + i + 1);
                    list.Items.Add(item);
                }

                var firstShown = currentPageIndex * pageSize + 1;
                var lastShown = Math.Min(filteredChoices.Count, firstShown + pageItems.Count - 1);
                pageStatus.Text = "Page " + (currentPageIndex + 1) + " of " + totalPages + " · " + firstShown + "-" + lastShown + " of " + filteredChoices.Count;
                listStatus.Text = filteredChoices.Count == allChoices.Count
                    ? filteredChoices.Count + " choice" + (filteredChoices.Count == 1 ? string.Empty : "s")
                    : filteredChoices.Count + " matching choice" + (filteredChoices.Count == 1 ? string.Empty : "s") + " of " + allChoices.Count;

                SteamGridDbAssetChoice targetChoice = null;
                if (preferredSelection != null) targetChoice = pageItems.FirstOrDefault(choice => SameChoice(choice, preferredSelection));
                if (targetChoice == null) targetChoice = pageItems.FirstOrDefault();
                if (targetChoice != null)
                {
                    foreach (var item in list.Items.OfType<ListBoxItem>())
                    {
                        if (SameChoice(item.Tag as SteamGridDbAssetChoice, targetChoice))
                        {
                            list.SelectedItem = item;
                            list.ScrollIntoView(item);
                            break;
                        }
                    }
                }
                UpdateControlStates();
            }

            async Task ReloadChoicesAsync(bool preserveSelection, string loadingHeading, string loadingMeta)
            {
                if (isLoading) return;
                isLoading = true;
                var selectedItem = list.SelectedItem as ListBoxItem;
                var rememberedSelection = preserveSelection && selectedItem != null ? selectedItem.Tag as SteamGridDbAssetChoice : null;
                list.Items.Clear();
                listStatus.Text = "Loading SteamGridDB choices…";
                pageStatus.Text = "Loading…";
                resetPreviewState(loadingHeading, loadingMeta, string.Empty);
                UpdateControlStates();
                try
                {
                    var choices = await loadChoicesAsync(loadCts.Token).ConfigureAwait(true);
                    if (!window.IsVisible) return;
                    allChoices = (choices ?? new List<SteamGridDbAssetChoice>())
                        .Where(choice => choice != null && !string.IsNullOrWhiteSpace(choice.DownloadUrl))
                        .ToList();
                    PopulateFilters();
                    currentPageIndex = 0;
                    RenderChoicePage(rememberedSelection);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (!window.IsVisible) return;
                    allChoices.Clear();
                    filteredChoices.Clear();
                    list.Items.Clear();
                    listStatus.Text = "Could not load choices";
                    pageStatus.Text = "No pages";
                    resetPreviewState("Could not load choices", ex.Message, openIdEditor == null ? "Check the SteamGridDB token and try again." : "Check the SteamGridDB token or edit IDs, then reload the picker.");
                }
                finally
                {
                    isLoading = false;
                    UpdateControlStates();
                }
            }

            Action<bool> closeWindow = delegate(bool accept)
            {
                if (accept)
                {
                    var selectedItem = list.SelectedItem as ListBoxItem;
                    if (selectedItem == null || !(selectedItem.Tag is SteamGridDbAssetChoice)) return;
                    selected = (SteamGridDbAssetChoice)selectedItem.Tag;
                    window.DialogResult = true;
                }
                else
                {
                    window.DialogResult = false;
                }
                window.Close();
            };

            list.SelectionChanged += delegate { updateSelectedPreview(); };
            list.MouseDoubleClick += delegate
            {
                if (list.SelectedItem is ListBoxItem) closeWindow(true);
            };
            searchBox.TextChanged += delegate
            {
                if (isLoading) return;
                currentPageIndex = 0;
                RenderChoicePage();
            };
            kindFilter.SelectionChanged += delegate
            {
                if (isLoading) return;
                currentPageIndex = 0;
                RenderChoicePage();
            };
            styleFilter.SelectionChanged += delegate
            {
                if (isLoading) return;
                currentPageIndex = 0;
                RenderChoicePage();
            };
            languageFilter.SelectionChanged += delegate
            {
                if (isLoading) return;
                currentPageIndex = 0;
                RenderChoicePage();
            };
            clearFiltersButton.Click += delegate
            {
                searchBox.Text = string.Empty;
                kindFilter.SelectedIndex = 0;
                styleFilter.SelectedIndex = 0;
                languageFilter.SelectedIndex = 0;
                currentPageIndex = 0;
                RenderChoicePage();
            };
            previousPageButton.Click += delegate
            {
                if (currentPageIndex <= 0) return;
                currentPageIndex--;
                RenderChoicePage();
            };
            nextPageButton.Click += delegate
            {
                if ((currentPageIndex + 1) * pageSize >= filteredChoices.Count) return;
                currentPageIndex++;
                RenderChoicePage();
            };
            cancelButton.Click += delegate { closeWindow(false); };
            chooseButton.Click += delegate { closeWindow(true); };
            reloadButton.Click += async delegate
            {
                await ReloadChoicesAsync(true, "Refreshing…", "Reloading SteamGridDB choices for this game.").ConfigureAwait(true);
            };
            editIdsButton.Click += async delegate
            {
                if (openIdEditor == null) return;
                openIdEditor(window);
                await ReloadChoicesAsync(false, "Refreshing…", "Reloading SteamGridDB choices after ID changes.").ConfigureAwait(true);
            };
            window.Closed += delegate
            {
                try
                {
                    loadCts.Cancel();
                }
                catch
                {
                }
                try
                {
                    loadCts.Dispose();
                }
                catch
                {
                }
            };

            window.Loaded += async delegate
            {
                await ReloadChoicesAsync(false, "Loading…", "Fetching SteamGridDB choices for this game.").ConfigureAwait(true);
            };

            resetPreviewState("Pick an asset", "SteamGridDB choices will load here.", string.Empty);
            SetComboItems(kindFilter, "All types", Enumerable.Empty<string>());
            SetComboItems(styleFilter, "All styles", Enumerable.Empty<string>());
            SetComboItems(languageFilter, "All languages", Enumerable.Empty<string>());
            pageStatus.Text = "No pages";
            UpdateControlStates();
            window.Content = root;
            return window.ShowDialog() == true ? selected : null;
        }

        FrameworkElement BuildSteamGridDbAssetChoiceRow(SteamGridDbAssetChoice choice, int ordinal)
        {
            var thumbSize = GetSteamGridDbAssetThumbSize(choice);
            var shell = new Border
            {
                Background = Brush("#1A2329"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var imageShell = new Border
            {
                Width = thumbSize.Width,
                Height = thumbSize.Height,
                Background = Brush("#0D1216"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6)
            };
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            imageShell.Child = image;
            SetRemoteImageSource(image, string.IsNullOrWhiteSpace(choice.PreviewUrl) ? choice.DownloadUrl : choice.PreviewUrl, 280);
            grid.Children.Add(imageShell);

            var textStack = new StackPanel { Margin = new Thickness(0) };
            var headingBits = new List<string> { "#" + ordinal };
            if (!string.IsNullOrWhiteSpace(choice.AssetKind)) headingBits.Add(choice.AssetKind);
            if (!string.IsNullOrWhiteSpace(choice.Style)) headingBits.Add(choice.Style);
            textStack.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", headingBits.Where(bit => !string.IsNullOrWhiteSpace(bit))),
                Foreground = Brushes.White,
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            var metaBits = new List<string>();
            if (choice.Width > 0 && choice.Height > 0) metaBits.Add(choice.Width + " × " + choice.Height);
            if (!string.IsNullOrWhiteSpace(choice.Language)) metaBits.Add(choice.Language);
            if (!string.IsNullOrWhiteSpace(choice.Mime)) metaBits.Add(choice.Mime);
            textStack.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", metaBits.Where(bit => !string.IsNullOrWhiteSpace(bit))),
                Foreground = Brush("#B7C6D0"),
                FontSize = 12.5,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            var footBits = new List<string>();
            footBits.Add("Score " + choice.Score);
            if (!string.IsNullOrWhiteSpace(choice.Author)) footBits.Add(choice.Author);
            textStack.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", footBits.Where(bit => !string.IsNullOrWhiteSpace(bit))),
                Foreground = Brush("#8FA4B0"),
                FontSize = 11.5,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            Grid.SetColumn(textStack, 2);
            grid.Children.Add(textStack);
            shell.Child = grid;
            return shell;
        }

        (double Width, double Height) GetSteamGridDbAssetThumbSize(SteamGridDbAssetChoice choice)
        {
            var kind = choice == null ? string.Empty : (choice.AssetKind ?? string.Empty);
            if (string.Equals(kind, "Cover", StringComparison.OrdinalIgnoreCase)) return (86, 128);
            if (string.Equals(kind, "Banner", StringComparison.OrdinalIgnoreCase)) return (148, 84);
            if (string.Equals(kind, "Icon", StringComparison.OrdinalIgnoreCase)) return (72, 72);
            return (132, 64);
        }

        void SetRemoteImageSource(Image image, string url, int decodePixelWidth)
        {
            if (image == null) return;
            image.Source = null;
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                if (decodePixelWidth > 0) bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.CacheOption = BitmapCacheOption.OnDemand;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                image.Source = bitmap;
            }
            catch
            {
            }
        }

        async Task<string> DownloadSteamGridDbAssetChoiceToTempFileAsync(SteamGridDbAssetChoice choice, CancellationToken cancellationToken)
        {
            if (choice == null) return string.Empty;
            var sourceUrl = !string.IsNullOrWhiteSpace(choice.DownloadUrl) ? choice.DownloadUrl : choice.PreviewUrl;
            if (string.IsNullOrWhiteSpace(sourceUrl)) return string.Empty;
            var extension = ".png";
            try
            {
                var parsed = new Uri(sourceUrl, UriKind.Absolute);
                var parsedExtension = Path.GetExtension(parsed.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(parsedExtension)) extension = parsedExtension.ToLowerInvariant();
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            var tempPath = Path.Combine(cacheRoot, "steamgriddb-picker-" + Guid.NewGuid().ToString("N") + extension);
            using (var wc = new TimeoutWebClient())
            {
                wc.Encoding = System.Text.Encoding.UTF8;
                wc.TimeoutMilliseconds = Math.Max(15000, SteamRequestTimeoutMilliseconds);
                try
                {
                    wc.Headers[System.Net.HttpRequestHeader.UserAgent] = "PixelVault/" + AppVersion;
                }
                catch
                {
                }
                await wc.DownloadFileAsync(sourceUrl, tempPath, cancellationToken).ConfigureAwait(false);
            }
            return tempPath;
        }

        string BuildLibraryAssetPickerWindowTitle(LibraryAssetPickerKind pickerKind)
        {
            switch (pickerKind)
            {
                case LibraryAssetPickerKind.Cover:
                    return "Choose Cover";
                case LibraryAssetPickerKind.Banner:
                    return "Choose Banner";
                default:
                    return "Choose Logo / Icon";
            }
        }

        string BuildLibraryAssetPickerWindowSubtitle(LibraryBrowserFolderView view, LibraryAssetPickerKind pickerKind)
        {
            var scope = BuildLibraryBrowserActionScopeLabel(view);
            switch (pickerKind)
            {
                case LibraryAssetPickerKind.Cover:
                    return "SteamGridDB cover choices for \"" + scope + "\". Picking one saves it as this game's custom cover.";
                case LibraryAssetPickerKind.Banner:
                    return "SteamGridDB hero banner choices for \"" + scope + "\". Picking one saves it as this game's custom banner.";
                default:
                    return "SteamGridDB logo and icon choices for \"" + scope + "\". Picking one saves it as this game's custom logo.";
            }
        }
    }
}
