using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        Border BuildLibraryBrowserDetailPlatformChip(string platformLabel)
        {
            var resolvedLabel = NormalizeConsoleLabel(platformLabel);
            var accent = LibrarySectionAccentBrush(resolvedLabel);
            return new Border
            {
                Background = Brush("#162129"),
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock
                {
                    Text = resolvedLabel,
                    Foreground = Brush("#D8E4EA"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            };
        }

        void UpdateLibraryBrowserDetailPlatformChips(LibraryBrowserPaneRefs panes, LibraryBrowserFolderView view)
        {
            if (panes == null || panes.DetailPlatformChipsPanel == null) return;
            panes.DetailPlatformChipsPanel.Children.Clear();
            if (!ShouldShowLibraryBrowserDetailPlatformChips(view))
            {
                panes.DetailPlatformChipsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var platformLabel in (view.PlatformLabels ?? new string[0])
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => PlatformGroupOrder(label))
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase))
            {
                panes.DetailPlatformChipsPanel.Children.Add(BuildLibraryBrowserDetailPlatformChip(platformLabel));
            }
            panes.DetailPlatformChipsPanel.Visibility = panes.DetailPlatformChipsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        Button LibraryBrowserBuildFolderTile(
            LibraryBrowserFolderView folder,
            int tileWidth,
            int tileHeight,
            bool showPlatformBadge,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action<bool> refreshLibraryFoldersAsync,
            Action<List<LibraryFolderInfo>, string, bool, bool> runScopedCoverRefresh,
            Action<LibraryBrowserFolderView> openLibraryMetadataEditor)
        {
            var displayFolder = BuildLibraryBrowserDisplayFolder(folder);
            var actionFolder = GetLibraryBrowserPrimaryFolder(folder) ?? displayFolder;
            var actionFolders = GetLibraryBrowserActionFolders(folder);
            var badgePlatformLabel = folder == null ? string.Empty : folder.PrimaryPlatformLabel;
            var showPlatformContext = ShouldShowLibraryBrowserPlatformContext();
            var showPlatformBadgeOnTile = showPlatformBadge && showPlatformContext && !string.IsNullOrWhiteSpace(badgePlatformLabel);
            var tileFallbackText = string.IsNullOrWhiteSpace(folder == null ? string.Empty : folder.Name)
                ? badgePlatformLabel
                : folder.Name;
            var tile = new Button
            {
                Width = tileWidth,
                Height = tileHeight + 76,
                Margin = new Thickness(0, 0, 14, 16),
                Padding = new Thickness(0),
                Background = Brush("#151E24"),
                BorderBrush = Brush("#25333D"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            tile.Template = BuildRoundedTileButtonTemplate();
            var tileStack = new StackPanel();
            var imageBorder = new Border { Width = tileWidth, Height = tileHeight, Background = Brush("#0E1418"), CornerRadius = new CornerRadius(18), ClipToBounds = true };
            if (showPlatformBadgeOnTile)
            {
                var imageGrid = new Grid();
                imageGrid.Children.Add(CreateAsyncImageTile(
                    GetLibraryArtPathForDisplayOnly(displayFolder),
                    CalculateLibraryFolderArtDecodeWidth(tileWidth),
                    tileWidth,
                    tileHeight,
                    Stretch.UniformToFill,
                    tileFallbackText,
                    Brushes.White,
                    new Thickness(0),
                    new Thickness(0),
                    Brushes.Transparent,
                    new CornerRadius(0),
                    Brushes.Transparent,
                    new Thickness(0)));
                imageGrid.Children.Add(BuildLibraryTilePlatformBadge(badgePlatformLabel));
                imageBorder.Child = imageGrid;
            }
            else
            {
                imageBorder.Child = CreateAsyncImageTile(
                    GetLibraryArtPathForDisplayOnly(displayFolder),
                    CalculateLibraryFolderArtDecodeWidth(tileWidth),
                    tileWidth,
                    tileHeight,
                    Stretch.UniformToFill,
                    tileFallbackText,
                    Brushes.White,
                    new Thickness(0),
                    new Thickness(0),
                    Brushes.Transparent,
                    new CornerRadius(0),
                    Brushes.Transparent,
                    new Thickness(0));
            }
            tileStack.Children.Add(imageBorder);
            tileStack.Children.Add(new TextBlock { Text = folder.Name, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.White, Margin = new Thickness(10, 12, 10, 3), FontWeight = FontWeights.SemiBold, FontSize = 13.5, Height = 34 });
            tileStack.Children.Add(new TextBlock { Text = BuildLibraryBrowserFolderTileSubtitle(folder), Foreground = Brush("#8FA4B0"), Margin = new Thickness(10, 0, 10, 10), FontSize = 10.5, Height = 16 });
            tile.Content = tileStack;
            tile.Click += delegate { showFolder(folder); };
            var contextMenu = new ContextMenu();
            var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
            openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };
            var setCoverItem = new MenuItem { Header = "Set Custom Cover...", IsEnabled = folder != null && !folder.IsMergedAcrossPlatforms };
            setCoverItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var pickedCover = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(pickedCover)) return;
                SaveCustomCover(actionFolder, pickedCover);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                Log("Custom cover set for " + BuildLibraryBrowserScopeLabel(folder) + ".");
            };
            var clearCoverItem = new MenuItem { Header = "Clear Custom Cover", IsEnabled = folder != null && !folder.IsMergedAcrossPlatforms && !string.IsNullOrWhiteSpace(CustomCoverPath(actionFolder)) };
            clearCoverItem.Click += delegate
            {
                ClearCustomCover(actionFolder);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                Log("Custom cover cleared for " + BuildLibraryBrowserScopeLabel(folder) + ".");
            };
            var openFolderItem = new MenuItem { Header = BuildLibraryBrowserOpenFoldersLabel(folder) };
            openFolderItem.Click += delegate
            {
                foreach (var folderPath in GetLibraryBrowserSourceFolderPaths(folder)) OpenFolder(folderPath);
            };
            var editMetadataItem = new MenuItem { Header = "Edit Metadata" };
            editMetadataItem.Click += delegate { openLibraryMetadataEditor(folder); };
            var editIdsItem = new MenuItem { Header = "Edit IDs...", IsEnabled = folder != null && !folder.IsMergedAcrossPlatforms };
            editIdsItem.Click += delegate
            {
                OpenLibraryFolderIdEditor(actionFolder, delegate
                {
                    showFolder(folder);
                    if (renderTiles != null) renderTiles();
                });
            };
            var refreshFolderItem = new MenuItem { Header = "Refresh Folder" };
            refreshFolderItem.Click += delegate
            {
                showFolder(folder);
                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
            };
            var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art", IsEnabled = actionFolders.Count > 0 };
            fetchFolderCoverItem.Click += delegate
            {
                showFolder(folder);
                runScopedCoverRefresh(actionFolders, BuildLibraryBrowserActionScopeLabel(folder), true, false);
            };
            contextMenu.Items.Add(openFolderItem);
            contextMenu.Items.Add(editMetadataItem);
            contextMenu.Items.Add(editIdsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(refreshFolderItem);
            contextMenu.Items.Add(fetchFolderCoverItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(openMyCoversItem);
            contextMenu.Items.Add(setCoverItem);
            contextMenu.Items.Add(clearCoverItem);
            tile.ContextMenu = contextMenu;
            return tile;
        }

        void LibraryBrowserShowSelectedFolder(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            LibraryBrowserFolderView info,
            Action renderSelectedFolder)
        {
            if (!SameLibraryBrowserSelection(ws.Current, info))
            {
                ws.SelectedDetailFiles.Clear();
                ws.DetailSelectionAnchorIndex = -1;
                ws.DetailFilesDisplayOrder.Clear();
            }
            ws.PreserveDetailScrollOnNextRender = false;
            ws.PreservedDetailScrollOffset = 0;
            panes.ThumbScroll.ScrollToVerticalOffset(0);
            ws.Current = info;
            var displayFolder = BuildLibraryBrowserDisplayFolder(info);
            var actionFolder = GetLibraryBrowserPrimaryFolder(info) ?? displayFolder;
            activeSelectedLibraryFolder = CloneLibraryFolderInfo(actionFolder);
            panes.DetailTitle.Text = info.Name;
            panes.DetailMeta.Text = BuildLibraryBrowserDetailMetaText(info, actionFolder);
            UpdateLibraryBrowserDetailPlatformChips(panes, info);
            panes.OpenFolderButton.Content = BuildToolbarButtonContent("\uE8B7", BuildLibraryBrowserOpenFoldersLabel(info));
            panes.PreviewImage.Source = null;
            panes.PreviewImage.Visibility = Visibility.Collapsed;
            var infoCapture = info;
            _ = Task.Run(() =>
            {
                try
                {
                    var artPath = GetLibraryArtPathForDisplayOnly(displayFolder);
                    var pathOk = !string.IsNullOrWhiteSpace(artPath) && File.Exists(artPath);
                    _ = libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                    {
                        if (!SameLibraryBrowserSelection(ws.Current, infoCapture)) return;
                        if (!pathOk)
                        {
                            panes.PreviewImage.Source = null;
                            panes.PreviewImage.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            QueueImageLoad(panes.PreviewImage, artPath, CalculateLibraryBannerArtDecodeWidth(), delegate(BitmapImage loaded)
                            {
                                panes.PreviewImage.Source = loaded;
                                panes.PreviewImage.Visibility = Visibility.Visible;
                            }, true, delegate { return SameLibraryBrowserSelection(ws.Current, infoCapture); });
                        }
                    }));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _ = libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Log("Library detail banner art resolve failed for " + (infoCapture.Name ?? "?") + ". " + ex.Message);
                    }));
                }
            });
            renderSelectedFolder();
        }
    }
}
