using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        Button LibraryBrowserBuildFolderTile(
            LibraryFolderInfo folder,
            int tileWidth,
            int tileHeight,
            bool showPlatformBadge,
            Action<LibraryFolderInfo> showFolder,
            Action renderTiles,
            Action<bool> refreshLibraryFoldersAsync,
            Action<List<LibraryFolderInfo>, string, bool, bool> runScopedCoverRefresh,
            Action<LibraryFolderInfo> openLibraryMetadataEditor)
        {
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
            if (showPlatformBadge)
            {
                var imageGrid = new Grid();
                imageGrid.Children.Add(CreateAsyncImageTile(
                    GetLibraryArtPathForDisplayOnly(folder),
                    CalculateLibraryFolderArtDecodeWidth(tileWidth),
                    tileWidth,
                    tileHeight,
                    Stretch.UniformToFill,
                    folder.PlatformLabel,
                    Brushes.White,
                    new Thickness(0),
                    new Thickness(0),
                    Brushes.Transparent,
                    new CornerRadius(0),
                    Brushes.Transparent,
                    new Thickness(0)));
                imageGrid.Children.Add(BuildLibraryTilePlatformBadge(folder.PlatformLabel));
                imageBorder.Child = imageGrid;
            }
            else
            {
                imageBorder.Child = CreateAsyncImageTile(
                    GetLibraryArtPathForDisplayOnly(folder),
                    CalculateLibraryFolderArtDecodeWidth(tileWidth),
                    tileWidth,
                    tileHeight,
                    Stretch.UniformToFill,
                    folder.PlatformLabel,
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
            tileStack.Children.Add(new TextBlock { Text = folder.PlatformLabel + " | " + folder.FileCount + " capture" + (folder.FileCount == 1 ? string.Empty : "s"), Foreground = Brush("#8FA4B0"), Margin = new Thickness(10, 0, 10, 10), FontSize = 10.5, Height = 16 });
            tile.Content = tileStack;
            tile.Click += delegate { showFolder(folder); };
            var contextMenu = new ContextMenu();
            var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
            openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };
            var setCoverItem = new MenuItem { Header = "Set Custom Cover..." };
            setCoverItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var pickedCover = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(pickedCover)) return;
                SaveCustomCover(folder, pickedCover);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                Log("Custom cover set for " + folder.Name + " | " + folder.PlatformLabel + ".");
            };
            var clearCoverItem = new MenuItem { Header = "Clear Custom Cover", IsEnabled = !string.IsNullOrWhiteSpace(CustomCoverPath(folder)) };
            clearCoverItem.Click += delegate
            {
                ClearCustomCover(folder);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                Log("Custom cover cleared for " + folder.Name + " | " + folder.PlatformLabel + ".");
            };
            var openFolderItem = new MenuItem { Header = "Open Folder" };
            openFolderItem.Click += delegate { OpenFolder(folder.FolderPath); };
            var editMetadataItem = new MenuItem { Header = "Edit Metadata" };
            editMetadataItem.Click += delegate { openLibraryMetadataEditor(folder); };
            var editIdsItem = new MenuItem { Header = "Edit IDs..." };
            editIdsItem.Click += delegate
            {
                OpenLibraryFolderIdEditor(folder, delegate
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
            var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art" };
            fetchFolderCoverItem.Click += delegate
            {
                showFolder(folder);
                runScopedCoverRefresh(new List<LibraryFolderInfo> { folder }, folder.Name + " | " + folder.PlatformLabel, true, false);
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
            LibraryFolderInfo info,
            Action renderSelectedFolder)
        {
            if (!SameLibraryFolderSelection(ws.Current, info))
            {
                ws.SelectedDetailFiles.Clear();
                ws.DetailSelectionAnchorIndex = -1;
                ws.DetailFilesDisplayOrder.Clear();
            }
            ws.PreserveDetailScrollOnNextRender = false;
            ws.PreservedDetailScrollOffset = 0;
            panes.ThumbScroll.ScrollToVerticalOffset(0);
            ws.Current = info;
            activeSelectedLibraryFolder = CloneLibraryFolderInfo(info);
            panes.DetailTitle.Text = info.Name;
            panes.DetailMeta.Text = info.FileCount + " item(s) | " + info.PlatformLabel + " | " + info.FolderPath;
            panes.PreviewImage.Source = null;
            panes.PreviewImage.Visibility = Visibility.Collapsed;
            var infoCapture = info;
            _ = Task.Run(() =>
            {
                try
                {
                    var artPath = GetLibraryArtPathForDisplayOnly(infoCapture);
                    var pathOk = !string.IsNullOrWhiteSpace(artPath) && File.Exists(artPath);
                    _ = libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                    {
                        if (!SameLibraryFolderSelection(ws.Current, infoCapture)) return;
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
                            }, true, delegate { return SameLibraryFolderSelection(ws.Current, infoCapture); });
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
