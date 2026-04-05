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
        FrameworkElement BuildLibraryBrowserDetailTitlePlatformBadge(string platformLabel)
        {
            var resolvedLabel = NormalizeConsoleLabel(platformLabel);
            if (string.IsNullOrWhiteSpace(resolvedLabel)) return null;
            var iconPath = ResolveLibrarySectionIconPath(resolvedLabel);
            var accent = LibrarySectionAccentBrush(resolvedLabel);
            var badge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(10),
                Background = Brush("#F6FAFC"),
                BorderBrush = accent,
                BorderThickness = new Thickness(1.1),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 6, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                badge.Child = new Image
                {
                    Source = LoadImageSource(iconPath, 60),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                badge.Child = new TextBlock
                {
                    Text = resolvedLabel == "Multiple Tags" ? "+" : "•",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("#1D2931"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
            }
            return badge;
        }

        void UpdateLibraryBrowserDetailTitleBadges(LibraryBrowserPaneRefs panes, LibraryBrowserFolderView view)
        {
            if (panes == null || panes.DetailTitleBadgePanel == null) return;
            panes.DetailTitleBadgePanel.Children.Clear();
            panes.DetailTitleBadgePanel.Visibility = Visibility.Collapsed;
            if (view == null)
            {
                return;
            }

            var labels = (view.PlatformLabels ?? new string[0])
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(NormalizeConsoleLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => PlatformGroupOrder(label))
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (labels.Count == 0 && !string.IsNullOrWhiteSpace(view.PrimaryPlatformLabel))
            {
                labels.Add(NormalizeConsoleLabel(view.PrimaryPlatformLabel));
            }

            foreach (var label in labels)
            {
                var badge = BuildLibraryBrowserDetailTitlePlatformBadge(label);
                if (badge != null) panes.DetailTitleBadgePanel.Children.Add(badge);
            }

            panes.DetailTitleBadgePanel.Visibility = panes.DetailTitleBadgePanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        Button LibraryBrowserBuildFolderTile(
            LibraryBrowserFolderView folder,
            int tileWidth,
            int tileHeight,
            bool showPlatformBadge,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action<bool> refreshLibraryFoldersAsync,
            Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh,
            Action<LibraryBrowserFolderView> openLibraryMetadataEditor,
            Action<string> libraryToast)
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
            tileStack.Children.Add(new TextBlock { Text = BuildLibraryBrowserFolderTileSubtitle(folder), Foreground = Brush("#8FA4B0"), Margin = new Thickness(10, 0, 10, 10), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, MaxHeight = 36 });
            tile.Content = tileStack;
            tile.Click += delegate { showFolder(folder); };
            var contextMenu = new ContextMenu();
            var openMyCoversItem = new MenuItem { Header = "Open My Covers Folder" };
            openMyCoversItem.Click += delegate { OpenSavedCoversFolder(); };
            var setCoverItem = new MenuItem { Header = "Set Custom Cover...", IsEnabled = actionFolders.Count > 0 };
            setCoverItem.Click += delegate
            {
                Directory.CreateDirectory(savedCoversRoot);
                var pickedCover = PickFile(string.Empty, "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*", savedCoversRoot);
                if (string.IsNullOrWhiteSpace(pickedCover)) return;
                foreach (var targetFolder in actionFolders) SaveCustomCover(targetFolder, pickedCover);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                libraryToast?.Invoke("Cover saved");
                Log("Custom cover set for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };
            var clearCoverItem = new MenuItem
            {
                Header = "Clear Custom Cover",
                IsEnabled = actionFolders.Any(targetFolder => !string.IsNullOrWhiteSpace(CustomCoverPath(targetFolder)))
            };
            clearCoverItem.Click += delegate
            {
                foreach (var targetFolder in actionFolders.Where(targetFolder => !string.IsNullOrWhiteSpace(CustomCoverPath(targetFolder)))) ClearCustomCover(targetFolder);
                showFolder(folder);
                if (renderTiles != null) renderTiles();
                libraryToast?.Invoke("Cover cleared");
                Log("Custom cover cleared for " + BuildLibraryBrowserActionScopeLabel(folder) + ".");
            };
            var openFolderItem = new MenuItem { Header = BuildLibraryBrowserOpenFoldersLabel(folder) };
            openFolderItem.Click += delegate
            {
                foreach (var folderPath in GetLibraryBrowserSourceFolderPaths(folder)) OpenFolder(folderPath);
            };
            var copyFolderPathItem = new MenuItem { Header = "Copy folder path" };
            copyFolderPathItem.Click += delegate
            {
                try
                {
                    var paths = GetLibraryBrowserSourceFolderPaths(folder).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (paths.Count == 0) return;
                    Clipboard.SetText(paths.Count == 1 ? paths[0] : string.Join(Environment.NewLine, paths));
                }
                catch (Exception ex)
                {
                    Log("Copy folder path failed. " + ex.Message);
                }
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
            var refreshThisFolderItem = new MenuItem { Header = "Refresh this folder", IsEnabled = actionFolders.Count > 0 };
            refreshThisFolderItem.Click += delegate
            {
                showFolder(folder);
                runScopedCoverRefresh(actionFolders, BuildLibraryBrowserActionScopeLabel(folder), true, false, false);
            };
            var reloadLibraryListItem = new MenuItem { Header = "Reload library folder list" };
            reloadLibraryListItem.Click += delegate
            {
                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
            };
            var fetchFolderCoverItem = new MenuItem { Header = "Fetch Cover Art", IsEnabled = actionFolders.Count > 0 };
            fetchFolderCoverItem.Click += delegate
            {
                showFolder(folder);
                runScopedCoverRefresh(actionFolders, BuildLibraryBrowserActionScopeLabel(folder), true, false, true);
            };
            contextMenu.Items.Add(openFolderItem);
            contextMenu.Items.Add(copyFolderPathItem);
            contextMenu.Items.Add(editMetadataItem);
            contextMenu.Items.Add(editIdsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(refreshThisFolderItem);
            contextMenu.Items.Add(reloadLibraryListItem);
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
            var selectionChanged = !SameLibraryBrowserSelection(ws.Current, info);
            if (!selectionChanged && ws.Current != null && info != null)
            {
                if (!string.Equals(ws.Current.Name ?? string.Empty, info.Name ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(ws.Current.PrimaryFolderPath ?? string.Empty, info.PrimaryFolderPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    selectionChanged = true;
                }
            }
            if (selectionChanged)
            {
                ws.SelectedDetailFiles.Clear();
                ws.DetailSelectionAnchorIndex = -1;
                ws.DetailFilesDisplayOrder.Clear();
                panes.PreviewImage.Source = null;
                panes.PreviewImage.Visibility = Visibility.Collapsed;
                panes.PreviewImage.Uid = Guid.NewGuid().ToString("N");
            }
            ws.ResetDetailRowsToLoadingOnNextRender = selectionChanged;
            ws.PreserveDetailScrollOnNextRender = false;
            ws.PreservedDetailScrollOffset = 0;
            if (ws.PendingRestoreDetailScrollAfterShow > 0.1d)
            {
                ws.PreserveDetailScrollOnNextRender = true;
                ws.PreservedDetailScrollOffset = ws.PendingRestoreDetailScrollAfterShow;
                ws.PendingRestoreDetailScrollAfterShow = 0;
            }
            else
            {
                panes.ThumbScroll.ScrollToVerticalOffset(0);
            }
            ws.Current = info;
            LogTroubleshooting("LibrarySelection",
                "selectionChanged=" + selectionChanged
                + "; resetDetailRows=" + ws.ResetDetailRowsToLoadingOnNextRender
                + "; restoreDetailScroll=" + ws.PreserveDetailScrollOnNextRender
                + "; detailScrollOffset=" + ws.PreservedDetailScrollOffset.ToString("0.0")
                + "; " + BuildLibraryBrowserTroubleshootingLabel(info));
            var displayFolder = BuildLibraryBrowserDisplayFolder(info);
            var actionFolder = GetLibraryBrowserPrimaryFolder(info) ?? displayFolder;
            activeSelectedLibraryFolder = CloneLibraryFolderInfo(actionFolder);
            panes.DetailTitle.Text = info.Name;
            UpdateLibraryBrowserDetailTitleBadges(panes, info);
            panes.DetailMeta.Text = BuildLibraryBrowserDetailMetaText(info, actionFolder);
            panes.OpenFolderButton.Content = BuildToolbarButtonContent("\uE8B7", BuildLibraryBrowserOpenFoldersLabel(info));
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
                        LogException("Library banner art | " + (infoCapture.Name ?? "?"), ex);
                        LogTroubleshooting("LibraryBannerArtFail",
                            "type=" + ex.GetType().FullName
                            + "; message=" + ex.Message
                            + "; exception=" + FormatExceptionForTroubleshooting(ex)
                            + "; " + BuildLibraryBrowserTroubleshootingLabel(infoCapture));
                    }));
                }
            });
            PersistLibraryBrowserLastSelection(info);
            renderSelectedFolder();
        }
    }
}
